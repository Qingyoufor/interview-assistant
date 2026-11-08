using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Interview;

namespace InterviewAssistant.Api.Services.Stats;

/// <summary>
/// 弱点标签的跨场次聚合（spec §8 / §17 S7）。
/// 纯计算：不调模型、不碰数据库，方便单测。只做 GROUP BY tag；
/// 词表外标签一律丢弃，保证统计口径与评分时一致。
/// </summary>
public static class WeaknessStatsBuilder
{
    public static WeaknessStats Build(IReadOnlyList<StatsQuestion> questions)
    {
        // 每道题内先按词表过滤并去重：同一题重复提到同一个标签只算一次
        var perQuestion = questions
            .Select(q => (q.SessionId, Tags: WeaknessCatalog.Filter(q.Weaknesses ?? [])))
            .ToList();

        var weaknesses = WeaknessCatalog.All
            .Select(tag => new WeaknessStat(
                tag,
                perQuestion.Count(x => x.Tags.Contains(tag)),
                perQuestion.Where(x => x.Tags.Contains(tag)).Select(x => x.SessionId).Distinct().Count()))
            .OrderByDescending(w => w.Count)
            .ThenBy(w => Array.IndexOf(WeaknessCatalog.All, w.Tag))
            .ToList();

        // 与报告同一口径：缺分按 0 计，分母是参与统计的题数，缺分不能把均分抬高
        var dimensions = Enum.GetNames<ScoreDimension>()
            .Select(dimension =>
            {
                var values = questions.Select(q => q.Scores.GetValueOrDefault(dimension, 0)).ToList();

                return new DimensionStat(
                    dimension,
                    values.Count == 0 ? 0 : Math.Round(values.Average(), 2),
                    values.Count);
            })
            .ToList();

        return new WeaknessStats(
            perQuestion.Select(x => x.SessionId).Distinct().Count(),
            questions.Count,
            weaknesses,
            dimensions);
    }
}
