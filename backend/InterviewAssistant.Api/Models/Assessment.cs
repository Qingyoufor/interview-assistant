namespace InterviewAssistant.Api.Models;

/// <summary>一次作答的评估结果（spec §7）。原子分供策略表使用，展示分给用户看。</summary>
public sealed class Assessment
{
    public AtomScores Atoms { get; set; } = new();
    public DisplayScores Display { get; set; } = new();
    public List<ClaimCheck> ClaimChecks { get; set; } = [];
    public List<string> Weaknesses { get; set; } = [];
    public List<string> Strengths { get; set; } = [];
    public List<string> Improvements { get; set; } = [];
    public string? Remediation { get; set; }
    public bool HasFactualError { get; set; }

    public void Normalize()
    {
        Atoms ??= new AtomScores();
        Display ??= new DisplayScores();
        Weaknesses ??= [];
        Strengths ??= [];
        Improvements ??= [];

        // 模型可能返回 [null] 或 claim=null，过滤掉再谈别的
        ClaimChecks = (ClaimChecks ?? []).Where(c => c is not null).ToList();
        foreach (var c in ClaimChecks)
        {
            c.Claim ??= "";
            c.EvidenceAnchorIds ??= [];
        }
    }
}

/// <summary>原子分：策略表只看这几项。</summary>
public sealed class AtomScores
{
    public int Correctness { get; set; } = 0;
    public int Depth { get; set; } = 0;
    public int Specificity { get; set; } = 0;
    public int Ownership { get; set; } = 0;
    public int Metrics { get; set; } = 0;
    public int Tradeoffs { get; set; } = 0;
}

/// <summary>展示分：给用户看的四维。</summary>
public sealed class DisplayScores
{
    public int Structure { get; set; } = 0;
    public int Depth { get; set; } = 0;
    public int Evidence { get; set; } = 0;
    public int Delivery { get; set; } = 0;
}

/// <summary>声明核验：回答里关于简历的主张，是否被材料支撑。</summary>
public sealed class ClaimCheck
{
    public string Claim { get; set; } = "";
    public string Verdict { get; set; } = ClaimVerdicts.Unverified;
    public List<string> EvidenceAnchorIds { get; set; } = [];
}

public static class ClaimVerdicts
{
    public const string Supported = "supported";
    public const string Unverified = "unverified";
    public const string Contradicted = "contradicted";
    public const string NotInMaterial = "not_in_material";

    public static readonly string[] All = [Supported, Unverified, Contradicted, NotInMaterial];
}
