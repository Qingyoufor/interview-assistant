# 后端详细介绍

> 面向「要讲清楚这个项目」的读者：面试官、未来的自己、接手的人。
> 代码在 `backend/`，接口契约在 `docs/spec-v3.md`，开发过程见 `docs/vibe-log.md`。

## 1. 一句话

输入一份简历 + 一份目标 JD，系统给出「简历哪里不够、为什么不够」的可验证对齐表，再据此生成 8 道带证据锚点的问题；回答被逐题评分、核验声明、按策略追问，最后产出可回看的报告。

## 2. 技术栈

| 层 | 选型 | 说明 |
|---|---|---|
| 运行时 | .NET 10（SDK 10.0.302） | 只能用 10.0.10 runtime |
| Web | ASP.NET Core Web API + Swagger | 控制器薄壳，逻辑在 service |
| ORM | EF Core 9.0.0 + Pomelo 9.0.0 | EF Core 10 没有 MySQL provider，故意钉 9 |
| 数据库 | MySQL 8.0.46，utf8mb4 | 6 张表，JSON 列存结构化结果 |
| 模型 | DeepSeek `deepseek-chat`，JSON 模式，temperature 0.2 | HttpClient 超时 90s，失败重试 2 次（指数退避），校验失败 repair 1 次 |
| 测试 | xunit v3（in-proc runner） | 151 个用例，`pwsh scripts/test.ps1` |
| 前端 | Vue 3 + Vite + naive-ui | 五个页面：资料 / 匹配 / 历史 / 练习 / 报告，路由级懒加载 |

## 3. 分层与目录

```
Controllers/            薄壳：解析请求、状态码、落库顺序；不写业务规则
  ResumesController     POST/GET 简历 → 解析
  JobTargetsController  POST/GET JD → 解析
  MatchesController     匹配（带缓存）、按 id 取历史
  SessionsController    开练：匹配 + 出题 + 落库；会话详情
  AnswersController     作答、追问（评估 + 策略）
  ReportsController     报告（GET 只读、POST complete 落库）
  StatsController       弱点统计
  SessionMapper         会话 DTO 组装、评估 JSON 解析（含脏数据兜底）
Services/               业务：每个文件一个职责，核心是纯函数
  AnchorLocator         锚点定位（行首优先、词边界、200 字/4 行上限）
  InputGuard            录入校验与截断（50~12000 字，标题 120）
  ResumeParser / JdParser   提示词调用 + 结果归一化 + 锚点构建
  Match/                MatchService(调模型+repair)、MatchValidator(硬校验)、
                        MatchStore(缓存/落库)、MatchResults(反序列化)
  Questions/            QuestionPlanner(配比)、QuestionGenerator(调用+repair)、
                        QuestionValidator(校验)、GeneratedQuestion
  Interview/            InterviewService(评估+追问)、AssessmentValidator、
                        StrategyEngine(动作策略)、WeaknessCatalog(25 词表)
  Report/               ReportBuilder(纯聚合)、ReportModels
  Stats/                WeaknessStatsBuilder(纯聚合)、WeaknessStatsModels
  Llm/                  DeepSeekClient(唯一出口)、Redactor(脱敏)、LlmJson
  Prompts/*.md          6 个提示词：resume-parse、jd-parse、match、
                        question-gen、assess、followup
Data/AppDbContext.cs    6 张表的映射（列名、ENUM、JSON、索引）
Models/                 EF 实体 + 评估/锚点等数据结构
```

一条设计原则贯穿全项目：**模型只负责判断和措辞，能不能用由代码校验**。所以校验器、配比、策略表、词表都是纯函数，可以脱离数据库和网络单测——151 个用例里绝大多数打的是这些纯函数。

## 4. 数据模型（6 张表）

| 表 | 关键列 | 作用 |
|---|---|---|
| `resumes` | `raw_text`、`parsed_json` | 简历原文 + 解析结果（`{profile, anchors[]}`） |
| `job_targets` | `jd_text`、`parsed_json` | JD 原文 + 解析结果（`{job, anchors[]}`） |
| `matches` | `input_hash`、`overall_match`、`hard_block`、`result_json` | 一次加权匹配结果；`input_hash` 是两侧解析结果指纹，用于缓存失效 |
| `sessions` | `resume_id`、`match_id`、`status`、`overall_score`(TINYINT)、`summary` | 一场练习 |
| `questions` | `ordinal`、`type`、`source`、`anchor_ids`、`answer`、`follow_up`、`follow_up_answer`、`assessment_json`、`claim_checks` | 题 + 作答 + 评估（JSON 存整份结果） |
| `question_scores` | `dimension`、`score` | 四维展示分，拆行存，方便跨场次聚合 |

设计取舍：**锚点、评估、核验都是 JSON 挂在题目行上**（一周内不做拆表）；但**分数拆行**，因为统计要 `GROUP BY dimension`。

## 5. 主链路

### 5.1 录入与解析

```
POST /api/resumes  {title, rawText}
  → InputGuard（<50 字拒绝，>12000 截断）
  → ResumeParser：prompt 里用 <resume> 包裹原文
      → 模型返回 {profile, ...} → 反序列化 → 归一化（null 兜底）
      → 没解析出经历/项目/技能 → 422 语义的错误
  → 代码构建锚点：r:exp:i / r:proj:i / r:skill:i
      → AnchorLocator 在原文里定位（行首优先 → 词边界 → 正文探测）
  → 落库 parsed_json
```

JD 同理，锚点是 `j:req:i`（硬性要求）和 `j:resp:i`（职责）。

### 5.2 匹配（加权，不让模型自由给分）

```
POST /api/matches {resumeId, jobTargetId, refresh?}
  → 查缓存：同一对简历/JD 最近一条，且 input_hash 一致 → 直接返回（Cached=true）
  → MatchService：prompt 用 <match_input> 包裹（JSON 化后把 < > 转义成 \u003C）
      → 模型给 items[]（每条 = 一个 JD 锚点 + verdict + 证据锚点）和 breakdown
  → MatchValidator：一串硬校验（见 §6.2）
      → 不过 → 带上问题清单 repair 一次 → 仍不过就抛错（不落可疑结果）
  → 落 matches 行
```

### 5.3 开练（出题）

```
POST /api/sessions {resumeId, jobTargetId}
  → 复用或计算匹配
  → QuestionPlanner.Plan：默认 8 题 = 简历 3 / 缺口 3 / 通用 2，
    缺口题上限受「可出缺口题的缺口数」限制（match 判 missing 且 JD 锚点原文命中）
  → QuestionGenerator：模型出题 → QuestionValidator 校验 → repair 一次
  → 落 sessions + questions（source/anchor_ids 一起存）
```

### 5.4 作答与追问

```
POST /api/sessions/{id}/questions/{qid}/answer
  → 组装 <assess_input>：题目、意图、来源、回答、简历+JD 锚点
  → InterviewService：评估 → AssessmentValidator → 不过 repair 一次
  → StrategyEngine 决定追问或推进（见 §6.4）
  → 追问文本由模型写，代码兜底文案；写 assessment_json + question_scores + 标记完成
POST .../followup → 同样的评估链路，depth=1（每题最多追问一次）
```

### 5.5 报告与统计

```
POST /api/sessions/{id}/complete   → 校验所有题都答完，计算并写回 overall_score/summary
                                     （答完最后一题时也会自动写回一次，历史列表才有分）
GET  /api/sessions/{id}/report     → 只读；未完成返回 409
GET  /api/stats/weakness           → 跨场次聚合 25 个弱点标签 + 四维均分
```

## 6. 核心机制

### 6.1 证据锚点

- 锚点 id 由代码分配（`r:proj:2`、`j:req:3`），模型只能引用、不能创造。
- `QuoteExact` 表示「这段原文真的在材料里定位到了」；定位不到时保留但标记 false。
- 单条 quote 上限 200 字 / 4 行，按字符硬截断且不切断 emoji 代理对。
- ASCII 技术名词按词边界匹配，避免 `Java` 命中 `JavaScript`。

### 6.2 加权匹配与硬校验

四项：`mustHave 45 / responsibilities 25 / evidenceQuality 20 / bonus 10`，四项之和必须等于总分（容差 1）。

校验器（`MatchValidator`）拒绝的典型情况：

- 分数越界、四项和不等于总分；
- `items` 为空，或 JD 的某些锚点没出现在 items 里（要求全覆盖）；
- `category` 与锚点类型不匹配（`j:req:` 只能 mustHave）；
- verdict 不是 strong/partial/missing；
- 非 missing 却没有简历锚点做证据；
- 存在 mustHave+missing 但 `hardBlock=false`（防止「明明硬性不达标却说没阻断」）。

### 6.3 出题配比与校验

- 配比：`resume 3 / gap 3 / general 2`，可出缺口数不足时向简历题倾斜。
- `gap` 题只能引用「match 判 missing 且 QuoteExact=true」的 JD 锚点——否则缺口题会引用不存在的原文。
- 简历题必须引用简历锚点；题目内容 <8 字判失败。

### 6.4 追问策略表

代码决定动作，模型只写措辞：

| 触发条件 | 方向 | 追问要点 |
|---|---|---|
| 有事实错误 / correctness ≤1 | Prerequisite | 先确认前置知识 |
| 有 contradicted 声明 | Verification | 与简历矛盾，要求核实 |
| specificity ≤2 | Metrics | 要量级、基线、对比 |
| gap 题且未追问过 | Boundary | 边界与失效场景 |
| 其他 | 推进下一题 | —— |

深度上限 1（`questions` 只存一个 `follow_up` 字段），超过上限一律推进。

### 6.5 评分与声明核验

- 原子分 6 项（correctness/depth/specificity/ownership/metrics/tradeoffs）供策略表用；展示分 4 项给用户看。
- `claimChecks` 四态：`supported / unverified / contradicted / not_in_material`；supported 与 contradicted 都必须有简历侧锚点，JD 锚点不能当证据。
- 弱点只从 25 个标签里选，服务端白名单过滤后再落库。

### 6.6 报告与统计

- 四维均分（1~5）→ 换算 0-100；已作答但缺分的题按 0 计，分母是已作答数（缺分不能抬高均分）。
- `topRisks` 来自匹配结果；「最该补的 3 条证据」按缺口严重度（missing 优先）排序去重。
- 弱点统计把 25 个标签恒定返回、缺数据补 0，并给出「命中题数 + 出现在几场」；四维均分与报告同口径。

### 6.7 隐私

`Redactor` 挂在 `DeepSeekClient` 唯一出口，6 个调用点全覆盖：

| 类型 | 覆盖 |
|---|---|
| 手机号 | 连续 11 位、空格/短横分隔、`+86` 前缀 |
| 邮箱 | 标准格式 |
| 身份证 | 18 位且第 7–14 位是合法生日（避免订单号误伤） |
| 姓名 | `姓名：`、行首 `名字/Name:`、`我叫…`、`张三，男` 这类首行格式 |

占位符不含原值（`【手机号】` 等），模型抄回输出也不泄露。`Project Name:`、`Company Name:` 刻意不替换。

### 6.8 离线评测

`backend/Qy.Eval` 跑 10 个合成 case（junior/mid/senior × 知识/项目），输出 `evals/report.json`：

| 指标 | 阈值 | 现状 |
|---|---|---|
| `duplicateQuestionRate` | ≤ 0.2 | 0 |
| `evidenceValidityRate` | ≥ 0.9 | 1.0 |
| `gapCitationRate` | = 1.0 | 1.0（10 道缺口题） |
| `privacyMarkerHits` | = 0 | 0 |
| `jsonRepairRate` | ≤ 0.3 | 0 |

## 7. 接口清单

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/api/resumes` | 传简历文本 → 画像 + 锚点 |
| GET | `/api/resumes` | 简历列表 |
| GET | `/api/resumes/{id}` | 简历详情（原文 + 画像 + 锚点） |
| DELETE | `/api/resumes/{id}?force=` | 删简历；被引用时 409，`force=true` 连带删会话/匹配 |
| POST | `/api/job-targets` | 传 JD → 要求 + 锚点 |
| GET | `/api/job-targets` | JD 列表 |
| GET | `/api/job-targets/{id}` | JD 详情（原文 + 要求 + 锚点） |
| DELETE | `/api/job-targets/{id}?force=` | 删 JD；被引用时 409，`force=true` 连带删会话/匹配 |
| GET | `/api/sessions` | 历史面试列表（岗位/简历/状态/分数/进度） |
| POST | `/api/matches` | 加权匹配（`refresh=true` 绕过缓存） |
| GET | `/api/matches/{id}` | 按 id 取历史匹配 |
| POST | `/api/sessions` | 开练，生成 8 题 |
| GET | `/api/sessions/{id}` | 会话详情（含题目与匹配） |
| POST | `/api/sessions/{id}/questions/{qid}/answer` | 作答 → 评估 → 追问或下一题 |
| POST | `/api/sessions/{id}/questions/{qid}/followup` | 追问回答 → 评估 → 推进 |
| POST | `/api/sessions/{id}/complete` | 收尾，写回总分与摘要 |
| GET | `/api/sessions/{id}/report` | 报告（只读，未完成 409） |
| GET | `/api/stats/weakness` | 25 标签跨场次聚合 + 四维均分 |
| GET | `/health` | 健康检查 |
| GET | `/` | 跳 Swagger |

## 8. 质量与验证

| 项 | 结果 |
|---|---|
| 单元测试 | 151 个，`pwsh scripts/test.ps1`（xunit v3 in-proc） |
| 构建 | `dotnet build --no-incremental -warnaserror` 三个项目 0 警告 0 错误 |
| 格式 | `dotnet format --verify-no-changes` 三个项目均通过 |
| 端到端 | 真实链路跑过：匹配 78 分（33+20+17+8）、8 题按 5/1/2 分配、报告 29 分、弱点统计 11 题/4 场 |
| 评测 | 10 case / 80 题 / 80 次调用，5 项指标全绿，repair 0 次 |
| 提交闸门 | `.githooks/pre-commit`：单文件 >20 KB 拒绝提交 |

## 9. 已知取舍（详见 `docs/progress.md`）

- 没有集成测试宿主：控制器是薄壳，逻辑在纯函数里，控制器分支只手工验证。
- 每题只保留最后一轮评分（追问会覆盖初答的四维分）。
- 匹配缓存只按输入指纹失效，不做 TTL，旧行不删。
- 姓名脱敏是正则不是 NER，自由文本里的裸姓名会漏。
- 弱点统计包含未完成场次，与报告口径不同。
- 评测里 evidence/gap 两项在成功 case 上恒为 1，只能当回归冒烟。
- 前端 F1–F4 已做（`frontend/`，Vue 3 + naive-ui，路由级懒加载）；「删除全部数据」接口未做；无鉴权（单用户 `local`）。

## 10. 面试可以怎么讲

1. **为什么加权而不是让模型打分**：模型给分不可复核、不可聚合；拆成四项后，每一项都能落到具体的 JD 锚点和简历锚点上，服务端还能校验四项和等于总分。
2. **证据锚点解决什么问题**：把「为什么问这道题」变成可验证的引用；缺口题只能引用原文里真实存在的那句话，杜绝模型编造要求。
3. **为什么追问由代码决定**：模型容易跑偏或复读；策略表把「什么时候追问、追问什么方向」写成规则，模型只负责措辞，行为可测。
4. **校验失败怎么办**：一次 repair（把问题清单回传给模型），仍失败就抛错，不落可疑数据；线上表现为 502 + 通用错误文案。
5. **怎么证明 prompt 没退化**：离线评测集，5 个指标有阈值，跑批器退出码进 CI 也不难；首轮评测就抓出两个真实缺陷。
6. **成本控制**：JSON 模式 + temperature 0.2 + 60s 超时 + 重试 2 次；匹配结果按输入指纹缓存；评测按 case 抽样。
7. **隐私**：唯一出口脱敏 + 简历不写日志 + 评测埋隐私标记断言 0 命中。
8. **一周怎么做完的**：先定 spec（接口、数据、校验、验收），再按切片实现，每片都要过「测试 / Code Review / 静态分析」三关，每片一个提交、一个进度记录。
