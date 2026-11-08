using InterviewAssistant.Api.Services.Stats;

namespace InterviewAssistant.Tests;

public class WeaknessStatsBuilderTests
{
    private static readonly Guid S1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid S2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static StatsQuestion Q(Guid session, string[] weaknesses, int structure = 3, int depth = 3, int evidence = 3, int delivery = 3) =>
        new(session, weaknesses, new Dictionary<string, int>
        {
            ["structure"] = structure,
            ["depth"] = depth,
            ["evidence"] = evidence,
            ["delivery"] = delivery
        });

    private static WeaknessStat Stat(WeaknessStats stats, string tag) => stats.Weaknesses.Single(w => w.Tag == tag);

    [Fact]
    public void 没有作答数据时仍返回完整词表且全部为零()
    {
        var stats = WeaknessStatsBuilder.Build([]);

        Assert.Equal(0, stats.Sessions);
        Assert.Equal(0, stats.Questions);
        Assert.Equal(25, stats.Weaknesses.Count);
        Assert.All(stats.Weaknesses, w => Assert.Equal(0, w.Count));
        Assert.All(stats.Dimensions, d => Assert.Equal(0, d.Count));
    }

    [Fact]
    public void 同一道题重复提到同一个标签只算一次()
    {
        var stats = WeaknessStatsBuilder.Build([Q(S1, ["技术深度", "技术深度"])]);

        Assert.Equal(1, Stat(stats, "技术深度").Count);
        Assert.Equal(1, Stat(stats, "技术深度").Sessions);
    }

    [Fact]
    public void 跨场次累计命中数与场次数()
    {
        var stats = WeaknessStatsBuilder.Build([
            Q(S1, ["技术深度"]),
            Q(S1, ["技术深度"]),
            Q(S2, ["技术深度"])
        ]);

        Assert.Equal(3, Stat(stats, "技术深度").Count);
        Assert.Equal(2, Stat(stats, "技术深度").Sessions);
        Assert.Equal(2, stats.Sessions);
        Assert.Equal(3, stats.Questions);
    }

    [Fact]
    public void 词表外的标签不计入统计()
    {
        var stats = WeaknessStatsBuilder.Build([Q(S1, ["Kafka", "技术深度"])]);

        Assert.DoesNotContain(stats.Weaknesses, w => w.Tag == "Kafka");
        Assert.Equal(1, Stat(stats, "技术深度").Count);
        Assert.Equal(1, stats.Weaknesses.Sum(w => w.Count));
    }

    [Fact]
    public void 按命中数降序同分按词表顺序()
    {
        var stats = WeaknessStatsBuilder.Build([
            Q(S1, ["并发"]),
            Q(S1, ["并发"]),
            Q(S1, ["技术深度"]),
            Q(S1, ["自我介绍"])
        ]);

        Assert.Equal("并发", stats.Weaknesses[0].Tag);
        Assert.Equal(2, stats.Weaknesses[0].Count);
        // 同为 1 次时：自我介绍在词表里排第 1，技术深度排第 4
        Assert.Equal("自我介绍", stats.Weaknesses[1].Tag);
        Assert.Equal("技术深度", stats.Weaknesses[2].Tag);
    }

    [Fact]
    public void 维度均分与参与题目数正确()
    {
        var stats = WeaknessStatsBuilder.Build([
            Q(S1, [], structure: 4, depth: 2, evidence: 5, delivery: 3),
            Q(S2, [], structure: 2, depth: 4, evidence: 1, delivery: 3)
        ]);

        Assert.Equal(3, stats.Dimensions.Single(d => d.Dimension == "structure").Average);
        Assert.Equal(3, stats.Dimensions.Single(d => d.Dimension == "depth").Average);
        Assert.All(stats.Dimensions, d => Assert.Equal(2, d.Count));
    }

    [Fact]
    public void 均分保留两位小数()
    {
        var stats = WeaknessStatsBuilder.Build([
            Q(S1, [], structure: 3),
            Q(S2, [], structure: 4),
            Q(S1, [], structure: 4)
        ]);

        Assert.Equal(3.67, stats.Dimensions.Single(d => d.Dimension == "structure").Average);
    }

    [Fact]
    public void 缺分数的题目按零分计入该维均分()
    {
        StatsQuestion full = new(S1, [], new Dictionary<string, int>
        {
            ["structure"] = 4,
            ["depth"] = 4,
            ["evidence"] = 4,
            ["delivery"] = 4
        });
        StatsQuestion partial = new(S1, [], new Dictionary<string, int>
        {
            ["structure"] = 2,
            ["depth"] = 2,
            ["evidence"] = 2
        });

        var stats = WeaknessStatsBuilder.Build([full, partial]);

        // 与报告同一口径：缺分当 0，分母仍是两题
        var delivery = stats.Dimensions.Single(d => d.Dimension == "delivery");
        Assert.Equal(2, delivery.Average);
        Assert.Equal(2, delivery.Count);
        Assert.Equal(3, stats.Dimensions.Single(d => d.Dimension == "structure").Average);
    }

    [Fact]
    public void 没有任何分数时维度均分为零但题数照算()
    {
        var stats = WeaknessStatsBuilder.Build([new StatsQuestion(S1, ["测试"], new Dictionary<string, int>())]);

        Assert.All(stats.Dimensions, d =>
        {
            Assert.Equal(0, d.Average);
            Assert.Equal(1, d.Count);
        });
        Assert.Equal(1, stats.Questions);
    }
}
