using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Interview;

namespace InterviewAssistant.Tests;

public class StrategyEngineTests
{
    private static Assessment Good() => new()
    {
        Atoms = new AtomScores { Correctness = 4, Depth = 4, Specificity = 4, Ownership = 4, Metrics = 3, Tradeoffs = 3 },
        Display = new DisplayScores { Structure = 4, Depth = 4, Evidence = 4, Delivery = 4 }
    };

    [Fact]
    public void 正常回答推进下一题()
    {
        var decision = StrategyEngine.Decide(Good(), isGapQuestion: false, depth: 0);

        Assert.Equal(StrategyDecision.Advance, decision.Action);
    }

    [Fact]
    public void 事实错误优先追前置知识()
    {
        var assessment = Good();
        assessment.HasFactualError = true;

        var decision = StrategyEngine.Decide(assessment, false, 0);

        Assert.Equal(StrategyDecision.FollowUp, decision.Action);
        Assert.Equal(FollowUpAxis.Prerequisite, decision.Axis);
    }

    [Fact]
    public void 事实错误追到深度上限就推进()
    {
        var assessment = Good();
        assessment.HasFactualError = true;

        Assert.Equal(StrategyDecision.Advance, StrategyEngine.Decide(assessment, false, StrategyEngine.MaxDepth).Action);
    }

    [Fact]
    public void 已经追问过一次后_即使仍缺细节也推进()
    {
        var assessment = Good();
        assessment.Atoms.Specificity = 1;

        Assert.Equal(StrategyDecision.Advance, StrategyEngine.Decide(assessment, false, 1).Action);
    }

    [Fact]
    public void 回答与简历矛盾时要求核实()
    {
        var assessment = Good();
        assessment.ClaimChecks.Add(new ClaimCheck { Claim = "主导了整个项目", Verdict = ClaimVerdicts.Contradicted });

        var decision = StrategyEngine.Decide(assessment, false, 0);

        Assert.Equal(FollowUpAxis.Verification, decision.Axis);
    }

    [Fact]
    public void 缺少细节时追问量化口径()
    {
        var assessment = Good();
        assessment.Atoms.Specificity = 2;

        Assert.Equal(FollowUpAxis.Metrics, StrategyEngine.Decide(assessment, false, 0).Axis);
    }

    [Fact]
    public void 缺口题追一次边界()
    {
        var decision = StrategyEngine.Decide(Good(), isGapQuestion: true, depth: 0);

        Assert.Equal(FollowUpAxis.Boundary, decision.Axis);
    }

    [Fact]
    public void 缺口题已经追过一次就推进()
    {
        Assert.Equal(StrategyDecision.Advance, StrategyEngine.Decide(Good(), true, 1).Action);
    }

    [Fact]
    public void 事实错误优先于声明矛盾()
    {
        var assessment = Good();
        assessment.HasFactualError = true;
        assessment.ClaimChecks.Add(new ClaimCheck { Claim = "x", Verdict = ClaimVerdicts.Contradicted });

        Assert.Equal(FollowUpAxis.Prerequisite, StrategyEngine.Decide(assessment, false, 0).Axis);
    }

    [Fact]
    public void 正确性低于两分也触发前置追问()
    {
        var assessment = Good();
        assessment.Atoms.Correctness = 1;

        Assert.Equal(FollowUpAxis.Prerequisite, StrategyEngine.Decide(assessment, false, 0).Axis);
    }

    [Fact]
    public void 正确性等于二不触发前置追问()
    {
        var assessment = Good();
        assessment.Atoms.Correctness = 2;

        Assert.Equal(StrategyDecision.Advance, StrategyEngine.Decide(assessment, false, 0).Action);
    }

    [Fact]
    public void 矛盾被追问过一次后推进()
    {
        var assessment = Good();
        assessment.ClaimChecks.Add(new ClaimCheck { Claim = "x", Verdict = ClaimVerdicts.Contradicted });

        Assert.Equal(StrategyDecision.Advance, StrategyEngine.Decide(assessment, false, 1).Action);
    }

    [Fact]
    public void 缺口题缺细节时先追量化而不是边界()
    {
        var assessment = Good();
        assessment.Atoms.Specificity = 2;

        Assert.Equal(FollowUpAxis.Metrics, StrategyEngine.Decide(assessment, isGapQuestion: true, depth: 0).Axis);
    }
}
