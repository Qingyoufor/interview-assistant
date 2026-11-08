# AGENTS.md

1. 动手之前，先读 docs 里的《决策记录》。
2. 每完成一个小功能，先让我确认，再用 Git 存一次档，同时更新 `docs/progress.md`（状态可回退，回退要写原因）。
3. 没存档之前，不许大改代码。
4. 图片、音频、视频等不进 Git。

## 质量检查

以下由主 Agent 派发子 Agent 执行，结果回传主 Agent，主 Agent 不自行执行：

- 测试：单元 / 集成 / E2E（`dotnet test`）
- Code Review：设计、可维护性、规范符合度
- 静态分析 / Lint：Roslyn 分析器（`dotnet build`，要求零 warning）+ `dotnet format --verify-no-changes`；SonarQube 可选，需要本地服务时再开

回传：检查项、通过/失败、问题清单（文件:行号、规则ID、级别）。
blocker/critical 必修，否则不放行。

## 交差

遵循 prove-it skill，本文件不重复。

## 异常

子 Agent 超时/失败：记录、重试或上报，不得静默跳过。
