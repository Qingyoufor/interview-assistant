namespace InterviewAssistant.Api.Services.Stats;

/// <summary>一道已评估题目的统计输入：标签来自 assessment_json，展示分来自 question_scores。</summary>
public sealed record StatsQuestion(
    Guid SessionId,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyDictionary<string, int> Scores);

/// <summary>单个弱点标签的聚合。Count = 命中题目数，Sessions = 出现该标签的场次数。</summary>
public sealed record WeaknessStat(string Tag, int Count, int Sessions);

/// <summary>某一维度的跨场次均分。Count = 参与均分的题目数。</summary>
public sealed record DimensionStat(string Dimension, double Average, int Count);

/// <summary>弱点统计结果（spec §8 / §17 S7）。25 个标签恒定返回，缺数据的补 0。</summary>
public sealed record WeaknessStats(
    int Sessions,
    int Questions,
    IReadOnlyList<WeaknessStat> Weaknesses,
    IReadOnlyList<DimensionStat> Dimensions);
