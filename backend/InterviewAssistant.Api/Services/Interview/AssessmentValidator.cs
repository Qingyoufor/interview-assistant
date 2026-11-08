using InterviewAssistant.Api.Models;

namespace InterviewAssistant.Api.Services.Interview;

/// <summary>评估结果的硬校验（spec §7）。任何一条不过就 repair，不允许落库可疑评分。</summary>
public static class AssessmentValidator
{
    private static readonly string[] ResumeAnchorKinds = ["project", "experience", "skill"];

    public static List<string> Validate(Assessment assessment, IReadOnlyDictionary<string, string> anchorKinds)
    {
        var problems = new List<string>();
        assessment.Normalize();

        CheckAtom(problems, "correctness", assessment.Atoms.Correctness);
        CheckAtom(problems, "depth", assessment.Atoms.Depth);
        CheckAtom(problems, "specificity", assessment.Atoms.Specificity);
        CheckAtom(problems, "ownership", assessment.Atoms.Ownership);
        CheckAtom(problems, "metrics", assessment.Atoms.Metrics);
        CheckAtom(problems, "tradeoffs", assessment.Atoms.Tradeoffs);

        CheckDisplay(problems, "structure", assessment.Display.Structure);
        CheckDisplay(problems, "depth", assessment.Display.Depth);
        CheckDisplay(problems, "evidence", assessment.Display.Evidence);
        CheckDisplay(problems, "delivery", assessment.Display.Delivery);

        foreach (var check in assessment.ClaimChecks)
        {
            if (!ClaimVerdicts.All.Contains(check.Verdict))
                problems.Add($"claimChecks 的 verdict 非法：{check.Verdict}");

            // supported 与 contradicted 都必须有证据，否则模型凭空写一条就能触发追问
            var needsEvidence = check.Verdict is ClaimVerdicts.Supported or ClaimVerdicts.Contradicted;
            if (needsEvidence && check.EvidenceAnchorIds.Count == 0)
                problems.Add($"声明「{Truncate(check.Claim)}」判为 {check.Verdict}，但没有引用任何锚点");

            foreach (var id in check.EvidenceAnchorIds)
            {
                if (!anchorKinds.TryGetValue(id, out var kind))
                {
                    problems.Add($"声明「{Truncate(check.Claim)}」引用了不存在的锚点：{id}");
                    continue;
                }

                // 声明说的是简历事实，只能用简历侧锚点，不能拿 JD 要求当证据
                if (!ResumeAnchorKinds.Contains(kind))
                    problems.Add($"声明「{Truncate(check.Claim)}」只能用简历锚点作为证据，{id} 是 {kind}");
            }
        }

        // spec §8：白名单过滤，非法丢弃；只保留 1~4 个，避免污染 S7 的按标签聚合
        assessment.Weaknesses = WeaknessCatalog.Filter(assessment.Weaknesses).Take(4).ToList();
        if (assessment.Weaknesses.Count == 0)
            problems.Add("weaknesses 至少需要 1 个词表内的标签");

        return problems;
    }

    private static void CheckAtom(List<string> problems, string name, int value)
    {
        if (value is < 1 or > 5) problems.Add($"原子分 {name} 必须在 1~5，当前 {value}");
    }

    private static void CheckDisplay(List<string> problems, string name, int value)
    {
        if (value is < 1 or > 5) problems.Add($"展示分 {name} 必须在 1~5，当前 {value}");
    }

    private static string Truncate(string s) => s.Length <= 20 ? s : s[..20] + "…";
}
