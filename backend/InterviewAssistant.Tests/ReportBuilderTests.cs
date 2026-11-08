using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Match;
using InterviewAssistant.Api.Services.Report;

namespace InterviewAssistant.Tests;

public class ReportBuilderTests
{
    private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static MatchResult Match() => new()
    {
        OverallMatch = 63,
        HardBlock = true,
        Breakdown = new MatchBreakdown { MustHave = 24, Responsibilities = 20, EvidenceQuality = 14, Bonus = 5 },
        TopRisks = ["Kafka 无实战", "无高并发量级数据"],
        Items =
        [
            new MatchItem { JdAnchorId = "j:req:0", Requirement = "5 年经验", Verdict = MatchVerdict.Partial },
            new MatchItem { JdAnchorId = "j:req:1", Requirement = "Spring Cloud", Verdict = MatchVerdict.Partial },
            new MatchItem { JdAnchorId = "j:req:2", Requirement = "熟悉 Kafka", Verdict = MatchVerdict.Missing },
            new MatchItem { JdAnchorId = "j:req:3", Requirement = "MySQL 调优", Verdict = MatchVerdict.Missing },
            new MatchItem { JdAnchorId = "j:req:4", Requirement = "英语", Verdict = MatchVerdict.Missing }
        ]
    };

    private static Assessment AssessmentOf(params string[] weaknesses) => new()
    {
        Weaknesses = [.. weaknesses],
        Strengths = ["结构清晰"],
        Remediation = weaknesses.Length > 0 ? "补一个 Kafka 的小项目" : null
    };

    private static QuestionReport Q(
        string source,
        Assessment? assessment,
        int structure, int depth, int evidence, int delivery,
        string content = "题目内容",
        params string[] anchors) => new(
            source,
            assessment,
            new Dictionary<string, int>
            {
                ["structure"] = structure,
                ["depth"] = depth,
                ["evidence"] = evidence,
                ["delivery"] = delivery
            },
            anchors,
            content);

    [Fact]
    public void 一题未答时总分为零()
    {
        var report = ReportBuilder.Build(SessionId, [Q("resume", null, 0, 0, 0, 0)], Match());

        Assert.Equal(0, report.OverallScore);
        Assert.Equal(0, report.AnsweredQuestions);
        Assert.Equal(1, report.TotalQuestions);
    }

    [Fact]
    public void 四维均分与总分换算正确()
    {
        var report = ReportBuilder.Build(SessionId,
            [Q("resume", AssessmentOf("量化结果"), 4, 4, 4, 4)],
            Match());

        Assert.Equal(4, report.Dimensions.Single(d => d.Dimension == "structure").Average);
        Assert.Equal(80, report.OverallScore);
    }

    [Fact]
    public void 已作答但分数全零时不计高分()
    {
        var report = ReportBuilder.Build(SessionId,
            [Q("resume", AssessmentOf("量化结果"), 0, 0, 0, 0)],
            Match());

        Assert.Equal(0, report.OverallScore);
        Assert.Equal(1, report.AnsweredQuestions);
        Assert.All(report.Dimensions, d => Assert.Equal(0, d.Average));
        Assert.All(report.Dimensions, d => Assert.Equal(1, d.Count));
    }

    [Fact]
    public void 弱点超过五条只保留前五()
    {
        var report = ReportBuilder.Build(SessionId,
            [
                Q("resume", AssessmentOf("量化结果", "量化结果", "技术深度", "并发", "事务", "测试", "沟通表达"), 3, 3, 3, 3)
            ],
            Match());

        Assert.Equal(5, report.Weaknesses.Count);
        Assert.Equal("量化结果", report.Weaknesses[0].Tag);
        Assert.Equal(2, report.Weaknesses[0].Count);
    }

    [Fact]
    public void 缺口题的补救建议按严重度排序并最多三条()
    {
        var report = ReportBuilder.Build(SessionId,
            [
                Q("gap", AssessmentOf("量化结果"), 3, 3, 3, 3, "题目0", "j:req:0"),
                Q("gap", AssessmentOf("技术深度"), 3, 3, 3, 3, "题目1", "j:req:1"),
                Q("gap", AssessmentOf("并发"), 3, 3, 3, 3, "题目2", "j:req:2"),
                Q("gap", AssessmentOf("事务"), 3, 3, 3, 3, "题目3", "j:req:3"),
                Q("gap", AssessmentOf("测试"), 3, 3, 3, 3, "题目4", "j:req:4")
            ],
            Match());

        Assert.Equal(3, report.TopGaps.Count);
        // missing 排在 partial 前面
        Assert.Equal("熟悉 Kafka", report.TopGaps[0].Requirement);
        Assert.Equal("题目2", report.TopGaps[0].Question);
    }

    [Fact]
    public void 锚点找不到时回退到题目内容而不是折叠成一条()
    {
        var report = ReportBuilder.Build(SessionId,
            [
                Q("gap", AssessmentOf("量化结果"), 3, 3, 3, 3, "第一道缺口题"),
                Q("gap", AssessmentOf("技术深度"), 3, 3, 3, 3, "第二道缺口题")
            ],
            Match());

        Assert.Equal(2, report.TopGaps.Count);
        Assert.Equal("第一道缺口题", report.TopGaps[0].Requirement);
    }

    [Fact]
    public void 报告透传匹配的加权拆分与风险()
    {
        var report = ReportBuilder.Build(SessionId, [Q("resume", AssessmentOf("量化结果"), 3, 3, 3, 3)], Match());

        Assert.True(report.HardBlock);
        Assert.Equal(63, report.MatchOverall);
        Assert.Equal(24, report.Breakdown.MustHave);
        Assert.Equal(2, report.TopRisks.Count);
        Assert.Contains("综合得分", report.Summary);
    }

    [Fact]
    public void 没有弱点和缺口时摘要不出现暂无()
    {
        var report = ReportBuilder.Build(SessionId, [], Match());

        Assert.DoesNotContain("暂无", report.Summary);
        Assert.Contains("还没有足够的作答数据", report.Summary);
    }
}
