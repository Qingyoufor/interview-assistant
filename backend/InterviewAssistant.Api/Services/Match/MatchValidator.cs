using InterviewAssistant.Api.Models;

namespace InterviewAssistant.Api.Services.Match;

/// <summary>
/// 服务端对模型输出的硬校验。任何一条不通过都要 repair，不允许把可疑结果落库。
/// 纯函数，方便单测。
/// </summary>
public static class MatchValidator
{
    /// <summary>锚点类型 → 允许的 item 分类。防止把 j:req 塞进 responsibility 来绕过 hardBlock。</summary>
    private static readonly Dictionary<string, string> KindToCategory = new(StringComparer.Ordinal)
    {
        ["requirement"] = MatchCategory.MustHave,
        ["responsibility"] = MatchCategory.Responsibility
    };

    public static List<string> Validate(MatchResult result, IReadOnlyDictionary<string, string> jdAnchorKinds, IReadOnlySet<string> resumeAnchorIds)
    {
        var problems = new List<string>();
        result.Normalize();

        if (result.OverallMatch is < 0 or > 100)
            problems.Add($"overallMatch 必须在 0~100，当前 {result.OverallMatch}");

        CheckRange(problems, "mustHave", result.Breakdown.MustHave, MatchBreakdown.MustHaveMax);
        CheckRange(problems, "responsibilities", result.Breakdown.Responsibilities, MatchBreakdown.ResponsibilitiesMax);
        CheckRange(problems, "evidenceQuality", result.Breakdown.EvidenceQuality, MatchBreakdown.EvidenceQualityMax);
        CheckRange(problems, "bonus", result.Breakdown.Bonus, MatchBreakdown.BonusMax);

        var sum = result.Breakdown.Sum;
        if (Math.Abs(sum - result.OverallMatch) > 1)
            problems.Add($"四项分之和为 {sum}，与总分 {result.OverallMatch} 相差超过 1");

        if (result.Items.Count == 0)
            problems.Add("items 为空，至少要有 mustHave 类的对齐项");

        // match.md 硬规则 5：每条 mustHave / responsibility 都必须出现在 items 里
        var covered = result.Items.Select(i => i.JdAnchorId).ToHashSet(StringComparer.Ordinal);
        var missingAnchors = jdAnchorKinds.Keys.Where(id => !covered.Contains(id)).OrderBy(id => id).ToList();
        if (missingAnchors.Count > 0)
            problems.Add($"以下 JD 要求没有出现在 items 里：{string.Join("、", missingAnchors)}");

        foreach (var item in result.Items)
        {
            if (!MatchCategory.All.Contains(item.Category))
                problems.Add($"category 非法：{item.Category}");

            if (!MatchVerdict.All.Contains(item.Verdict))
                problems.Add($"verdict 非法：{item.Verdict}");

            if (!jdAnchorKinds.TryGetValue(item.JdAnchorId, out var kind))
            {
                problems.Add($"jdAnchorId 不存在：{item.JdAnchorId}");
            }
            else if (KindToCategory.TryGetValue(kind, out var expected) && item.Category != expected)
            {
                problems.Add($"{item.JdAnchorId} 的锚点类型是 {kind}，category 必须是 {expected}，当前 {item.Category}");
            }

            foreach (var id in item.ResumeAnchorIds.Where(id => !resumeAnchorIds.Contains(id)))
                problems.Add($"resumeAnchorId 不存在：{id}");

            // 非 missing 必须有证据，任何 category 都一样，否则等于允许无证据得分
            if (item.Verdict != MatchVerdict.Missing && item.ResumeAnchorIds.Count == 0)
                problems.Add($"{item.JdAnchorId} 判为 {item.Verdict}，但没有任何简历锚点作为证据");
        }

        var shouldHardBlock = result.Items.Any(i => i.Category == MatchCategory.MustHave && i.Verdict == MatchVerdict.Missing);
        if (result.HardBlock != shouldHardBlock)
            problems.Add($"hardBlock 应为 {shouldHardBlock}（存在 mustHave 且 missing 的项）");

        return problems;
    }

    private static void CheckRange(List<string> problems, string name, int value, int max)
    {
        if (value < 0 || value > max)
            problems.Add($"{name} 必须在 0~{max}，当前 {value}");
    }

    /// <summary>从锚点列表取出 id → kind 映射，供校验使用。</summary>
    public static Dictionary<string, string> KindsOf(IEnumerable<Anchor> anchors) =>
        anchors.ToDictionary(a => a.Id, a => a.Kind, StringComparer.Ordinal);

    public static IReadOnlySet<string> IdsOf(IEnumerable<Anchor> anchors) =>
        anchors.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
}
