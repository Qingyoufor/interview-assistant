using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services;
using InterviewAssistant.Api.Services.Match;
using InterviewAssistant.Api.Services.Questions;

namespace InterviewAssistant.Tests;

public class QuestionPlannerTests
{
    private static MatchResult MatchWith(int missing, int partial = 0, int strong = 0)
    {
        var items = new List<MatchItem>();
        for (var i = 0; i < missing; i++) items.Add(Item($"j:req:{i}", MatchVerdict.Missing));
        for (var i = 0; i < partial; i++) items.Add(Item($"j:resp:{i}", MatchVerdict.Partial));
        for (var i = 0; i < strong; i++) items.Add(Item($"j:req:s{i}", MatchVerdict.Strong));
        return new MatchResult { Items = items };
    }

    private static MatchItem Item(string anchor, string verdict) => new()
    {
        JdAnchorId = anchor,
        Category = anchor.StartsWith("j:resp") ? MatchCategory.Responsibility : MatchCategory.MustHave,
        Verdict = verdict,
        ResumeAnchorIds = verdict == MatchVerdict.Missing ? [] : ["r:proj:0"]
    };

    /// <summary>给 match 里的每个锚点造一条 JD 锚点，默认命中原文。</summary>
    private static List<Anchor> AnchorsFor(MatchResult match, bool quoteExact = true) =>
        match.Items
            .GroupBy(i => i.JdAnchorId, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(i => new Anchor(i.JdAnchorId,
                i.JdAnchorId.StartsWith("j:resp") ? "responsibility" : "requirement",
                i.JdAnchorId, "原文片段", quoteExact))
            .ToList();

    [Fact]
    public void 三个缺口时按三三二配比()
    {
        var match = MatchWith(missing: 3);

        Assert.Equal(new QuestionPlan(3, 3, 2, 8), QuestionPlanner.Plan(match, AnchorsFor(match)));
    }

    [Fact]
    public void 只有missing计入缺口_parial与strong不计()
    {
        var match = MatchWith(missing: 3, partial: 2, strong: 4);
        var plan = QuestionPlanner.Plan(match, AnchorsFor(match));

        Assert.Equal(3, plan.Gap);
        Assert.Equal(3, plan.Resume);
    }

    [Fact]
    public void 未命中原文的缺口不排缺口题()
    {
        var match = MatchWith(missing: 1);

        var plan = QuestionPlanner.Plan(match, AnchorsFor(match, quoteExact: false));

        Assert.Equal(0, plan.Gap);
    }

    [Fact]
    public void 重复缺口去重后只算一个()
    {
        var match = new MatchResult
        {
            Items =
            [
                Item("j:req:0", MatchVerdict.Missing),
                Item("j:req:0", MatchVerdict.Missing)
            ]
        };

        Assert.Single(QuestionPlanner.ExactMissing(match, AnchorsFor(match)));
    }

    [Fact]
    public void 缺口不足时用简历题补足()
    {
        var match = MatchWith(missing: 1);
        var plan = QuestionPlanner.Plan(match, AnchorsFor(match));

        Assert.Equal(5, plan.Resume);
        Assert.Equal(1, plan.Gap);
        Assert.Equal(2, plan.General);
        Assert.Equal(8, plan.Total);
    }

    [Fact]
    public void 没有缺口时全是简历题与通用题()
    {
        var match = MatchWith(missing: 0);
        var plan = QuestionPlanner.Plan(match, AnchorsFor(match));

        Assert.Equal(6, plan.Resume);
        Assert.Equal(0, plan.Gap);
        Assert.Equal(2, plan.General);
    }

    [Fact]
    public void 缺口很多时缺口题封顶三道()
    {
        var match = MatchWith(missing: 10);

        Assert.Equal(3, QuestionPlanner.Plan(match, AnchorsFor(match)).Gap);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void 题量很小时配比不为负(int total)
    {
        var match = MatchWith(missing: 5);
        var plan = QuestionPlanner.Plan(match, AnchorsFor(match), total);

        Assert.True(plan.Resume >= 0);
        Assert.True(plan.Gap >= 0);
        Assert.True(plan.General >= 0);
        Assert.Equal(total, plan.Resume + plan.Gap + plan.General);
    }
}

public class InputFingerprintTests
{
    [Fact]
    public void 输入相同指纹相同()
    {
        Assert.Equal(InputFingerprint.Of("a", "b"), InputFingerprint.Of("a", "b"));
    }

    [Fact]
    public void 任一输入变化指纹就变()
    {
        var baseline = InputFingerprint.Of("a", "b");

        Assert.NotEqual(baseline, InputFingerprint.Of("a2", "b"));
        Assert.NotEqual(baseline, InputFingerprint.Of("a", "b2"));
    }
}
