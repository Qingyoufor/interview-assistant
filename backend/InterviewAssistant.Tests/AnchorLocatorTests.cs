using InterviewAssistant.Api.Services;

namespace InterviewAssistant.Tests;

public class AnchorLocatorTests
{
    private const string Raw = """
        工作经历
        2023.07 - 至今  某某科技  后端开发工程师
        - 负责订单中心重构，把单体拆成 3 个服务

        项目经历
        订单中心重构  2024.03 - 2024.09
        - 技术栈：Java, Spring Cloud
        - 拆分订单读写，用 RabbitMQ 做异步削峰
        """;

    [Fact]
    public void Locate_命中行首标题行_而不是正文里的同名提及()
    {
        var anchor = AnchorLocator.Locate("r:proj:0", "project", "订单中心重构", "负责订单中心重构，把单体拆成 3 个服务", Raw);

        Assert.True(anchor.QuoteExact);
        Assert.Equal("r:proj:0", anchor.Id);
        Assert.Equal("project", anchor.Kind);
        Assert.Equal("订单中心重构", anchor.Label);
        Assert.StartsWith("订单中心重构  2024.03", anchor.Quote);
        Assert.Contains("拆分订单读写", anchor.Quote);
    }

    [Fact]
    public void Locate_原文找不到时_退回摘要并标记非原文()
    {
        var anchor = AnchorLocator.Locate("r:proj:9", "project", "不存在的项目", "模型编出来的内容", Raw);

        Assert.False(anchor.QuoteExact);
        Assert.Equal("模型编出来的内容", anchor.Quote);
        Assert.Equal("不存在的项目", anchor.Label);
    }

    [Fact]
    public void Locate_段落最多取四行()
    {
        var body = string.Join('\n', Enumerable.Range(0, 10).Select(i => $"第{i}行内容"));
        var raw = $"标题\n{body}";
        var anchor = AnchorLocator.Locate("x:0", "test", "标题", "", raw);

        Assert.True(anchor.QuoteExact);
        Assert.Equal(4, anchor.Quote.Split('\n').Length);
    }

    [Fact]
    public void Locate_单行超长时不超过200字()
    {
        var raw = "标题\n" + new string('长', 500);
        var anchor = AnchorLocator.Locate("x:0", "test", "标题", "", raw);

        Assert.True(anchor.QuoteExact);
        Assert.Equal(AnchorLocator.MaxQuoteChars, anchor.Quote.Length);
        Assert.Equal(200, AnchorLocator.MaxQuoteChars);
    }

    [Fact]
    public void Locate_Java不命中JavaScript()
    {
        var raw = "技能\n- 技术栈：JavaScript, Vue";
        var anchor = AnchorLocator.Locate("r:skill:0", "skill", "Java", "Java", raw);

        Assert.False(anchor.QuoteExact);
    }

    [Fact]
    public void Locate_整词Java能命中()
    {
        var raw = "技能\nJava, MySQL";
        var anchor = AnchorLocator.Locate("r:skill:0", "skill", "Java", "Java", raw);

        Assert.True(anchor.QuoteExact);
        Assert.StartsWith("Java, MySQL", anchor.Quote);
    }

    [Fact]
    public void Locate_CRLF不残留回车()
    {
        const string raw = "标题\r\n要求一\r\n要求二";
        var anchor = AnchorLocator.Locate("j:req:0", "requirement", "要求一", "要求一", raw);

        Assert.True(anchor.QuoteExact);
        Assert.DoesNotContain('\r', anchor.Quote);
        Assert.Contains("要求二", anchor.Quote);
    }

    [Fact]
    public void Locate_空原文返回空引用并标记非原文()
    {
        var anchor = AnchorLocator.Locate("x:0", "test", "标签", "", "");

        Assert.False(anchor.QuoteExact);
        Assert.Equal("", anchor.Quote);
    }

    [Fact]
    public void FindLineStart_只有出现在行首才算命中()
    {
        Assert.Equal(-1, AnchorLocator.FindLineStart("前缀 ABC 后缀", "ABC"));
        Assert.Equal(0, AnchorLocator.FindLineStart("ABC 开头", "ABC"));
        Assert.Equal(5, AnchorLocator.FindLineStart("前缀文本\nABC 开头", "ABC"));
    }

    [Fact]
    public void Locate_中文技能名命中所在行()
    {
        var anchor = AnchorLocator.Locate("r:skill:0", "skill", "RabbitMQ", "RabbitMQ", Raw);

        Assert.True(anchor.QuoteExact);
        Assert.Contains("RabbitMQ", anchor.Quote);
        Assert.Contains("异步削峰", anchor.Quote);
    }
}
