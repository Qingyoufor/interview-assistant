using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Interview;

namespace InterviewAssistant.Tests;

public class AssessmentValidatorTests
{
    private static readonly Dictionary<string, string> Anchors = new(StringComparer.Ordinal)
    {
        ["r:proj:0"] = "project",
        ["r:skill:0"] = "skill",
        ["j:req:0"] = "requirement"
    };

    private static Assessment Valid() => new()
    {
        Atoms = new AtomScores { Correctness = 4, Depth = 3, Specificity = 4, Ownership = 3, Metrics = 2, Tradeoffs = 3 },
        Display = new DisplayScores { Structure = 4, Depth = 3, Evidence = 4, Delivery = 3 },
        ClaimChecks =
        [
            new ClaimCheck { Claim = "负责订单中心重构", Verdict = ClaimVerdicts.Supported, EvidenceAnchorIds = ["r:proj:0"] }
        ],
        Weaknesses = ["量化结果"],
        Strengths = ["结构清晰"],
        Improvements = ["补上具体数字"]
    };

    [Fact]
    public void 合法评估没有问题()
    {
        Assert.Empty(AssessmentValidator.Validate(Valid(), Anchors));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void 原子分越界被拒(int value)
    {
        var assessment = Valid();
        assessment.Atoms.Correctness = value;

        Assert.Contains(AssessmentValidator.Validate(assessment, Anchors), p => p.Contains("原子分 correctness"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void 展示分越界被拒(int value)
    {
        var assessment = Valid();
        assessment.Display.Delivery = value;

        Assert.Contains(AssessmentValidator.Validate(assessment, Anchors), p => p.Contains("展示分 delivery"));
    }

    [Fact]
    public void 声明结论非法被拒()
    {
        var assessment = Valid();
        assessment.ClaimChecks[0].Verdict = "maybe";

        Assert.Contains(AssessmentValidator.Validate(assessment, Anchors), p => p.Contains("verdict 非法"));
    }

    [Fact]
    public void 判为supported却没有锚点被拒()
    {
        var assessment = Valid();
        assessment.ClaimChecks[0].EvidenceAnchorIds = [];

        Assert.Contains(AssessmentValidator.Validate(assessment, Anchors), p => p.Contains("没有引用任何锚点"));
    }

    [Fact]
    public void 判为contradicted却没有锚点被拒()
    {
        var assessment = Valid();
        assessment.ClaimChecks[0].Verdict = ClaimVerdicts.Contradicted;
        assessment.ClaimChecks[0].EvidenceAnchorIds = [];

        Assert.Contains(AssessmentValidator.Validate(assessment, Anchors), p => p.Contains("没有引用任何锚点"));
    }

    [Fact]
    public void 声明不能拿JD锚点当证据()
    {
        var assessment = Valid();
        assessment.ClaimChecks[0].EvidenceAnchorIds = ["j:req:0"];

        Assert.Contains(AssessmentValidator.Validate(assessment, Anchors), p => p.Contains("只能用简历锚点"));
    }

    [Fact]
    public void 空评估对象因分数为0被拒()
    {
        var problems = AssessmentValidator.Validate(new Assessment(), Anchors);

        Assert.Contains(problems, p => p.Contains("原子分 correctness"));
    }

    [Fact]
    public void 声明引用不存在的锚点被拒()
    {
        var assessment = Valid();
        assessment.ClaimChecks[0].EvidenceAnchorIds = ["r:proj:9"];

        Assert.Contains(AssessmentValidator.Validate(assessment, Anchors), p => p.Contains("不存在的锚点"));
    }

    [Fact]
    public void 词表外的弱点标签被丢弃而不是拒收()
    {
        var assessment = Valid();
        assessment.Weaknesses = ["量化结果", "表达不够自信", "技术深度"];

        var problems = AssessmentValidator.Validate(assessment, Anchors);

        Assert.Empty(problems);
        Assert.Equal(["量化结果", "技术深度"], assessment.Weaknesses);
    }

    [Fact]
    public void 弱点为空被拒()
    {
        var assessment = Valid();
        assessment.Weaknesses = [];

        Assert.Contains(AssessmentValidator.Validate(assessment, Anchors), p => p.Contains("至少需要 1 个"));
    }

    [Fact]
    public void 弱点超过四个时截断()
    {
        var assessment = Valid();
        assessment.Weaknesses = ["量化结果", "技术深度", "并发", "事务", "测试", "沟通表达"];

        AssessmentValidator.Validate(assessment, Anchors);

        Assert.Equal(4, assessment.Weaknesses.Count);
    }

    [Fact]
    public void 模型返回null字段时不崩溃()
    {
        var assessment = Valid();
        assessment.Atoms = null!;
        assessment.ClaimChecks = null!;
        assessment.Weaknesses = null!;

        var problems = AssessmentValidator.Validate(assessment, Anchors);

        Assert.Contains(problems, p => p.Contains("weaknesses 至少需要"));
    }

    [Fact]
    public void 词表本身是二十五个标签()
    {
        Assert.Equal(25, WeaknessCatalog.All.Length);
        Assert.Equal(25, WeaknessCatalog.All.Distinct().Count());
    }
}
