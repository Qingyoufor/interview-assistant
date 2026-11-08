using InterviewAssistant.Api.Controllers;

namespace InterviewAssistant.Tests;

public class SessionMapperTests
{
    [Fact]
    public void 正常评估能解析出弱点标签()
    {
        var assessment = SessionMapper.ParseAssessment("""{"weaknesses":["技术深度","Kafka"]}""");

        Assert.NotNull(assessment);
        Assert.Contains("技术深度", assessment!.Weaknesses);
    }

    [Fact]
    public void 空对象不会抛异常()
    {
        var assessment = SessionMapper.ParseAssessment("{}");

        Assert.NotNull(assessment);
        Assert.Empty(assessment!.Weaknesses);
    }

    [Theory]
    [InlineData("不是 JSON")]
    [InlineData("""{"weaknesses": 12}""")]
    [InlineData("")]
    [InlineData(null)]
    public void 脏数据或空值一律当作没有评估(string? json)
    {
        Assert.Null(SessionMapper.ParseAssessment(json));
    }
}
