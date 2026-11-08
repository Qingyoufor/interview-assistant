using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Match;

namespace InterviewAssistant.Tests;

public class MatchValidatorTests
{
    private static readonly Dictionary<string, string> JdKinds = new(StringComparer.Ordinal)
    {
        ["j:req:0"] = "requirement",
        ["j:req:1"] = "requirement",
        ["j:resp:0"] = "responsibility"
    };

    private static readonly HashSet<string> ResumeIds = ["r:proj:0", "r:skill:0"];

    private static MatchResult Valid() => new()
    {
        OverallMatch = 72,
        Breakdown = new MatchBreakdown { MustHave = 32, Responsibilities = 22, EvidenceQuality = 14, Bonus = 4 },
        HardBlock = false,
        Items =
        [
            new MatchItem
            {
                Requirement = "熟悉 Kafka",
                Category = MatchCategory.MustHave,
                JdAnchorId = "j:req:0",
                ResumeAnchorIds = ["r:proj:0"],
                Evidence = "用 RabbitMQ 做异步削峰",
                Verdict = MatchVerdict.Partial
            },
            new MatchItem
            {
                Requirement = "本科及以上学历",
                Category = MatchCategory.MustHave,
                JdAnchorId = "j:req:1",
                ResumeAnchorIds = ["r:skill:0"],
                Evidence = "本科，软件工程",
                Verdict = MatchVerdict.Strong
            },
            new MatchItem
            {
                Requirement = "负责订单中心",
                Category = MatchCategory.Responsibility,
                JdAnchorId = "j:resp:0",
                ResumeAnchorIds = ["r:proj:0"],
                Evidence = "订单中心重构",
                Verdict = MatchVerdict.Strong
            }
        ],
        TopRisks = ["Kafka 无实战"]
    };

    [Fact]
    public void 合法结果没有问題()
    {
        Assert.Empty(MatchValidator.Validate(Valid(), JdKinds, ResumeIds));
    }

    [Fact]
    public void 四项分之和必须等于总分()
    {
        var r = Valid();
        r.OverallMatch = 90;

        Assert.Contains(MatchValidator.Validate(r, JdKinds, ResumeIds), p => p.Contains("四项分之和"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void 总分越界被拒(int score)
    {
        var r = Valid();
        r.OverallMatch = score;
        r.Breakdown = new MatchBreakdown { MustHave = score, Responsibilities = 0, EvidenceQuality = 0, Bonus = 0 };

        Assert.Contains(MatchValidator.Validate(r, JdKinds, ResumeIds), p => p.Contains("overallMatch 必须在 0~100"));
    }

    [Fact]
    public void 单项分数超上限被拒()
    {
        var r = Valid();
        r.Breakdown = new MatchBreakdown { MustHave = 60, Responsibilities = 25, EvidenceQuality = 20, Bonus = 10 };
        r.OverallMatch = 100;

        Assert.Contains(MatchValidator.Validate(r, JdKinds, ResumeIds), p => p.Contains("mustHave 必须在 0~45"));
    }

    [Fact]
    public void 锚点不存在被拒()
    {
        var r = Valid();
        r.Items[0].JdAnchorId = "j:req:9";

        Assert.Contains(MatchValidator.Validate(r, JdKinds, ResumeIds), p => p.Contains("jdAnchorId 不存在"));
    }

    [Fact]
    public void 简历锚点不存在被拒()
    {
        var r = Valid();
        r.Items[0].ResumeAnchorIds = ["r:proj:99"];

        Assert.Contains(MatchValidator.Validate(r, JdKinds, ResumeIds), p => p.Contains("resumeAnchorId 不存在"));
    }

    [Fact]
    public void verdict非法被拒()
    {
        var r = Valid();
        r.Items[0].Verdict = "great";

        Assert.Contains(MatchValidator.Validate(r, JdKinds, ResumeIds), p => p.Contains("verdict 非法"));
    }

    [Fact]
    public void 硬性要求被错分类时被拒_防止绕过hardBlock()
    {
        var r = Valid();
        r.Items[0].Category = MatchCategory.Responsibility;

        var problems = MatchValidator.Validate(r, JdKinds, ResumeIds);

        Assert.Contains(problems, p => p.Contains("category 必须是 mustHave"));
    }

    [Fact]
    public void 判为强匹配却没有证据被拒()
    {
        var r = Valid();
        r.Items[0].Verdict = MatchVerdict.Strong;
        r.Items[0].ResumeAnchorIds = [];

        Assert.Contains(MatchValidator.Validate(r, JdKinds, ResumeIds), p => p.Contains("没有任何简历锚点作为证据"));
    }

    [Fact]
    public void 职责类判为partial也必须给证据()
    {
        var r = Valid();
        r.Items[2].Verdict = MatchVerdict.Partial;
        r.Items[2].ResumeAnchorIds = [];

        Assert.Contains(MatchValidator.Validate(r, JdKinds, ResumeIds), p => p.Contains("没有任何简历锚点作为证据"));
    }

    [Fact]
    public void 硬性缺失必须标记hardBlock()
    {
        var r = Valid();
        r.Items[0].Verdict = MatchVerdict.Missing;
        r.Items[0].ResumeAnchorIds = [];

        Assert.Contains(MatchValidator.Validate(r, JdKinds, ResumeIds), p => p.Contains("hardBlock 应为 True"));
    }

    [Fact]
    public void 空items被拒()
    {
        var r = Valid();
        r.Items.Clear();

        Assert.Contains(MatchValidator.Validate(r, JdKinds, ResumeIds), p => p.Contains("items 为空"));
    }

    [Fact]
    public void 漏掉某条JD要求被拒()
    {
        var r = Valid();
        r.Items.RemoveAt(2);

        Assert.Contains(MatchValidator.Validate(r, JdKinds, ResumeIds), p => p.Contains("没有出现在 items 里"));
    }

    [Fact]
    public void breakdown为null不崩溃而是报校验问题()
    {
        var r = Valid();
        r.Breakdown = null!;

        var problems = MatchValidator.Validate(r, JdKinds, ResumeIds);

        Assert.NotEmpty(problems);
        Assert.Contains(problems, p => p.Contains("四项分之和"));
    }

    [Fact]
    public void items为null不崩溃而是报校验问题()
    {
        var r = Valid();
        r.Items = null!;

        Assert.Contains(MatchValidator.Validate(r, JdKinds, ResumeIds), p => p.Contains("items 为空"));
    }

    [Fact]
    public void resumeAnchorIds为null不崩溃()
    {
        var r = Valid();
        r.Items[0].ResumeAnchorIds = null!;

        Assert.DoesNotContain(MatchValidator.Validate(r, JdKinds, ResumeIds), p => p.Contains("resumeAnchorId 不存在"));
    }
}
