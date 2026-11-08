using Qy.Eval;

namespace InterviewAssistant.Tests;

public class EvalCalculatorTests
{
    private static EvalQuestion Q(string content, params string[] anchors) => new("resume", anchors, content);

    private static CaseRun Case(
        IEnumerable<EvalQuestion>? questions = null,
        IEnumerable<string>? knownAnchors = null,
        Dictionary<string, bool>? jdExact = null,
        IEnumerable<string>? outputs = null,
        int calls = 10,
        int repairs = 0) =>
        new("case",
            (questions ?? []).ToList(),
            (knownAnchors ?? []).ToHashSet(StringComparer.Ordinal),
            jdExact ?? new Dictionary<string, bool>(),
            (outputs ?? []).ToList(),
            calls,
            repairs);

    [Fact]
    public void 完全相同的两道题算重复()
    {
        var run = Case([Q("介绍一下你自己"), Q("介绍一下你自己")]);

        Assert.Equal(1, EvalCalculator.DuplicateRate([run]));
    }

    [Fact]
    public void 只差标点的两道题算重复()
    {
        Assert.True(EvalCalculator.IsNearDuplicate("请介绍一下你在项目里负责的部分", "请介绍一下你在项目里负责的部分？"));
    }

    [Fact]
    public void 只差一个词的长句算近似重复()
    {
        var left = "我在上家公司负责订单服务的开发和维护，用的是 Java 和 Spring Boot，日均订单五万笔";
        var right = "我在上家公司负责订单服务的开发和维护，用的是 Java 和 Spring Cloud，日均订单五万笔";

        Assert.True(EvalCalculator.IsNearDuplicate(left, right));
    }

    [Fact]
    public void 主题不同的两道题不算重复()
    {
        var run = Case([Q("介绍一下你自己"), Q("说说你对数据库索引的理解")]);

        Assert.Equal(0, EvalCalculator.DuplicateRate([run]));
    }

    [Fact]
    public void 锚点不在已知集合里会拉低合法率()
    {
        var run = Case(
            [Q("引用合法锚点的题", "r:proj:0"), Q("引用不存在锚点的题", "j:req:9")],
            knownAnchors: ["r:proj:0"]);

        Assert.Equal(0.5, EvalCalculator.EvidenceValidityRate([run]));
    }

    [Fact]
    public void 缺口题引用原文未命中的锚点算不达标()
    {
        var run = Case(
            [new EvalQuestion("gap", ["j:req:0"], "缺口题")],
            knownAnchors: ["j:req:0"],
            jdExact: new Dictionary<string, bool> { ["j:req:0"] = false });

        Assert.Equal(0, EvalCalculator.GapCitationRate([run]));
    }

    [Fact]
    public void 缺口题引用原文命中的锚点达标()
    {
        var run = Case(
            [new EvalQuestion("gap", ["j:req:0"], "缺口题")],
            knownAnchors: ["j:req:0"],
            jdExact: new Dictionary<string, bool> { ["j:req:0"] = true });

        Assert.Equal(1, EvalCalculator.GapCitationRate([run]));
    }

    [Fact]
    public void 没有缺口题时引用率视为达标()
    {
        var run = Case([Q("普通题")]);

        Assert.Equal(1, EvalCalculator.GapCitationRate([run]));
    }

    [Fact]
    public void 隐私标记每出现一次计一次()
    {
        var run = Case(outputs: ["联系 13800138000 或 13800138000", "邮箱 ABC@X.COM"]);

        Assert.Equal(2, EvalCalculator.PrivacyHits([run], ["13800138000"]));
        Assert.Equal(1, EvalCalculator.PrivacyHits([run], ["abc@x.com"]));
    }

    [Fact]
    public void repair率按模型调用次数算()
    {
        var run = Case(calls: 10, repairs: 2);

        Assert.Equal(0.2, EvalCalculator.RepairRate([run]));
    }

    [Fact]
    public void 五项指标都超阈值时全部列出()
    {
        var failures = EvalCalculator.Failures(new EvalMetrics(0.5, 0.5, 0.5, 3, 0.9));

        Assert.Equal(5, failures.Count);
    }

    [Fact]
    public void 指标达标时没有失败项()
    {
        Assert.Empty(EvalCalculator.Failures(new EvalMetrics(0.1, 1, 1, 0, 0.1)));
    }
}
