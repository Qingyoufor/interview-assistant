using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Interview;
using InterviewAssistant.Api.Services.Match;

namespace InterviewAssistant.Api.Services.Report;

/// <summary>
/// 把一场练习的逐题结果聚合成报告（spec §14 / §17 S6）。
/// 纯计算：不调模型，输入全是已落库的数据，方便单测。
/// </summary>
public static class ReportBuilder
{
    private static readonly string[] Dimensions = Enum.GetNames<ScoreDimension>();
    private static readonly Dictionary<string, string> DimensionLabels = new(StringComparer.Ordinal)
    {
        ["structure"] = "结构",
        ["depth"] = "深度",
        ["evidence"] = "证据",
        ["delivery"] = "表达"
    };
    private const int MaxTopGaps = 3;
    private const int MaxStrengths = 5;
    private const int MaxWeaknesses = 5;

    public static SessionReport Build(Guid sessionId, IReadOnlyList<QuestionReport> questions, MatchResult match)
    {
        match.Normalize();

        var answered = questions.Where(q => q.Assessment is not null).ToList();

        // 已作答题目的缺分按 0 计，分母是「已作答数」——缺分不能把均分抬高
        var dimensions = Dimensions
            .Select(d =>
            {
                var values = answered.Select(q => q.Scores.GetValueOrDefault(d, 0)).ToList();
                return new DimensionAverage(d, values.Count == 0 ? 0 : Math.Round(values.Average(), 2), values.Count);
            })
            .ToList();

        var scored = dimensions.Where(d => d.Count > 0).ToList();
        var overall = scored.Count == 0 ? 0 : scored.Average(d => d.Average);
        var overallScore = (int)Math.Round(overall / 5 * 100);

        var weaknesses = answered
            .SelectMany(q => q.Assessment!.Weaknesses)
            .GroupBy(t => t, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(MaxWeaknesses)
            .Select(g => new WeaknessCount(g.Key, g.Count()))
            .ToList();

        var strengths = answered
            .SelectMany(q => q.Assessment!.Strengths)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxStrengths)
            .ToList();

        var topGaps = answered
            .Where(q => q.Source == "gap" && !string.IsNullOrWhiteSpace(q.Assessment!.Remediation))
            .Select(q => new { Question = q, Gap = new GapEvidence(RequirementOf(q, match), q.Assessment!.Remediation!.Trim(), q.Content) })
            .OrderBy(x => Severity(x.Gap.Requirement, match))
            .Select(x => x.Gap)
            .DistinctBy(g => g.Requirement)
            .Take(MaxTopGaps)
            .ToList();

        var weakestDimension = scored.Count == 0
            ? null
            : DimensionLabels.GetValueOrDefault(scored.OrderBy(d => d.Average).First().Dimension, "未知维度");

        return new SessionReport(
            sessionId,
            overallScore,
            match.OverallMatch,
            match.Breakdown,
            match.HardBlock,
            match.TopRisks,
            dimensions,
            questions.Count,
            answered.Count,
            weaknesses,
            topGaps,
            strengths,
            Summary(overallScore, weakestDimension, weaknesses, topGaps));
    }

    /// <summary>missing 的缺口排在 partial 前面，已消失的排最后。</summary>
    private static int Severity(string requirement, MatchResult match)
    {
        var item = match.Items.FirstOrDefault(i => i.Requirement == requirement);
        return item?.Verdict switch
        {
            MatchVerdict.Missing => 0,
            MatchVerdict.Partial => 1,
            _ => 2
        };
    }

    private static string RequirementOf(QuestionReport question, MatchResult match)
    {
        foreach (var anchorId in question.AnchorIds)
        {
            var item = match.Items.FirstOrDefault(i => i.JdAnchorId == anchorId);
            if (item is not null && !string.IsNullOrWhiteSpace(item.Requirement)) return item.Requirement;
        }

        return question.Content;
    }

    private static string Summary(
        int overallScore,
        string? weakestDimension,
        IReadOnlyList<WeaknessCount> weaknesses,
        IReadOnlyList<GapEvidence> topGaps)
    {
        if (weaknesses.Count == 0 && topGaps.Count == 0)
            return $"综合得分 {overallScore}，还没有足够的作答数据。";

        var dimension = string.IsNullOrWhiteSpace(weakestDimension) ? "暂无" : weakestDimension;
        var advice = topGaps.Count > 0
            ? $"最该补的是「{topGaps[0].Requirement}」"
            : $"建议先补「{(weaknesses.Count > 0 ? weaknesses[0].Tag : "薄弱项")}」相关证据";

        return $"综合得分 {overallScore}，最弱一维是{dimension}，{advice}。";
    }
}
