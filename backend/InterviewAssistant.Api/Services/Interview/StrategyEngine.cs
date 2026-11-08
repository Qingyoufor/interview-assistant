using InterviewAssistant.Api.Models;

namespace InterviewAssistant.Api.Services.Interview;

public enum FollowUpAxis { None, Prerequisite, Verification, Metrics, Boundary }

public sealed record StrategyDecision(string Action, FollowUpAxis Axis, string Reason)
{
    public const string FollowUp = "follow_up";
    public const string Advance = "advance";

    public bool IsFollowUp => Action == FollowUp;
}

/// <summary>
/// 追问策略表（spec §6）：下一步做什么由代码决定，模型只负责措辞。
/// 深度 = 这道题已经追问过的次数。上限 1：questions 表只存一个 FollowUp，不允许二次追问。
/// </summary>
public static class StrategyEngine
{
    public const int MaxDepth = 1;

    public static StrategyDecision Decide(Assessment assessment, bool isGapQuestion, int depth)
    {
        assessment.Normalize();

        if (assessment.HasFactualError || assessment.Atoms.Correctness <= 1)
        {
            return depth < MaxDepth
                ? new StrategyDecision(StrategyDecision.FollowUp, FollowUpAxis.Prerequisite, "存在事实错误，先确认前置知识")
                : new StrategyDecision(StrategyDecision.Advance, FollowUpAxis.None, "事实错误但已追到深度上限");
        }

        if (assessment.ClaimChecks.Any(c => c.Verdict == ClaimVerdicts.Contradicted))
        {
            return depth < MaxDepth
                ? new StrategyDecision(StrategyDecision.FollowUp, FollowUpAxis.Verification, "回答与简历矛盾，要求核实")
                : new StrategyDecision(StrategyDecision.Advance, FollowUpAxis.None, "矛盾但已追到深度上限");
        }

        if (assessment.Atoms.Specificity <= 2)
        {
            return depth < MaxDepth
                ? new StrategyDecision(StrategyDecision.FollowUp, FollowUpAxis.Metrics, "回答缺少细节与量化")
                : new StrategyDecision(StrategyDecision.Advance, FollowUpAxis.None, "缺少细节但已追到深度上限");
        }

        if (isGapQuestion && depth < 1)
            return new StrategyDecision(StrategyDecision.FollowUp, FollowUpAxis.Boundary, "缺口题追问边界与失效场景");

        return new StrategyDecision(StrategyDecision.Advance, FollowUpAxis.None, "正常推进下一题");
    }
}
