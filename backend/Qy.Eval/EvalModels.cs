namespace Qy.Eval;

/// <summary>一道题在评测里的最小信息。</summary>
public sealed record EvalQuestion(string Source, IReadOnlyList<string> AnchorIds, string Content);

/// <summary>跑完一个 case 的原始数据。指标全部从这些数据算出来，所以能脱离模型单测。</summary>
public sealed record CaseRun(
    string Id,
    IReadOnlyList<EvalQuestion> Questions,
    IReadOnlySet<string> KnownAnchors,
    IReadOnlyDictionary<string, bool> JdAnchorQuoteExact,
    IReadOnlyList<string> ModelOutputs,
    int Calls,
    int Repairs);

/// <summary>spec §12 的五个指标。</summary>
public sealed record EvalMetrics(
    double DuplicateQuestionRate,
    double EvidenceValidityRate,
    double GapCitationRate,
    int PrivacyMarkerHits,
    double JsonRepairRate);

/// <summary>一个合成 case：简历 + JD + 预设回答 + 埋进去的隐私标记。</summary>
public sealed record EvalCase(
    string Id,
    string Level,
    string Kind,
    string Resume,
    string Jd,
    IReadOnlyList<string> Answers,
    IReadOnlyList<string> Markers);
