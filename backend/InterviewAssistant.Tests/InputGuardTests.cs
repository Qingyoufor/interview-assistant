using InterviewAssistant.Api.Services;

namespace InterviewAssistant.Tests;

public class InputGuardTests
{
    [Fact]
    public void Prepare_不足下限被拒绝()
    {
        var result = InputGuard.Prepare(new string('字', 49), 50, 12000, "简历内容");

        Assert.False(result.Ok);
        Assert.Contains("简历内容太短", result.Error);
    }

    [Fact]
    public void Prepare_恰好等于下限放行()
    {
        var result = InputGuard.Prepare(new string('字', 50), 50, 12000);

        Assert.True(result.Ok);
        Assert.False(result.Truncated);
        Assert.Equal(50, result.Text!.Length);
    }

    [Fact]
    public void Prepare_null视为空()
    {
        var result = InputGuard.Prepare(null, 50, 12000);

        Assert.False(result.Ok);
    }

    [Fact]
    public void Prepare_恰好等于上限不截断()
    {
        var result = InputGuard.Prepare(new string('字', 12000), 50, 12000);

        Assert.True(result.Ok);
        Assert.False(result.Truncated);
        Assert.Equal(12000, result.Text!.Length);
    }

    [Fact]
    public void Prepare_超过上限截断并标记()
    {
        var result = InputGuard.Prepare(new string('字', 12001), 50, 12000);

        Assert.True(result.Ok);
        Assert.True(result.Truncated);
        Assert.Equal(12000, result.Text!.Length);
    }

    [Fact]
    public void Prepare_截断不切断emoji()
    {
        var text = new string('a', 11999) + "😀" + new string('b', 10);

        var result = InputGuard.Prepare(text, 50, 12000);

        Assert.True(result.Ok);
        Assert.True(result.Truncated);
        Assert.Equal(11999, result.Text!.Length);
        Assert.False(char.IsHighSurrogate(result.Text[^1]));
    }

    [Fact]
    public void Clamp_超长文本按列宽截断()
    {
        Assert.Equal(120, InputGuard.Clamp(new string('标', 200), 120).Length);
        Assert.Equal("短标题", InputGuard.Clamp("  短标题  ", 120));
        Assert.Equal("", InputGuard.Clamp(null, 10));
    }
}
