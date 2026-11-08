namespace InterviewAssistant.Api.Services.Match;

/// <summary>加权匹配结果（spec §5）。</summary>
public sealed class MatchResult
{
    public int OverallMatch { get; set; }
    public MatchBreakdown Breakdown { get; set; } = new();
    public bool HardBlock { get; set; }
    public List<MatchItem> Items { get; set; } = [];
    public List<string> TopRisks { get; set; } = [];

    /// <summary>模型可能显式返回 null，反序列化后先归一化，避免校验器空引用。</summary>
    public void Normalize()
    {
        Breakdown ??= new MatchBreakdown();
        Items ??= [];
        TopRisks ??= [];
        foreach (var item in Items) item.ResumeAnchorIds ??= [];
    }
}

/// <summary>四项分：必须分别落在各自上限内，且四项之和等于 OverallMatch。</summary>
public sealed class MatchBreakdown
{
    public const int MustHaveMax = 45;
    public const int ResponsibilitiesMax = 25;
    public const int EvidenceQualityMax = 20;
    public const int BonusMax = 10;

    public int MustHave { get; set; }
    public int Responsibilities { get; set; }
    public int EvidenceQuality { get; set; }
    public int Bonus { get; set; }

    public int Sum => MustHave + Responsibilities + EvidenceQuality + Bonus;
}

public sealed class MatchItem
{
    public string Requirement { get; set; } = "";
    public string Category { get; set; } = MatchCategory.MustHave;
    public string JdAnchorId { get; set; } = "";
    public List<string> ResumeAnchorIds { get; set; } = [];
    public string Evidence { get; set; } = "";
    public string Verdict { get; set; } = MatchVerdict.Missing;
    public string? Risk { get; set; }
}

public static class MatchCategory
{
    public const string MustHave = "mustHave";
    public const string Responsibility = "responsibility";
    public const string Bonus = "bonus";
    public static readonly string[] All = [MustHave, Responsibility, Bonus];
}

public static class MatchVerdict
{
    public const string Strong = "strong";
    public const string Partial = "partial";
    public const string Missing = "missing";
    public static readonly string[] All = [Strong, Partial, Missing];
}
