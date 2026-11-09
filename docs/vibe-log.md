# 后端 vibe coding 流程总结（S0–S8）

> 这份文档只回答两件事：**这八个切片是怎么一步步做出来的**，以及**中间踩了哪些坑、怎么爬出来的**。
> 技术细节见 `docs/backend-deep-dive.md`；接口与验收标准见 `docs/spec-v3.md`。

## 1. 结论先说

- 8 个后端切片（S0–S8）全部完成，151 个单元测试、三项目零警告、格式检查通过、离线评测 5 项指标全绿。
- 真正省时间的不是「让 AI 写代码」，而是**先把契约写死**（数据模型、接口、校验规则、验收标准），再让 AI 按切片实现；每片都要过测试、Code Review、静态分析三关。
- 真正花时间的不是写代码，是**发现「模型输出不可靠」的每一种方式**，然后用代码兜住。八成的坑都在这一层。
- 最大的教训：**评测应该更早做**。它最后才上线，却一次性抓出两个 prompt 缺陷；如果 S4 就有它，后面会少返工好几次。

## 2. 我是怎么和 AI 配合的

### 2.1 规矩先写成文件

`AGENTS.md` 里只有四条（刻意保持极简）：

1. 动手之前先读 `docs/decisions-project-selection.md`（决策记录）；
2. 每完成一个小功能，先让用户确认，再用 Git 存档，同时更新 `docs/progress.md`；
3. 没存档之前，不许大改代码；
4. 图片、音频、视频不进 Git。

外加一节「质量检查」：测试 / Code Review / 静态分析**一律派子 Agent 执行**，主 Agent 不自己跑；Code Review 一次审完、最多 5 条、只报 blocker/critical/major；**每次检查新开子 Agent，不复用会话**（复用会把几百万 token 的上下文反复重发）。

### 2.2 一个切片的固定节奏

```
读决策记录 → 看 spec 对应章节 → 写实现 + 单测 → 存一次（-N 标记新文件）
→ 派三个子 Agent：测试 / Code Review / 静态分析
→ 主 Agent 一次性分诊：blocker/critical 必修，major 视成本修或记已知取舍
→ 用户说「通过」→ 更新 progress.md（写可复核证据）→ git commit
```

一个切片一个提交，提交信息统一写成 `feat: 某某（Sx：要点）`。进度表里的每一行都要求「证据可复核」：命令、数字、提交号，写不出来就不算通过。

### 2.3 三路质量检查各自抓什么

| 检查 | 实际抓到的问题（举例） |
|---|---|
| 测试 | `Assert.Single` 替代 `Assert.Equal(1, x.Count)`（xUnit2013）、新用例与实现语义不一致（隐私计数、近似重复阈值） |
| Code Review | 手机号带分隔符漏脱敏（critical）、`Project Name` 被当姓名、统计四维分母与报告相反、脏 JSON 会让统计页 500 |
| 静态分析 | `dotnet format` 的 WHITESPACE（缩进）、构建级 `-warnaserror` 零警告 |

三者分开跑还有个副作用：**串行构建**避免了 `obj/bin` 文件锁（MSB3021/MSB3027）。并行跑过两次，都会互相抢。

### 2.4 存档前不写代码的原则救过场

有几次「顺手改一下」的冲动（比如把统计口径改成 SQL GROUP BY、把 repair 次数加到 3），都因为「没存档不许大改」被推迟。事后看，其中一半是不必要的优化——这条规矩实际起的是**防膨胀**作用。

## 3. 八个切片各自做了什么、卡在哪

| # | 切片 | 产出 | 卡住的点（详见 §4） |
|---|---|---|---|
| S0 | 数据模型 | 6 张表、ENUM、JSON 列、索引 | `overall_score` 列类型选错（DECIMAL） |
| S1 | 简历解析 + 锚点 | `ResumeParser`、`AnchorLocator` | 锚点截断、`Java` 命中 `JavaScript`、CRLF |
| S2 | JD 解析 + 测试项目 | `JdParser`、xunit 项目、21 个用例 | `dotnet test` 在 .NET 10 不可用 |
| S3 | 加权匹配 | `MatchService/Validator/Store`、缓存指纹 | 模型分数不可信、校验器空引用、EF 10 无 MySQL provider |
| S4 | 出题 | `QuestionPlanner/Generator/Validator` | gap 题引用了未命中原文的锚点 |
| S5 | 作答/追问/评分 | `InterviewService`、`StrategyEngine`、`AssessmentValidator` | 模型返回 `{}` 也能过校验；contradicted 没锚点 |
| S6 | 报告 | `ReportBuilder`、`complete` 落库 | `GET /report` 有写副作用、半场报告能拿 100 分 |
| S7 | 弱点统计 | `WeaknessStatsBuilder`、`/api/stats/weakness` | 四维均分口径与报告不一致；脏 JSON 会 500 |
| S8 | 离线评测 | `Qy.Eval`、10 case、5 指标 | 首轮 5/10 失败；缺口题样本为 0 时自动绿灯 |

每个切片的验收证据都写在 `docs/progress.md` 里，例如：

- S3：实测 overall=78、hardBlock=False、6 条 items 分类正确、四项和=总分；
- S6：实测总分 29、拆分 33+20+17+8=78、半场返回 409；
- S7：11 道已评估题 / 4 场，技术深度 10 次（4 场），与 SQL 直算逐条一致；
- S8：10 case / 80 题 / 80 次调用，5 项指标全绿。

## 4. 踩过的坑（按类型）

### 4.1 环境与工具链

**Windows 沙箱 ACL 坏掉**
现象：任何写文件都报 `SetNamedSecurityInfoW failed (Win32 5)`，连 HTTPS 都报 `SEC_E_NO_CREDENTIALS`（后来确认是沙箱假象，不是证书问题）。
解决：跑诊断脚本定位到某个祖先目录 ACL 被改，备份后给当前用户补全控制权（备份留在 `D:\Project\dsh-acl-recovery\`）。
教训：先怀疑环境，再怀疑代码——同一段代码在修复后的环境里一次跑通。

**`dotnet test` 在 .NET 10 上不可用**
现象：VSTest 入口已移除，testhost 还会因为父进程句柄被拒直接崩。
解决：改用 xunit v3 的 in-proc runner，统一入口 `pwsh scripts/test.ps1`（内部 `dotnet run`）。
教训：工具链版本不匹配时，换入口比修工具快。

**EF Core 10 没有 MySQL provider**
现象：升级到 EF Core 10 后 Pomelo 还没有对应版本。
解决：钉 EF Core 9.0.0 + Pomelo 9.0.0，跑在 net10 上。
教训：**先确认 provider 再升框架**；这条写进了决策记录，避免以后又纠结一遍。

### 4.2 数据与契约

**`overall_score` 用 DECIMAL(3,2) 存 0-100**
现象：写 78 分直接 `Out of range value for column 'overall_score'`。
解决：改 `TINYINT`，实体改 `byte?`，老库需要手工 `ALTER`。
教训：列类型要在 S0 就按「最终取值范围」定，不能按「看起来像百分比」定。

**`GET /report` 有写副作用**
现象：GET 里顺手把总分写回 session，还能在没答完时生成「100 分的半场报告」。
解决：GET 只读且未完成返回 409；新增 `POST /complete` 负责落库。
教训：读写分离不是洁癖——GET 被浏览器预取、被前端重复调用时，副作用会变成脏数据。

**命名冲突 `Services.Match` vs `Models.Match`**
现象：`using` 两个命名空间后 `Match` 歧义。
解决：`using MatchRow = InterviewAssistant.Api.Models.Match;`（只在 store 里用别名）。

### 4.3 解析与锚点

**锚点 320 字上限在超长单行上失效**
现象：按行取 4 行的循环，遇到一整段没有换行的简历时一次吞到底，200 字上限形同虚设。
解决：`Block()` 末尾再按字符硬截断，且不切断 emoji 代理对（`SafeCut`）。

**`Java` 命中 `JavaScript`**
现象：技能锚点把 `JavaScript` 的段落错配给 `Java`。
解决：ASCII 技术名词按词边界匹配（前后不能是字母数字），中文仍按子串。

**CRLF 混进锚点原文**
现象：Windows 文本的 `\r` 进了 quote，前端显示与「原文命中」比对都会出问题。
解决：quote 生成时统一 `Replace("\r", "")`。

**gap 题引用了「不在原文里」的 JD 要求**
现象：模型转述的 JD 要求被当成锚点，缺口题引用了招聘方根本没写的话。
解决：抽出 `QuestionPlanner.ExactMissing`（missing ∧ 去重 ∧ `QuoteExact=true`）作为唯一口径，出题与校验共用。

### 4.4 模型输出不可靠（坑最多的一类）

**校验器空引用**
现象：模型返回 `"skills": null`、`items` 里混 `null`、`claimChecks:[null]`、`weaknesses` 为 null，校验器直接 NRE。
解决：每个模型返回的类型都有 `Normalize()`，先补 null、再过滤 null 元素，最后才校验。

**分数默认值太宽容**
现象：deserialize 后 int 默认 0，但某次把默认改成 3 之后，`{}` 也能通过「1~5」校验。
解决：默认回到 0，校验器强制 1~5；任何缺失都必须被 repair 修出来。

**`contradicted` 不需要锚点也能过**
现象：模型凭空写一条「与简历矛盾」就能触发追问。
解决：`supported` 与 `contradicted` 都必须带简历侧锚点，且不能用 JD 锚点。

**两次校验不过就 502**
现象：模型偶尔两次都不合规，接口直接失败。
决定：这是**故意的**——不落可疑数据；一次 repair 是成本与质量的平衡点。评测里 `jsonRepairRate` 就是盯这个的。

**prompt 注入**
现象：简历里写「忽略以上指令」就能让模型跑偏。
解决：所有输入用 `<jd>` / `<resume>` / `<match_input>` / `<question_input>` / `<assess_input>` / `<followup_input>` 包裹，并把内容里的 `<` `>` 转义成 `\u003C` / `\u003E`，材料无法伪造边界。

### 4.5 工程与维护

**`SessionsController` 涨到 452 行**
现象：一个文件塞了 7 个动作 + DTO + 映射，改一处要看全篇。
解决：按资源拆成 Sessions / Answers / Reports 三个控制器 + `SessionMapper`，装 20 KB 提交前钩子。拆完最大文件 9.2 KB。

**反序列化逻辑抄了三份，其中一份漏了 `Normalize`**
现象：三处各自 `JsonSerializer.Deserialize<MatchResult>`，其中一处忘了归一化，遇到脏数据就崩。
解决：收敛到 `MatchResults.Read` 一处。

**统计口径和报告不一致**
现象：报告里四维缺分按 0 计，统计里却把缺分题整题剔除，同一份数据两个页面对不上。
解决：统一成「缺分按 0、分母是参与题数」；维度名改用 `Enum.GetNames<ScoreDimension>()`，两边同一个来源。

**脏 `assessment_json` 会让统计页整页 500**
现象：`SessionMapper.ParseAssessment` 遇到类型不符的 JSON 直接抛，统计接口一次扫全表，一行坏数据就全挂。
解决：捕获 `JsonException` 返回 null，该题跳过、不计入统计。

### 4.6 隐私与评测

**手机号只认连续 11 位（critical）**
现象：`138 0013 8000`、`138-0013-8000`、`+8613800138000` 全部漏脱敏——真实简历常见写法能绕过验收第 5 条。
解决：正则支持分隔符与国家码，补 4 条用例。

**18 位订单号被当身份证**
现象：任何 18 位数字都被替换成【身份证】。
解决：加「第 7–14 位必须是合法生日」的校验；订单号、流水号不再误伤。

**`Project Name:` / `Company Name:` 被当姓名**
现象：英文 `Name:` 字段把项目名和公司名替换掉，解析结果与锚点全坏。
解决：`名字/Name` 只在行首算姓名字段；`姓名：` 保持任意位置。

**测试拿中文占位符去断言 HTTP body**
现象：`System.Text.Json` 把中文转义成 `\uXXXX`，`Assert.Contains("【手机号】")` 假失败。
解决：先 `JsonDocument` 解析出 `messages[1].content`，再对解码后的字符串断言。

**评测首轮 5/10 失败**
现象：`匹配结果两次校验未通过`。打开详细报错后看到两类：
1. 模型把「看着像加分项」的要求标成 `category=bonus`，但 `j:req:` 锚点只允许 mustHave；
2. 模型说 `hardBlock=false`，却同时给出 mustHave+missing 的项。

解决：`match.md` 写死两条——「j:req 写 mustHave、j:resp 写 responsibility，加分项不进 items」和「有 mustHave+missing 就必须 hardBlock=true，与总分无关」。重跑 10/10 通过。

**评估阶段模型拿 JD 锚点当证据**
现象：`声明「候选人负责…」只能用简历锚点作为证据，j:resp:0 是 responsibility`。
解决：`assess.md` 明确「evidenceAnchorIds 只能用 r:proj / r:exp / r:skill」；顺带补了「atoms 六项 + display 四项必须全给」「weaknesses 至少 1 个词表原词」。

**缺口题为 0 时自动绿灯**
现象：`gapCitationRate` 在没有缺口题的场次里返回 1.0，整轮可以「零样本全绿」。
解决：报告里每个 case 带 `gapQuestions`，总数为 0 直接判失败——第一次冒烟就是这么被拦下的。

**评测成本估低了**
现象：预估 ¥2～4，实际花了约 ¥9（含 6 次调试重跑）。
教训：每次重跑都要重走解析+匹配+出题+评估全链路，调试成本要按「全量跑一次」算；先用 `--cases 1` 定位，再全量。

## 5. 这套流程的代价

| 项 | 数字 |
|---|---|
| 后端切片 | 9 个（S0–S8） |
| 单元测试 | 151 个 |
| 提交 | 每片一个，另有若干 docs/refactor 提交 |
| 子 Agent 轮次 | 每个切片 3～6 轮（测试/CR/静态 + 返工复查） |
| 模型花费 | 开发期调用不多；评测跑了 6 次，余额从 ¥17.59 降到 ¥8.39 |
| 返工次数 | 主要集中在 S7 之后：统计口径、脱敏规则、评测两侧各返工 2 次 |

## 6. 复盘：哪些决定是对的

1. **先 spec 后代码**：接口、数据模型、校验规则、验收标准写死之后，AI 的产出可以被机械验证（151 个测试里大半是纯函数）。
2. **纯函数是核心**：校验器、配比、策略表、统计、报告都不碰数据库和网络，单测成本极低，返工也快。
3. **唯一出口**：模型调用只走 `DeepSeekClient`，脱敏、重试、超时都在这一处，6 个调用点不用各写一遍。
4. **代码管行为、模型管措辞**：追问策略表、白名单过滤、加权公式，都是「模型不能自由发挥」的地方。
5. **每片必存档**：回退成本低，出问题能定位到具体切片。

## 7. 复盘：哪些本可以更早做

1. **评测集提前到 S4**：它抓的正是出题/评估的 prompt 问题，越早越省返工。
2. **真实简历早点要**：S1 到现在还挂着「真实简历未验」。
3. **`InputGuard` 常量早该收一处**：两个控制器各写一遍，tidy 时才合并。
4. **脏数据兜底意识**：`assessment_json` 的 try/catch、`Normalize()` 都是踩过坑才补的，其实在 S3 就该成惯例。

## 8. 如果再来一遍，顺序会怎么排

```
S0 数据模型 → S1 解析+锚点（拿真实简历验）→ S2 JD 解析
→ S3 加权匹配 → S8 评测骨架（先跑通 1 个 case）
→ S4 出题 → S5 作答/追问/评分 → S6 报告 → S7 统计
→ 脱敏（在第一次真调模型前就做）→ 前端 F1–F4
```

核心改动只有两处：**评测骨架提前**（S3 之后）、**脱敏提前到第一次真调模型之前**。
