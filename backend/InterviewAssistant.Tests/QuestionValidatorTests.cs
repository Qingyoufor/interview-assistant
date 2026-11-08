using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Match;
using InterviewAssistant.Api.Services.Questions;

namespace InterviewAssistant.Tests;

public class QuestionValidatorTests
{
    private static readonly List<Anchor> ResumeAnchors =
    [
        new("r:proj:0", "project", "订单中心重构", "负责订单中心重构", true),
        new("r:skill:0", "skill", "Kafka", "Kafka", true)
    ];

    private static readonly List<Anchor> JdAnchors =
    [
        new("j:req:0", "requirement", "5 年经验", "5 年以上后端开发经验", true),
        new("j:req:1", "requirement", "Kafka", "熟悉 Kafka", true),
        new("j:req:2", "requirement", "英文", "英语流利", false),
        new("j:resp:0", "responsibility", "架构演进", "参与系统架构演进", true)
    ];

    private static MatchResult Match() => new()
    {
        Items =
        [
            new MatchItem { JdAnchorId = "j:req:0", Category = MatchCategory.MustHave, Verdict = MatchVerdict.Missing },
            new MatchItem { JdAnchorId = "j:req:1", Category = MatchCategory.MustHave, Verdict = MatchVerdict.Missing },
            new MatchItem { JdAnchorId = "j:req:2", Category = MatchCategory.MustHave, Verdict = MatchVerdict.Missing },
            new MatchItem { JdAnchorId = "j:resp:0", Category = MatchCategory.Responsibility, Verdict = MatchVerdict.Strong, ResumeAnchorIds = ["r:proj:0"] }
        ]
    };

    private static readonly QuestionPlan Plan = new(Resume: 3, Gap: 3, General: 2, Total: 8);

    private static List<GeneratedQuestion> Valid() =>
    [
        Q("resume", "project", ["r:proj:0"]),
        Q("resume", "project", ["r:proj:0"]),
        Q("resume", "basic", ["r:skill:0"]),
        Q("gap", "scenario", ["j:req:0"]),
        Q("gap", "scenario", ["j:req:1"]),
        Q("gap", "scenario", ["j:req:0"]),
        Q("general", "behavior", []),
        Q("general", "basic", [])
    ];

    private static GeneratedQuestion Q(string source, string type, List<string> anchors) => new()
    {
        Source = source,
        Type = type,
        AnchorIds = anchors,
        Content = $"这是一道{source}类的面试题，用于验证校验逻辑",
        Intent = "校验"
    };

    private static List<string> Check(List<GeneratedQuestion> questions) =>
        QuestionValidator.Validate(questions, Plan, ResumeAnchors, JdAnchors, Match());

    [Fact]
    public void 合法出题没有问題()
    {
        Assert.Empty(Check(Valid()));
    }

    [Fact]
    public void 数量不符被拒()
    {
        var questions = Valid();
        questions.RemoveAt(7);

        Assert.Contains(Check(questions), p => p.Contains("题目数量应为 8"));
    }

    [Fact]
    public void 配比不符被拒()
    {
        var questions = Valid();
        questions[6].Source = "resume";

        Assert.Contains(Check(questions), p => p.Contains("通用题应为 2"));
    }

    [Fact]
    public void source非法被拒()
    {
        var questions = Valid();
        questions[6].Source = "unknown";

        Assert.Contains(Check(questions), p => p.Contains("source 非法"));
    }

    [Fact]
    public void 缺口题引用非missing锚点被拒()
    {
        var questions = Valid();
        questions[3].AnchorIds = ["j:resp:0"];

        Assert.Contains(Check(questions), p => p.Contains("不是 missing"));
    }

    [Fact]
    public void 缺口题引用未命中原文的锚点被拒()
    {
        var questions = Valid();
        questions[5].AnchorIds = ["j:req:2"];

        Assert.Contains(Check(questions), p => p.Contains("未命中原文"));
    }

    [Fact]
    public void 缺口题没有JD锚点被拒()
    {
        var questions = Valid();
        questions[3].AnchorIds = [];

        Assert.Contains(Check(questions), p => p.Contains("没有引用任何 JD 锚点"));
    }

    [Fact]
    public void 简历题没有简历锚点被拒()
    {
        var questions = Valid();
        questions[0].AnchorIds = [];

        Assert.Contains(Check(questions), p => p.Contains("没有引用任何简历锚点"));
    }

    [Fact]
    public void 锚点不存在被拒()
    {
        var questions = Valid();
        questions[0].AnchorIds = ["r:proj:99"];

        Assert.Contains(Check(questions), p => p.Contains("不存在的锚点"));
    }

    [Fact]
    public void 题型非法被拒()
    {
        var questions = Valid();
        questions[0].Type = "coding";

        Assert.Contains(Check(questions), p => p.Contains("type 非法"));
    }

    [Fact]
    public void 内容过短被拒()
    {
        var questions = Valid();
        questions[0].Content = "短";

        Assert.Contains(Check(questions), p => p.Contains("内容过短"));
    }

    [Fact]
    public void 模型返回null字段时不崩溃而是报校验问题()
    {
        var questions = Valid();
        questions[0].Content = null!;
        questions[1].AnchorIds = null!;
        questions[2].Source = null!;

        var problems = Check(questions);

        Assert.NotEmpty(problems);
    }

    [Fact]
    public void 列表里出现null元素时不崩溃_标记该题为空()
    {
        var questions = Valid();
        questions[6] = null!;

        var problems = Check(questions);

        Assert.Contains(problems, p => p.Contains("为空"));
    }
}
