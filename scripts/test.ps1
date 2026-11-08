param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Args
)

# xUnit v3 在本项目使用 in-proc runner（OutOfProcess 的 VSTest testhost 在 .NET 10 上已不可用，
# 且本机 testhost 打开父进程句柄会被拒绝）。所以统一用 dotnet run 执行测试。
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\backend\InterviewAssistant.Tests'
dotnet run --project $project -- @Args
exit $LASTEXITCODE
