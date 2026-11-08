using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Match;

namespace InterviewAssistant.Api.Services.Questions;

/// <summary>题量配比。默认 8 题：简历深挖 + 缺口 + 通用，缺口题上限受「可出缺口题的缺口」数量限制。</summary>
public sealed record QuestionPlan(int Resume, int Gap, int General, int Total);

public static class QuestionPlanner
{
    public const int DefaultTotal = 8;
    public const int MaxGap = 3;
    public const int MaxGeneral = 2;

    /// <summary>
    /// 可出缺口题的缺口：match 判为 missing、去重、且该 JD 锚点命中了原文（QuoteExact）。
    /// 未命中原文的锚点不能作为缺口题依据，否则校验必然失败。
    /// </summary>
    public static HashSet<string> ExactMissing(MatchResult match, IReadOnlyList<Anchor> jdAnchors)
    {
        match.Normalize();
        // 锚点正常不会重复，这里仍去重，避免脏数据让 ToDictionary 抛异常
        var byId = jdAnchors
            .GroupBy(a => a.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        return match.Items
            .Where(i => i.Verdict == MatchVerdict.Missing)
            .Select(i => i.JdAnchorId)
            .Where(id => byId.TryGetValue(id, out var anchor) && anchor.QuoteExact)
            .ToHashSet(StringComparer.Ordinal);
    }

    public static QuestionPlan Plan(MatchResult match, IReadOnlyList<Anchor> jdAnchors, int total = DefaultTotal)
    {
        total = Math.Max(0, total);
        var candidateGaps = ExactMissing(match, jdAnchors).Count;

        var gap = Math.Min(MaxGap, Math.Min(candidateGaps, total));
        var general = Math.Clamp(total - gap, 0, MaxGeneral);
        var resume = Math.Max(0, total - gap - general);

        return new QuestionPlan(resume, gap, general, total);
    }
}
