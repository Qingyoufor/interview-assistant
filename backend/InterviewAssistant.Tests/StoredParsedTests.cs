using InterviewAssistant.Api.Services;

namespace InterviewAssistant.Tests;

public class StoredParsedTests
{
    [Fact]
    public void 空json抛可读异常()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StoredParsed.ReadResume(null));
        Assert.Contains("还没有解析结果", ex.Message);
    }

    [Fact]
    public void 空对象视为缺少profile()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StoredParsed.ReadResume("{}"));
        Assert.Contains("缺少 profile", ex.Message);
    }

    [Fact]
    public void 空对象视为缺少job()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StoredParsed.ReadJob("{}"));
        Assert.Contains("缺少 job", ex.Message);
    }

    [Fact]
    public void 非法json抛JsonException()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => StoredParsed.ReadJob("{not json"));
    }

    [Fact]
    public void 正常json能读出profile与锚点()
    {
        const string json = """
        {
          "profile": { "summary": "3年后端", "skills": ["Java"], "experiences": [], "projects": [], "strengths": [], "gaps": [] },
          "anchors": [ { "id": "r:skill:0", "kind": "skill", "label": "Java", "quote": "Java", "quoteExact": true } ]
        }
        """;

        var (profile, anchors) = StoredParsed.ReadResume(json);

        Assert.Equal("3年后端", profile.Summary);
        Assert.Single(anchors);
        Assert.Equal("r:skill:0", anchors[0].Id);
    }
}
