using InterviewAssistant.Api.Services;

namespace InterviewAssistant.Tests;

public class JdParserTests
{
    private const string Raw = """
        岗位职责：
        负责订单中心核心链路的设计与开发
        负责性能优化

        任职要求：
        熟悉 Kafka 等消息中间件
        本科及以上学历
        """;

    [Fact]
    public void BuildAnchors_先要求后职责_编号从零开始()
    {
        var profile = new JobProfile
        {
            MustHave = ["熟悉 Kafka 等消息中间件", "本科及以上学历"],
            Responsibilities = ["负责订单中心核心链路的设计与开发", "负责性能优化"]
        };

        var anchors = JdParser.BuildAnchors(profile, Raw);

        Assert.Equal(4, anchors.Count);
        Assert.Equal(["j:req:0", "j:req:1", "j:resp:0", "j:resp:1"], anchors.Select(a => a.Id));
        Assert.Equal(["requirement", "requirement", "responsibility", "responsibility"], anchors.Select(a => a.Kind));
        Assert.Equal(["熟悉 Kafka 等消息中间件", "本科及以上学历", "负责订单中心核心链路的设计与开发", "负责性能优化"], anchors.Select(a => a.Label));
        Assert.All(anchors, a => Assert.True(a.QuoteExact));
    }

    [Fact]
    public void BuildAnchors_空数组返回空集合()
    {
        var anchors = JdParser.BuildAnchors(new JobProfile(), Raw);

        Assert.Empty(anchors);
    }

    [Fact]
    public void BuildAnchors_原文找不到时标记非原文()
    {
        var profile = new JobProfile { MustHave = ["熟悉 Rust 异步运行时"] };

        var anchors = JdParser.BuildAnchors(profile, Raw);

        Assert.Single(anchors);
        Assert.False(anchors[0].QuoteExact);
        Assert.Equal("熟悉 Rust 异步运行时", anchors[0].Quote);
    }
}
