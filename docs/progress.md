# 进度看板

> **这是一张状态快照，不是计划。**
> 切片定义与顺序见 `docs/spec-v3.md` §17；本表只记录状态，不定义切片。
> 计划在 `docs/spec-v3.md`（做什么）和 `docs/decisions-project-selection.md`（为什么这么定）。
> 这张表只回答一个问题：现在做到哪了。

## 变更规则

- **状态可以随时变，也允许回退**（✅ → 🟡），回退时在「证据」列写清原因。
- **切片划分和顺序不在这里改**。要改切片，先改 spec 并在决策记录留痕——那是计划变更，不是进度变更。
- 一个切片验收通过 → 改状态，并在「证据」列写上可复核的东西；复核不了就不算通过。

## 状态语义

| 标记 | 含义 |
|---|---|
| ⬜ | 未开始 |
| 🟡 | 进行中，或只通过部分验收 |
| ✅ | 验收通过，且证据可复核 |
| ⚠️ | 曾经通过、现在回退（后面写原因） |

## 后端切片

| # | 切片 | 状态 | 验收依据（可复核） |
|---|---|---|---|
| S0 | 数据模型 v3（5 表） | ✅ | `db/schema.sql`，`SHOW CREATE TABLE` |
| S1 | 简历解析 + 证据锚点 | 🟡 | 12 锚点、quoteExact 全 true；真实简历未验 |
| S2 | JD 解析 + 锚点 + xunit 项目 | ✅ | 实测 7 锚点、7/7 原文命中；20 个单元测试；三方审计（测试/CR/静态）通过 |
| S3 | 加权匹配（45/25/20/10 + hardBlock） | ✅ | 实测 overall=78、hardBlock=False、6 条 items 分类正确；四项和=总分；缓存命中与 refresh 绕过均验证；42 个单元测试 |
| S4 | 出题（8 题，带来源与锚点） | ✅ | 实测 8 题按 plan 分配（5/1/2，随可出缺口数自适应）；gap 题只引用 missing 且原文命中的锚点；68 个单元测试 |
| S5 | 作答 / 策略追问 / 评分 + 声明核验 | ✅ | 实测追问→推进、重复提交 409、展示分/弱点/声明核验落库；97 个单元测试 |
| S6 | 报告（四维 + topRisks） | ⬜ | — |
| S7 | 弱点点统计 | ⬜ | — |
| S8 | 离线评测集（5 指标） | ⬜ | — |

## 前端切片

| # | 切片 | 状态 |
|---|---|---|
| F1 | 资料页（简历/JD 列表与编辑） | ⬜ |
| F2 | 匹配页（对齐表 + 加权分） | ⬜ |
| F3 | 练习页（单题流 + 追问 + 评分卡） | ⬜ |
| F4 | 报告页 | ⬜ |

## 质量

| 项 | 状态 | 说明 |
|---|---|---|
| xunit 测试项目 | ✅ | 20 个用例，`pwsh scripts/test.ps1` |
| Roslyn 零 warning | ✅ | `dotnet build --no-incremental -warnaserror` = 0/0 |
| `dotnet format` 校验 | ✅ | Api 与 Tests 均通过 |
| 评测集 5 指标 | ⬜ | 排在 S8 |

## 已知取舍

- **不引 `Microsoft.AspNetCore.Mvc.Testing` + EF InMemory**：控制器是薄壳，核心逻辑（校验、锚点、解析）已抽成纯函数并有单测，为 4 张表加测试宿主收益低于成本。故 Controller 的 502/取消分支目前只有手工验证。
- **`dotnet test` 不可用**：.NET 10 的 VSTest 入口已废弃，本机 testhost 还会因父进程句柄被拒崩溃。统一走 `pwsh scripts/test.ps1`（xunit v3 in-proc runner）。
- **锚点 `QuoteExact=false` 不丢弃**：JD 提示词已要求逐字复制（实测 7/7 命中），但服务端仍保留兜底。**S4 出题时必须只接受 `QuoteExact=true` 的 JD 锚点**，否则 gap 题会引用不存在的原文。
- **repair 路径无自动化测试**：`MatchService` / `QuestionGenerator` 的「首轮非法 → repair 成功」「两轮非法 → 抛异常」只有手工验证，要测需注入假 HTTP handler 并给测试项目铺 prompt 文件，成本高于收益，暂缓。
- **Controller 分支无测试**：`MatchesController` / `SessionsController` 的 404、409、502、缓存命中、落库顺序均只有手工验证；核心逻辑已抽成纯函数（planner / validator / strategy / store），控制器保持薄壳。
- **每题只保留最后一轮评分**：追问提交会覆盖初答的四维分与声明核验（`questions` 只有一列 `assessment_json`）。报告按最终分统计；若将来要区分初答/追问，需要加列。
- **匹配缓存只按输入指纹失效**：不做 TTL；简历/JD 重新解析后指纹变化会自动重算，refresh=true 可强制重算。旧行不删除，每次重算新增一行（量小，可接受）。
- **脱敏尚未实现**（spec §13）：调模型前应把姓名/手机号/邮箱/身份证替换掉，排在 S8 之前补，并加 `privacyMarkerHits` 用例。

## 怎么用

1. **一眼看整体**：状态列。
2. **看证据**：`git log --oneline`，一个切片一个提交；表里说完成、日志里找不到对应提交，就是没做完。
3. **看质量**：`pwsh scripts/test.ps1`、`dotnet build --no-incremental -warnaserror`、`dotnet format --verify-no-changes`。
4. 只有 `docs/spec-v3.md` §15 的 6 条验收全部通过，项目才算完成——这张表是过程，不是终点。
