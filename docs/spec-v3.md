# Spec v3：面试陪练（简历 × JD 驱动）

> 定稿：2026-11-08。取代 v1 / v2（历史版本已删除）。
> 本版新增四个机制：加权匹配分、证据锚点、代码层追问策略、固定弱点词表；并加入迷你离线评测集。
> 范围外：部署（用户自理）、语音、多用户、自动投递、简历排版美化、多 agent 编排。

## 1. 一句话

把你的简历和目标 JD 放进去，它像一个读过你简历的面试官，专挑「你和这个岗位之间的缝隙」问，并且每道题都能说清「为什么问这个」。

## 2. 三个核心机制

1. **三态判断**：每条 JD 要求对齐简历后判 `strong / partial / missing`，决定出题、追问和报告结论。
2. **加权匹配分**：0～100 由四部分相加得到，不允许模型自由给分（§5）。
3. **证据锚点**：题目的每一条依据都指向解析阶段生成的锚点 ID，服务端可校验（§4）。

## 3. 主流程

```
① 录入简历 → ② 录入 JD → ③ 匹配分析 → ④ 生成题单 → ⑤ 逐题作答+策略追问 → ⑥ 报告
```

| 步骤 | 输入 | 输出 | 用户可干预 |
|---|---|---|---|
| ① | 粘贴文本或 PDF（前端 pdfjs 抽文本） | 候选人画像 + 锚点 | 修正识别错误的项目/技能 |
| ② | JD 文本 | 岗位要求 + 锚点 | 删除不关心的要求 |
| ③ | 画像 + 要求 | 对齐表 + 加权分 + 风险 | 标记「这条我确实不会」 |
| ④ | 对齐结果 | 8 题，带来源与锚点 | 换题、加题 |
| ⑤ | 逐题 | 追问 + 评分 + 声明核验 | 跳过、重答 |
| ⑥ | 全部记录 | 报告 | — |

## 4. 证据锚点（R2）

解析阶段由**代码**给每个可引用对象分配 ID，模型只能引用，不能自造：

```
r:proj:2   简历第 2 个项目
r:skill:5  简历第 5 项技能
r:exp:1    简历第 1 段工作经历
j:req:3    JD 第 3 条要求
j:resp:2   JD 第 2 条职责
```

锚点记录：`{id, kind, label, quote}`，`quote` 必须是原文片段（≤200 字），存在 `parsed_json.anchors[]`。

出题时每题返回 `anchorIds[]`，服务端校验：

- 所有 ID ∈ 该简历/JD 的合法锚点集合，否则触发 repair；
- `source=resume` 的题至少含 1 个 `r:` 锚点；
- `source=gap` 的题至少含 1 个 `j:` 锚点，且必须是 `verdict=missing` 的那条要求；
- `source=general` 允许为空。

前端把锚点渲染成「为什么问这个」：悬停显示 `quote` 原文。

## 5. 加权匹配分（R1）

```json
{
  "overallMatch": 72,
  "breakdown": {
    "mustHave":        32,   // 0-45 硬性：学历/年限/明确技术门槛
    "responsibilities": 22,  // 0-25 职责与经历的语义对应
    "evidenceQuality":  14,  // 0-20 个人贡献、工程深度、量化结果的可信度
    "bonus":            4    // 0-10 加分项
  },
  "hardBlock": false,        // 存在 mustHave 类且 verdict=missing 时为 true
  "items": [
    {
      "requirement": "熟悉 Kafka",
      "jdAnchorId": "j:req:3",
      "resumeAnchorIds": ["r:proj:2"],
      "evidence": "简历项目二用 RabbitMQ 做异步解耦",
      "verdict": "partial",
      "risk": "被问 Kafka 与 RabbitMQ 取舍时容易露怯"
    }
  ],
  "topRisks": ["Kafka 无实战", "无高并发量级数据"]
}
```

服务端校验：`mustHave + responsibilities + evidenceQuality + bonus` 与 `overallMatch` 误差 ≤ 1，否则 repair。
`hardBlock=true` 时报告顶部必须显示「这条硬性要求不满足，投递前先确认」，不得被平均分掩盖。

## 6. 追问策略表（R3）

**下一步做什么由代码决定，模型只负责措辞。** 每题深度上限 2。

| 条件（按顺序命中） | 动作 |
|---|---|
| 已答满 8 题 | `complete` |
| 有事实错误，或 `correctness ≤ 1` | 深度 < 2 → `prerequisite`（降难度问前置知识）；否则 `advance` |
| 存在 `contradicted` 声明 | 深度 < 2 → `follow_up(verification)` |
| `specificity ≤ 2`（无细节/无量化） | 深度 < 2 → `follow_up(metrics)` |
| 缺口题且深度 < 1 | `follow_up(boundary)`：问边界与失效场景 |
| 其余 | `advance`（下一题） |

模型只接收 `{action, axis, difficulty, depth}`，输出一句话追问或下一题，不得改变动作。

## 7. 评分与声明核验

四维展示分（1～5）：`structure / depth / evidence / delivery`。
内部原子分（1～5，供策略表使用）：`correctness / depth / specificity / ownership / metrics / tradeoffs`。

**声明核验**（借鉴 OfferPilot，回答「这段经历经不经得起追问」）：

| verdict | 含义 |
|---|---|
| `supported` | 回答提供了与简历锚点一致的支撑 |
| `unverified` | 简历提到，但回答没给出支撑 |
| `contradicted` | 回答与简历锚点冲突 |
| `not_in_material` | 回答新增了简历里没有的内容 |

硬规则：简历自述**不能**被升级为已核验事实；没有锚点支撑一律 `unverified` 或 `not_in_material`。
评估禁止按字数、关键词命中、术语数量打分——写进 prompt 硬约束，并作为评测集的一个 case。

## 8. 弱点词表（R4）

固定 25 个标签，模型只能从中选 1～4 个，服务端白名单过滤，非法丢弃：

```
自我介绍 转行动机 项目真实性 技术深度 性能优化 并发 数据库索引 事务
缓存一致性 消息队列 分布式 异常处理 测试 部署运维 系统设计 取舍权衡
量化结果 团队协作 沟通表达 学习能力 抗压 薪资期望 反问环节 算法基础 框架原理
```

统计只做 `GROUP BY tag`，保证跨场次可聚合。

## 9. 数据模型

```sql
CREATE TABLE resumes (
  id          CHAR(36)     PRIMARY KEY,
  user_id     VARCHAR(64)  NOT NULL DEFAULT 'local',
  title       VARCHAR(120) NOT NULL,
  raw_text    MEDIUMTEXT   NOT NULL,
  parsed_json JSON         NULL,          -- {profile, anchors[], weakPoints[]}
  created_at  DATETIME(3)  NOT NULL,
  KEY idx_r_user (user_id, created_at)
) ENGINE=InnoDB;

-- job_targets：parsed_json 增加 anchors[]
-- sessions     ：增加 resume_id CHAR(36)、match_json JSON（缓存匹配结果）
-- questions    ：增加 source ENUM('resume','gap','general')、
--                anchor_ids JSON、assessment_json JSON、claim_checks JSON
-- question_scores：不变
```

设计取舍：锚点、评估、核验结果都用 JSON 存在题目行上，一周内不做拆分表；分数仍然拆行，因为统计要聚合。

## 10. 接口

| 方法 | 路径 | 作用 |
|---|---|---|
| POST | `/api/resumes` | 传简历文本 → 画像 + 锚点 |
| GET | `/api/resumes` | 简历列表 |
| POST | `/api/job-targets` | 传 JD → 要求 + 锚点 |
| POST | `/api/matches` | `{resumeId, jobTargetId}` → 加权匹配结果（缓存到 session） |
| POST | `/api/sessions` | 开练，生成 8 题 |
| POST | `/api/sessions/{id}/questions/{qid}/answer` | 提交回答 → 触发策略 → 追问或下一题（SSE） |
| POST | `/api/sessions/{id}/questions/{qid}/followup` | 提交追问回答 → 评分 + 核验 |
| POST | `/api/sessions/{id}/complete` | 收尾报告 |
| GET | `/api/sessions/{id}` | 详情 |
| GET | `/api/stats/weakness` | 词表聚合统计 |

## 11. 模型契约（五类调用）

全部 `json_object` + `temperature 0.2`，`timeout 60s`，失败重试 2 次（指数退避），校验失败 repair 一次。

1. 简历解析 → `{profile:{summary,totalYears,skills[],projects[],experiences[]}, anchors[]}`
2. JD 解析 → `{title, level, responsibilities[], mustHave[], niceToHave[], focusAreas[], anchors[]}`
3. 匹配 → §5 结构
4. 出题 → `{questions:[{type, source, anchorIds[], content, intent}]}`
5. 评估 → `{atoms:{correctness,depth,specificity,ownership,metrics,tradeoffs}, display:{structure,depth,evidence,delivery}, claimChecks[], weaknesses[], improvements[], remediation?}`
6. 措辞生成（追问/下一题）→ `{text}`，受策略表约束

## 12. 迷你离线评测（R5）

10 个合成 case（junior/mid/senior × 知识/项目），跑完输出 `evals/report.json`。

| 指标 | 阈值 | 含义 |
|---|---|---|
| `duplicateQuestionRate` | ≤ 0.2 | 同场题目近似重复率 |
| `evidenceValidityRate` | ≥ 0.9 | 题目锚点合法率 |
| `gapCitationRate` | = 1.0 | 缺口题命中 JD 锚点的比例 |
| `privacyMarkerHits` | = 0 | 输出中出现隐私标记的次数 |
| `jsonRepairRate` | ≤ 0.3 | 需要 repair 的调用比例 |

实现为独立控制台项目 `backend/Qy.Eval`，不依赖数据库，直接调 service。
这是「证明 prompt 没退化」的唯一手段，也是作品集里最能打的一页。

## 13. 隐私

- 简历只存本地 MySQL，不写日志；
- 调模型前正则脱敏姓名/手机号/邮箱/身份证；
- 评测集里埋 `privateMarkers`，出现即失败；
- 提供「删除全部数据」。

## 14. 界面（4 页）

1. 资料：简历/JD 列表与解析结果（可编辑）
2. 匹配：对齐表（绿/黄/红）+ 加权分 + 硬性阻断提示
3. 练习：单题流，来源徽标 + 锚点原文、追问气泡、评分卡
4. 报告：加权分拆解、四维雷达、topRisks、最该补的 3 条证据

## 15. 验收

1. 真实简历 + 真实 JD → 对齐表每条都能落到一个锚点上。
2. 8 题里 ≥6 题带合法锚点；`gap` 题的锚点必须命中 JD 原文。
3. 匹配分四项之和等于总分，误差 ≤ 1。
4. 报告「最可能被问倒的 3 点」全部来自 `topRisks`。
5. 手机号/邮箱不出现在任何模型请求中。
6. 评测集五项指标全绿。

## 16. 不抄 / 不做

- 多 agent 编排（interviewer/assessor/reporter/planner）、WebSocket 协议、DDD 七层域划分
- 语音、多用户、部署运维、简历改写全稿、自动投递

## 17. 里程碑（切片定义，全项目唯一编号）

切片的划分与顺序属于**计划**，只在本节定义；`docs/progress.md` 只负责记录每个切片的状态，不再自定义编号。

| # | 切片 | 完成判据 |
|---|---|---|
| S0 | 数据模型 v3 | 5 张表与 §9 一致 |
| S1 | 简历解析 + 锚点 | 画像可用；锚点能在原文定位 |
| S2 | JD 解析 + 锚点 | 锚点尽量 100% 原文命中，否则记入已知取舍 |
| S3 | 加权匹配 | 四项分之和 = 总分（误差 ≤1）；`hardBlock` 生效；三态判定落库 |
| S4 | 出题 | 8 题；`source` 与 `anchorIds` 合法；`gap` 题只引用 `QuoteExact=true` 的 JD 锚点 |
| S5 | 作答 / 追问 / 评分 | 策略表动作正确；四维分与 `claimChecks` 落库 |
| S6 | 报告 | 加权分拆解 + 四维 + `topRisks` + 三条待补证据 |
| S7 | 弱点点统计 | 25 词表聚合，跨场次可用 |
| S8 | 离线评测 | 5 指标全部达标 |
| F1 | 资料页 | 简历/JD 列表与解析结果可编辑 |
| F2 | 匹配页 | 对齐表 + 加权分 + 硬性阻断提示 |
| F3 | 练习页 | 单题流 + 来源徽标 + 追问 + 评分卡 |
| F4 | 报告页 | 与 S6 输出对应 |

每个切片完成 = 能给出可复核的证据（命令、截图或数据），而不是“写完了”。
