# MVP 设计：面试陪练（JD 驱动）

> 状态：草案，待用户确认后进入实现。配套决策见 `docs/decisions-project-selection.md`。

## 1. 范围

一条主流程，不做第二条：

```
粘贴 JD → 结构化抽取 → 生成 6~8 题 → 逐题作答 → AI 追问一次 → 四维评分 → 报告 → 弱点统计
```

明确不做：爬取 JD、语音作答、多用户注册、社交分享、付费、移动端适配。

## 2. 技术栈与版本（已按本机环境核定）

| 层 | 选型 | 说明 |
|---|---|---|
| 运行时 | .NET 10.0.302（本机仅装 10） | 只能 `net10.0`，没有 8 的运行时 |
| Web | ASP.NET Core Web API（Minimal API） | |
| ORM | EF Core 9.0.x + Pomelo.EntityFrameworkCore.MySql 9.0.0 | **EF Core 10 暂无 MySQL 提供程序**，必须钉住 9.x |
| 数据库 | MySQL 8.0（本机已装，服务名 MySQL80） | utf8mb4，避免中文乱码 |
| 前端 | Vue 3 + Vite + TypeScript + Pinia + Naive UI | |
| 模型 | DeepSeek `deepseek-chat`，OpenAI 兼容接口 | base_url `https://api.deepseek.com`，支持 JSON 输出与 SSE |
| 流式 | SSE（`text/event-stream`） | 前端用 fetch + ReadableStream，不用 EventSource（它不支持 POST） |

> 备选：若 EF Core 9 在 net10 上出现兼容问题，退到 **Dapper + MySqlConnector 2.6.2**，手写 SQL，四张表不值得为 ORM 折腾。

## 3. 数据模型（MySQL 8，utf8mb4_0900_ai_ci）

```sql
CREATE TABLE job_targets (
  id            CHAR(36)     PRIMARY KEY,
  user_id       VARCHAR(64)  NOT NULL DEFAULT 'local',
  title         VARCHAR(120) NOT NULL,
  level         VARCHAR(32)  NULL,
  jd_text       MEDIUMTEXT   NOT NULL,
  parsed_json   JSON         NULL,          -- {skills[], focusAreas[], difficulty}
  created_at    DATETIME(3)  NOT NULL,
  KEY idx_jt_user (user_id, created_at)
) ENGINE=InnoDB;

CREATE TABLE sessions (
  id             CHAR(36)     PRIMARY KEY,
  job_target_id  CHAR(36)     NOT NULL,
  user_id        VARCHAR(64)  NOT NULL DEFAULT 'local',
  status         ENUM('active','completed','abandoned') NOT NULL DEFAULT 'active',
  overall_score  DECIMAL(3,2) NULL,
  summary        TEXT         NULL,
  started_at     DATETIME(3)  NOT NULL,
  completed_at   DATETIME(3)  NULL,
  KEY idx_s_user (user_id, started_at),
  CONSTRAINT fk_s_jt FOREIGN KEY (job_target_id) REFERENCES job_targets(id)
) ENGINE=InnoDB;

CREATE TABLE questions (
  id               CHAR(36)    PRIMARY KEY,
  session_id       CHAR(36)    NOT NULL,
  ordinal          INT         NOT NULL,
  type             ENUM('basic','project','scenario','behavior') NOT NULL,
  content          TEXT        NOT NULL,
  intent           TEXT        NULL,        -- 出题意图，追问时复用
  answer           MEDIUMTEXT  NULL,
  follow_up        TEXT        NULL,
  follow_up_answer MEDIUMTEXT  NULL,
  feedback         TEXT        NULL,
  created_at       DATETIME(3) NOT NULL,
  UNIQUE KEY uk_q_ord (session_id, ordinal),
  CONSTRAINT fk_q_s FOREIGN KEY (session_id) REFERENCES sessions(id)
) ENGINE=InnoDB;

CREATE TABLE question_scores (
  id          CHAR(36)    PRIMARY KEY,
  question_id CHAR(36)    NOT NULL,
  dimension   ENUM('structure','depth','evidence','delivery') NOT NULL,
  score       TINYINT     NOT NULL,        -- 1~5
  comment     VARCHAR(255) NULL,
  UNIQUE KEY uk_qs (question_id, dimension),
  CONSTRAINT fk_qs_q FOREIGN KEY (question_id) REFERENCES questions(id)
) ENGINE=InnoDB;
```

设计取舍：
- 分数拆成行而不是塞 JSON，弱点统计直接 `GROUP BY dimension`，不用在 SQL 里解 JSON。
- 所有表带 `user_id`，当前固定 `local`；将来加登录不改表结构。
- 不给选项表、标签表，一周内用不上。

## 4. 接口清单

| 方法 | 路径 | 作用 |
|---|---|---|
| POST | `/api/job-targets` | 传 JD，抽取并保存，返回结构化岗位 |
| GET | `/api/job-targets` | 最近分析过的 JD |
| POST | `/api/sessions` | 用 jobTargetId 开一场练习，生成题目，返回第一题 |
| POST | `/api/sessions/{id}/questions/{qid}/answer` | 提交回答；若需追问则返回追问文本（SSE 流式） |
| POST | `/api/sessions/{id}/questions/{qid}/followup` | 提交追问回答，返回四维评分 + 反馈 + 下一题 |
| POST | `/api/sessions/{id}/complete` | 收尾，生成总结 |
| GET | `/api/sessions/{id}` | 会话详情（含全部题目与分数） |
| GET | `/api/sessions` | 历史列表（分页） |
| GET | `/api/stats/weakness` | 四维均分、最弱题型、最近 N 次趋势 |

状态机（每道题）：`asked → answered → scored`。前端只按状态渲染，不做隐式推断。

## 5. 模型输出契约（必须严格校验）

四类调用，全部 `response_format: {type:"json_object"}`，`temperature: 0.2`：

1. **JD 抽取** → `{title, level, skills[], focusAreas[], difficulty}`
2. **出题** → `{questions:[{type, content, intent}]}`（6~8 题，四种类型都要有）
3. **追问判断** → `{needsFollowUp, question, probeFor}`
4. **评分** → `{structure:{score,comment}, depth:{...}, evidence:{...}, delivery:{...}, overall, strengths[], improvements[], referencePoints[]}`

可靠性要求：
- 服务端反序列化校验，失败重试 1 次（追加“只输出 JSON，不要解释”）；二次失败返回 `degraded: true`，前端显示“评分失败，可重试”，**不要静默吞掉**。
- 单次调用 `max_tokens` 上限、`timeout` 60s、失败重试 2 次（指数退避）。
- 每日调用计数上限，防止调试时把额度烧光。
- 所有 prompt 存 `backend/Services/Prompts/` 下的 `.md` 文件，不硬编码在代码里，方便迭代。

## 6. 工程结构

```
Qy/
├─ backend/Qy.Api/
│  ├─ Program.cs
│  ├─ Endpoints/        # JobTargets, Sessions, Stats
│  ├─ Services/Llm/     # DeepSeekClient, JdParser, QuestionGenerator, Scorer
│  ├─ Services/Prompts/ # *.md
│  ├─ Data/             # AppDbContext, Migrations
│  └─ Models/
├─ frontend/            # vite + vue3 + ts
│  └─ src/{views,components,stores,api}
├─ docs/
└─ README.md
```

## 7. 阶段（不按天，按依赖顺序）

| 阶段 | 产出 | 完成判据 |
|---|---|---|
| P0 地基 | 仓库结构、MySQL 库表、前后端各自能跑起来 | 前端页面能拿到后端一个假 JSON |
| P1 模型链路 | JD 抽取 + 出题 | 用 Swagger 传一段真 JD，能拿到 6~8 道像样的题 |
| P2 会话流 | 答题 → 追问 → 评分 → 落库 | 一场完整练习能从第一题走到最后一题 |
| P3 报告与统计 | 总结、四维趋势 | 历史页能看出“哪一维最差” |
| P4 前端打磨 | 加载态、错误态、空态 | 断网/模型报错时界面不白屏 |

P1、P2 是真正的风险段，建议先在前端之前用 Swagger/curl 把模型链路跑通，别等 UI 做完才发现 prompt 不行。

## 8. 上线前必须验证的失败路径

- DeepSeek 超时 / 429 / 余额不足 → 界面提示明确，不白屏
- 模型返回非 JSON → 重试 → 降级提示
- 用户中途刷新页面 → 会话可恢复（按 sessionId 拉取）
- 长 JD（>8k 字符）→ 截断策略与提示
- 单日调用超限 → 明确拒绝而不是静默降级
