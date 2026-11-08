using InterviewAssistant.Api.Services.Llm;

namespace InterviewAssistant.Tests;

public class RedactorTests
{
    [Fact]
    public void 手机号被替换()
    {
        Assert.Equal("联系【手机号】", Redactor.Redact("联系13912345678"));
    }

    [Theory]
    [InlineData("138 0013 8000")]
    [InlineData("138-0013-8000")]
    [InlineData("+8613800138000")]
    [InlineData("+86 13800138000")]
    public void 带分隔符或国家码的手机号也被替换(string phone)
    {
        Assert.Equal("电话【手机号】", Redactor.Redact($"电话{phone}"));
    }

    [Fact]
    public void 邮箱被替换()
    {
        Assert.Equal("邮箱【邮箱】", Redactor.Redact("邮箱li.si@example.co.uk"));
    }

    [Fact]
    public void 身份证末尾为X也会被替换()
    {
        Assert.Equal("证件【身份证】", Redactor.Redact("证件11010119900307123X"));
    }

    [Fact]
    public void 恰好十八位但不是生日的流水号不当作身份证()
    {
        Assert.Equal("流水号202401011234567890", Redactor.Redact("流水号202401011234567890"));
    }

    [Fact]
    public void 标注的姓名被替换()
    {
        Assert.Equal("姓名：【姓名】", Redactor.Redact("姓名：张三丰"));
    }

    [Fact]
    public void 行首的名字字段被替换()
    {
        Assert.Equal("名字：【姓名】", Redactor.Redact("名字：张三"));
    }

    [Fact]
    public void 项目名和公司名里的Name不当作姓名()
    {
        const string text = "Project Name: 秒杀系统\nCompany Name: 字节跳动";

        Assert.Equal(text, Redactor.Redact(text));
    }

    [Fact]
    public void 我叫后面的姓名被替换()
    {
        Assert.Equal("我叫【姓名】，来自杭州", Redactor.Redact("我叫李四，来自杭州"));
    }

    [Fact]
    public void 我叫后面不是姓名时不误伤()
    {
        Assert.Equal("我叫这种设计思路的思路", Redactor.Redact("我叫这种设计思路的思路"));
    }

    [Theory]
    [InlineData("张三，男，25 岁", "【姓名】，男，25 岁")]
    [InlineData("欧阳娜娜 | 女 | 5 年经验", "【姓名】 | 女 | 5 年经验")]
    public void 简历首行的姓名被替换(string input, string expected)
    {
        Assert.Equal(expected, Redactor.Redact(input));
    }

    [Fact]
    public void 多处隐私一次处理干净()
    {
        var result = Redactor.Redact("姓名：张三，13800138000，a@b.com，110101199003071234");

        Assert.DoesNotContain("张三", result);
        Assert.DoesNotContain("13800138000", result);
        Assert.DoesNotContain("a@b.com", result);
        Assert.DoesNotContain("110101199003071234", result);
    }

    [Fact]
    public void 不含隐私的文本原样返回()
    {
        const string text = "2024 年 11 月上线，QPS 从 1000 提到 12000，数据库 MySQL 8.0，接口 P99 120ms";

        Assert.Equal(text, Redactor.Redact(text));
    }

    [Fact]
    public void 空文本与超长数字串不误伤()
    {
        Assert.Equal("", Redactor.Redact(""));
        Assert.Equal("订单2024010112345678901", Redactor.Redact("订单2024010112345678901"));
    }
}
