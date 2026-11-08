using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Match;

namespace InterviewAssistant.Api.Services.Questions;

/// <summary>
/// 出题结果校验（spec §4 / §15.2）：
/// - 数量与配比符合计划；
/// - resume 题必须引用简历锚点，gap 题必须引用 JD 锚点；
/// - gap 题引用的 JD 锚点必须是 match 里判为 missing 且 QuoteExact=true 的那条，否则 gap 会引用不存在的原文。
/// 纯函数，方便单测；对模型返回的 null 元素与 null 字段做兜底，不抛异常。
/// </summary>
public static class QuestionValidator
{
    private static readonly string[] Types = ["basic", "project", "scenario", "behavior"];
    private static readonly string[] Sources = ["resume", "gap", "general"];

    public static List<string> Validate(
        IReadOnlyList<GeneratedQuestion> questions,
        QuestionPlan plan,
        IReadOnlyList<Anchor> resumeAnchors,
        IReadOnlyList<Anchor> jdAnchors,
        MatchResult match)
    {
        var problems = new List<string>();
        match.Normalize();

        foreach (var q in questions)
            if (q is not null) q.Normalize();

        if (questions.Count != plan.Total)
            problems.Add($"题目数量应为 {plan.Total}，当前 {questions.Count}");

        var resumeKinds = resumeAnchors.ToDictionary(a => a.Id, a => a, StringComparer.Ordinal);
        var jdKinds = jdAnchors.ToDictionary(a => a.Id, a => a, StringComparer.Ordinal);

        var missing = match.Items
            .Where(i => i.Verdict == MatchVerdict.Missing)
            .Select(i => i.JdAnchorId)
            .ToHashSet(StringComparer.Ordinal);

        var counts = questions
            .Where(q => q is not null)
            .GroupBy(q => q.Source)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var actualResume = counts.GetValueOrDefault("resume");
        var actualGap = counts.GetValueOrDefault("gap");
        var actualGeneral = counts.GetValueOrDefault("general");

        if (actualResume != plan.Resume) problems.Add($"简历题应为 {plan.Resume} 道，当前 {actualResume}");
        if (actualGap != plan.Gap) problems.Add($"缺口题应为 {plan.Gap} 道，当前 {actualGap}");
        if (actualGeneral != plan.General) problems.Add($"通用题应为 {plan.General} 道，当前 {actualGeneral}");

        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var no = i + 1;

            if (q is null)
            {
                problems.Add($"第 {no} 题为空");
                continue;
            }

            if (!Types.Contains(q.Type)) problems.Add($"第 {no} 题 type 非法：{q.Type}");
            if (!Sources.Contains(q.Source)) problems.Add($"第 {no} 题 source 非法：{q.Source}");
            if (q.Content.Trim().Length < 8) problems.Add($"第 {no} 题内容过短");

            foreach (var id in q.AnchorIds.Where(id => !resumeKinds.ContainsKey(id) && !jdKinds.ContainsKey(id)))
                problems.Add($"第 {no} 题引用了不存在的锚点：{id}");

            var resumeRefs = q.AnchorIds.Where(resumeKinds.ContainsKey).ToList();
            var jdRefs = q.AnchorIds.Where(jdKinds.ContainsKey).ToList();

            switch (q.Source)
            {
                case "resume" when resumeRefs.Count == 0:
                    problems.Add($"第 {no} 题是简历题，但没有引用任何简历锚点");
                    break;

                case "gap":
                    if (jdRefs.Count == 0) problems.Add($"第 {no} 题是缺口题，但没有引用任何 JD 锚点");
                    foreach (var id in jdRefs.Where(id => !missing.Contains(id)))
                        problems.Add($"第 {no} 题的 JD 锚点 {id} 在匹配结果里不是 missing");
                    foreach (var id in jdRefs.Where(id => jdKinds.TryGetValue(id, out var a) && !a.QuoteExact))
                        problems.Add($"第 {no} 题的 JD 锚点 {id} 未命中原文，不能作为缺口题依据");
                    break;
            }
        }

        return problems;
    }
}
