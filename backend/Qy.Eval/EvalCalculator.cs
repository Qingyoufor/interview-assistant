namespace Qy.Eval;

/// <summary>
/// spec §12 的五个指标，纯计算（不调模型、不碰文件），方便单测。
/// 阈值写在这里，脚本与报告共用同一份判据。
/// </summary>
public static class EvalCalculator
{
    public const double MaxDuplicateRate = 0.2;
    public const double MinEvidenceValidityRate = 0.9;
    public const double MinGapCitationRate = 1.0;
    public const int MaxPrivacyMarkerHits = 0;
    public const double MaxJsonRepairRate = 0.3;

    /// <summary>两道题的字二元组 Jaccard ≥ 0.75 就算近似重复（只换一个词的问句也能抓到）。</summary>
    private const double DuplicateThreshold = 0.75;

    public static EvalMetrics Compute(IReadOnlyList<CaseRun> cases, IReadOnlyList<string> markers) => new(
        Math.Round(DuplicateRate(cases), 3),
        Math.Round(EvidenceValidityRate(cases), 3),
        Math.Round(GapCitationRate(cases), 3),
        PrivacyHits(cases, markers),
        Math.Round(RepairRate(cases), 3));

    public static List<string> Failures(EvalMetrics m)
    {
        var failures = new List<string>();

        if (m.DuplicateQuestionRate > MaxDuplicateRate)
            failures.Add($"duplicateQuestionRate={m.DuplicateQuestionRate} > {MaxDuplicateRate}");
        if (m.EvidenceValidityRate < MinEvidenceValidityRate)
            failures.Add($"evidenceValidityRate={m.EvidenceValidityRate} < {MinEvidenceValidityRate}");
        if (m.GapCitationRate < MinGapCitationRate)
            failures.Add($"gapCitationRate={m.GapCitationRate} < {MinGapCitationRate}");
        if (m.PrivacyMarkerHits > MaxPrivacyMarkerHits)
            failures.Add($"privacyMarkerHits={m.PrivacyMarkerHits} > {MaxPrivacyMarkerHits}");
        if (m.JsonRepairRate > MaxJsonRepairRate)
            failures.Add($"jsonRepairRate={m.JsonRepairRate} > {MaxJsonRepairRate}");

        return failures;
    }

    /// <summary>同场题目两两比较，取各场重复率的平均；题量不足两道的场次不参与。</summary>
    public static double DuplicateRate(IReadOnlyList<CaseRun> cases)
    {
        var rates = new List<double>();

        foreach (var run in cases)
        {
            var texts = run.Questions.Select(q => q.Content).ToList();
            if (texts.Count < 2) continue;

            var pairs = 0;
            var duplicates = 0;
            for (var i = 0; i < texts.Count; i++)
            {
                for (var j = i + 1; j < texts.Count; j++)
                {
                    pairs++;
                    if (IsNearDuplicate(texts[i], texts[j])) duplicates++;
                }
            }

            rates.Add((double)duplicates / pairs);
        }

        return rates.Count == 0 ? 0 : rates.Average();
    }

    public static bool IsNearDuplicate(string a, string b)
    {
        var left = Normalize(a);
        var right = Normalize(b);
        if (left.Length == 0 || right.Length == 0 || left == right) return left == right;
        if (left.Length < 2 || right.Length < 2) return false;

        var l = Bigrams(left);
        var r = Bigrams(right);
        var overlap = l.Intersect(r, StringComparer.Ordinal).Count();
        var union = l.Count + r.Count - overlap;

        return union > 0 && (double)overlap / union >= DuplicateThreshold;
    }

    /// <summary>题目引用的锚点必须都在本场已知锚点里；没引锚点的题不算违规。</summary>
    public static double EvidenceValidityRate(IReadOnlyList<CaseRun> cases)
    {
        var total = cases.Sum(c => c.Questions.Count);
        if (total == 0) return 1;

        var valid = cases.Sum(c => c.Questions.Count(q => q.AnchorIds.All(c.KnownAnchors.Contains)));
        return (double)valid / total;
    }

    /// <summary>缺口题至少要引用一个「原文命中」的 JD 锚点；没有缺口题时视为达标。</summary>
    public static double GapCitationRate(IReadOnlyList<CaseRun> cases)
    {
        var gaps = cases
            .SelectMany(c => c.Questions.Where(q => q.Source == "gap").Select(q => (Case: c, Question: q)))
            .ToList();

        if (gaps.Count == 0) return 1;

        var cited = gaps.Count(g => g.Question.AnchorIds.Any(id =>
            g.Case.JdAnchorQuoteExact.TryGetValue(id, out var exact) && exact));

        return (double)cited / gaps.Count;
    }

    /// <summary>模型输出里出现埋进去的隐私标记就算命中；同一个标记出现多次按多次计。</summary>
    public static int PrivacyHits(IReadOnlyList<CaseRun> cases, IReadOnlyList<string> markers)
    {
        var effective = markers.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        if (effective.Count == 0) return 0;

        return cases
            .SelectMany(c => c.ModelOutputs)
            .Sum(output => effective.Sum(marker => CountOccurrences(output, marker)));
    }

    private static int CountOccurrences(string text, string marker)
    {
        var count = 0;
        for (var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
             index >= 0;
             index = text.IndexOf(marker, index + marker.Length, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }

    /// <summary>需要 repair 的调用占比（repair 次数 / 模型调用次数）。</summary>
    public static double RepairRate(IReadOnlyList<CaseRun> cases)
    {
        var calls = cases.Sum(c => c.Calls);
        if (calls == 0) return 0;

        return (double)cases.Sum(c => c.Repairs) / calls;
    }

    private static string Normalize(string text) =>
        new(text.Where(ch => !char.IsWhiteSpace(ch) && !char.IsPunctuation(ch)).ToArray());

    private static HashSet<string> Bigrams(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < text.Length - 1; i++) set.Add(text.Substring(i, 2));
        return set;
    }
}
