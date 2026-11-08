# Spec v2：面试陪练（简历 × JD 驱动）

> 状态：**定稿**（2026-11-08，参数见 §14）。上一版（仅 JD）作废，核心输入为「个人简历 + 岗位 JD」。
> 范围外：部署（用户自理）、语音、多用户、自动投递、简历排版美化。

## 1. 一句话

把你的简历和目标 JD 放进去，它像一个读过你简历的面试官，专挑「你和这个岗位之间的缝隙」问。

## 2. 为什么是这个产品

通用题库谁都能背，网上到处都是；真正准备不了的，是「针对你这段经历、这个岗位」的问题。
所以产品的全部价值在**对齐**：JD 的每一条要求，对应简历里的哪段证据；哪一条没证据——那就是面试官会追着问的地方。

## 3. 主流程

```
① 录入简历 → ② 录入 JD → ③ 匹配分析 → ④ 生成题单 → ⑤ 逐题作答+追问 → ⑥ 报告
```

| 步骤 | 输入 | 输出 | 用户可干预 |
|---|---|---|---|
| ① 简历录入 | 粘贴文本，或上传 PDF 由前端抽文本 | 候选人画像 | 可修正识别错误的项目/技能 |
| ② JD 解析 | JD 文本 | 岗位要求条目 | 可删掉不关心的要求 |
| ③ 匹配分析 | 画像 + 要求 | 逐条对齐表 + 匹配度 + 风险点 | 可标记「这条我确实不会」 |
| ④ 出题 | 对齐结果 + 配比 | 6~8 道带来源标注的题 | 可换题、可加题 |
| ⑤ 作答 | 逐题 | 追问 + 四维评分 | 可跳过、可重答 |
| ⑥ 报告 | 全部记录 | 匹配度复盘 + 最可能被问倒的 3 点 | — |

## 4. 核心概念：题目来源

每道题必须标注它从哪来，前端用徽标显示「为什么问这个」：

- `resume`：来自简历某段（例：你写了「用 Redis 做缓存」，追问命中率怎么测的）
- `gap`：JD 要求了但简历没有证据（例：要求 Kafka，你没写过）
- `general`：岗位通用/行为题（例：讲一次你和人争执的经历）

**验收标准**：同一份真实简历 + 真实 JD，生成的题里至少 6 道能明确指向简历的某一段或 JD 的某一条；`gap` 类题必须引用 JD 原文。

## 5. 匹配分析（产品的关键差异化）

逐条输出，而不是一个笼统分数：

```json
{
  "overallMatch": 72,
  "items": [
    {
      "requirement": "熟悉消息队列，有 Kafka 使用经验",
      "jdRef": "岗位职责第 2 条",
      "evidence": "简历项目二写了 RabbitMQ，未提及 Kafka",
      "verdict": "partial",          // strong | partial | missing
      "risk": "被问 Kafka 与 RabbitMQ 的取舍时容易露怯"
    }
  ],
  "topRisks": ["Kafka 无实战", "无高并发量级数据"]
}
```

`verdict` 三态是本产品的判断核心：`strong` 有直接证据、`partial` 有相邻经验、`missing` 完全没有。
出题的配比、报告的结论、追问的狠度，全部由它驱动。

## 6. 数据模型（在 v1 基础上加简历）

```sql
CREATE TABLE resumes (
  id          CHAR(36)     PRIMARY KEY,
  user_id     VARCHAR(64)  NOT NULL DEFAULT 'local',
  title       VARCHAR(120) NOT NULL,      -- 例：张三-后端-2026
  raw_text    MEDIUMTEXT   NOT NULL,
  parsed_json JSON         NULL,          -- 候选人画像
  created_at  DATETIME(3)  NOT NULL,
  KEY idx_r_user (user_id, created_at)
) ENGINE=InnoDB;

-- job_targets / questions / question_scores 沿用 v1，改动如下：
-- sessions  增加 resume_id CHAR(36) NULL（FK -> resumes.id）
-- questions 增加 source ENUM('resume','gap','general') NOT NULL
--           增加 ref_key VARCHAR(128) NULL  -- 指向简历项目名 或 JD 要求摘要
```

保留 v1 的设计取舍：分数拆行、所有表带 `user_id`、题目状态机 `asked → answered → scored`。

## 7. 模型调用契约（五类）

全部 `json_object` + `temperature 0.2`，服务端严格校验，失败重试 1 次后降级。

1. **简历解析** → `{summary, totalYears, skills[], projects:[{name, role, tech[], achievements[], metrics[]}], strengths[], gaps[]}`
2. **JD 解析** → `{title, level, responsibilities[], mustHave[], niceToHave[], focusAreas[]}`
3. **匹配分析** → `{overallMatch, items[], topRisks[]}`（结构见上）
4. **出题** → `{questions:[{type, source, refKey, content, intent}]}`
5. **评分** → `{structure, depth, evidence, delivery}` 每维 `{score 1~5, comment}` + `overall` + `improvements[]` + `referencePoints[]`

出题配比默认（8 题）：`resume 3 + gap 3 + general 2`，可调。

## 8. 隐私（必须实现，不是可选项）

- 简历只存本地 MySQL，**不写入任何日志**。
- 调模型前做脱敏：姓名、手机号、邮箱、身份证号用正则替换为 `[已隐藏]`；脱敏后文本才发给 DeepSeek。
- 提供「删除全部数据」的单按钮（单用户场景就是几条 DELETE）。
- 简历解析结果在界面上可编辑，避免模型误读被当成事实。

## 9. 失败与边界

| 情况 | 处理 |
|---|---|
| 简历过长（>12k 字） | 截断并提示「只分析前 12k 字」 |
| JD 过短（<50 字） | 拒绝并提示粘贴完整 JD |
| 匹配项 > 30 条 | 按 `missing` 优先截断到 20 条 |
| 模型返回非 JSON | 重试 1 次 → 降级提示，不静默 |
| 用户中途刷新 | 按 sessionId 拉回，题目状态恢复 |
| 简历是图片/扫描件 PDF | 前端 pdf.js 抽不到文本时提示「这是扫描件，请粘贴文字版」，不做 OCR |
| PDF 提取为空或乱码 | 回退到粘贴框，不阻塞流程 |

## 10. 界面（4 个页面，够用即可）

1. **资料**：简历列表 + JD 列表，各自「解析」按钮，解析结果可视化可编辑
2. **匹配**：选一份简历 + 一个 JD → 对齐表（绿/黄/红三色）+ 开始练习
3. **练习**：单题流，顶部进度与来源徽标，输入区，追问气泡，评分卡片
4. **报告**：匹配度、四维雷达、最可能被问倒的 3 点、改进建议清单

## 11. 评分维度（沿用 v1，含义补全）

| 维度 | 看什么 |
|---|---|
| 结构 | 有没有先给结论，再看分点 |
| 深度 | 是否讲到原理与取舍，而不只是「用了什么」 |
| 证据 | 有没有具体数字、场景、结果 |
| 表达 | 是否啰嗦、是否跑题 |

`gap` 类题额外输出 `remediation`：这道题对应的缺口，怎么补（学什么、简历怎么写）。
报告**不做**全篇简历改写，只把 `remediation` 汇总成「三条最该补的证据」。

## 12. 里程碑（按依赖顺序，不排天）

| 阶段 | 产出 | 判据 |
|---|---|---|
| S0 | 数据模型定稿 + 库表 | 五表建好，能插入一条假数据 |
| S1 | 简历/JD 解析 | 真实简历进去，画像看起来是对的 |
| S2 | 匹配分析 | 对齐表能让用户说「对，这条我确实没有」 |
| S3 | 出题 | 题目能引用简历段落和 JD 条目 |
| S4 | 作答/追问/评分 | 一场完整练习可走通 |
| S5 | 报告与统计 | 结论能被用户认可 |
| S6 | 前端打磨 | 错误/空/加载三态齐全 |

S1、S2 是效果风险段，先用 curl/Swagger 验证，不碰 UI。

## 13. 明确不做

- 部署与运维（用户自理）
- 多用户注册、团队协作
- 语音作答与实时语音
- 自动投递、简历模板排版
- 报告级简历改写（只在 gap 题给补短板建议）
- 面试录像回放

## 14. 已确认参数

| 项 | 取值 | 备注 |
|---|---|---|
| 简历录入 | 粘贴文本 + PDF 上传（前端 pdfjs-dist 抽文本） | 后端只收纯文本，不存文件 |
| 简历改写建议 | 仅 `gap` 题内给 `remediation` | 报告不外扩成全篇改写 |
| 题量与配比 | 8 题：`resume 3 + gap 3 + general 2` | `missing` 多时允许动态把 general 换成 gap |
| 评分维度 | structure / depth / evidence / delivery，1~5 | 沿用 v1 |
| 用户模型 | 单用户，`user_id = 'local'` | 表结构预留 |
| 模型 | DeepSeek `deepseek-chat`，`json_object`，`temperature 0.2` | 已验证可用 |
| 数据库 | MySQL 8 `qy_dev`，utf8mb4 | 已有实例 |
| 后端 | ASP.NET Core `net10.0` + EF Core 9 + Pomelo 9 | 已构建通过 |

验收（S1~S3 完成时要能过）：

1. 一份真实简历 + 一份真实 JD → 匹配表里每条都能对应到 JD 的某句或简历的某段。
2. 8 道题里 ≥6 道带明确来源标注；`gap` 题的 `refKey` 必须命中 JD 原文。
3. 报告顶部「最可能被问倒的 3 点」全部来自 `topRisks`，不允许是模型自由发挥。
4. 简历里的手机号/邮箱不出现在任何一次模型请求体里（抓包或日志可验证）。
