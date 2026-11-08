using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Match;

namespace InterviewAssistant.Api.Services.Report;

/// <summary>报告构建的输入：一道题的作答结果（未作答的题 Assessment 为 null）。</summary>
public sealed record QuestionReport(
    string Source,
    Assessment? Assessment,
    IReadOnlyDictionary<string, int> Scores,
    IReadOnlyList<string> AnchorIds,
    string Content);

public sealed record DimensionAverage(string Dimension, double Average, int Count);

public sealed record WeaknessCount(string Tag, int Count);

public sealed record GapEvidence(string Requirement, string Remediation, string Question);

public sealed record SessionReport(
    Guid SessionId,
    int OverallScore,
    int MatchOverall,
    MatchBreakdown Breakdown,
    bool HardBlock,
    IReadOnlyList<string> TopRisks,
    IReadOnlyList<DimensionAverage> Dimensions,
    int TotalQuestions,
    int AnsweredQuestions,
    IReadOnlyList<WeaknessCount> Weaknesses,
    IReadOnlyList<GapEvidence> TopGaps,
    IReadOnlyList<string> Strengths,
    string Summary);
