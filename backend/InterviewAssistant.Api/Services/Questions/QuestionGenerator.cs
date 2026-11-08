using System.Text.Json;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Llm;
using InterviewAssistant.Api.Services.Match;

namespace InterviewAssistant.Api.Services.Questions;

public sealed record QuestionGenInput(
    ResumeProfile Resume,
    IReadOnlyList<Anchor> ResumeAnchors,
    JobProfile Job,
    IReadOnlyList<Anchor> JdAnchors,
    MatchResult Match);

public sealed record QuestionGenOutcome(IReadOnlyList<GeneratedQuestion> Questions, QuestionPlan Plan, int Repairs);

public sealed class QuestionGenerator(DeepSeekClient llm, IHostEnvironment env, ILogger<QuestionGenerator> log)
{
    private sealed class Payload
    {
        public List<GeneratedQuestion> Questions { get; set; } = [];
    }

    private readonly string _system = PromptLoader.Load(env, "question-gen");

    public async Task<QuestionGenOutcome> GenerateAsync(QuestionGenInput input, CancellationToken ct)
    {
        var gaps = QuestionPlanner.ExactMissing(input.Match, input.JdAnchors);
        var plan = QuestionPlanner.Plan(input.Match, input.JdAnchors);

        var questions = await CallAsync(Wrap(input, plan, gaps, previous: null, problems: null), ct);
        var problems = QuestionValidator.Validate(questions, plan, input.ResumeAnchors, input.JdAnchors, input.Match);
        if (problems.Count == 0) return new QuestionGenOutcome(questions, plan, 0);

        log.LogWarning("出题校验未通过（{Count} 条），执行一次 repair", problems.Count);

        questions = await CallAsync(
            Wrap(input, plan, gaps, previous: questions, problems: problems)
            + "\n\n上一版题目有以下问题，请逐条修正后重新输出完整 JSON：\n"
            + string.Join("\n", problems.Select(p => "- " + p)), ct);

        problems = QuestionValidator.Validate(questions, plan, input.ResumeAnchors, input.JdAnchors, input.Match);
        if (problems.Count > 0)
            throw new InvalidOperationException($"出题两次校验未通过（{problems.Count} 项）");

        return new QuestionGenOutcome(questions, plan, 1);
    }

    private static string Wrap(QuestionGenInput input, QuestionPlan plan, HashSet<string> gaps, IReadOnlyList<GeneratedQuestion>? previous, List<string>? problems)
    {
        var json = JsonSerializer.Serialize(new
        {
            resume = input.Resume,
            job = input.Job,
            resumeAnchors = input.ResumeAnchors.Select(a => new { a.Id, a.Kind, a.Label, a.Quote }),
            jdAnchors = input.JdAnchors.Select(a => new { a.Id, a.Kind, a.Label, a.Quote }),
            matchItems = input.Match.Items.Select(i => new { i.JdAnchorId, i.Category, i.Verdict, i.Requirement }),
            gaps = gaps.OrderBy(id => id),
            plan,
            previous,
            problems
        }, LlmJson.Options);

        return WrapTagged(json);
    }

    /// <summary>
    /// 把 JSON 里的尖括号转成 Unicode 转义，材料里出现 &lt;question_input&gt; 也无法伪造边界。
    /// 只转义内容，包裹标签在之后拼接，不受影响。
    /// </summary>
    private static string WrapTagged(string json) =>
        "<question_input>\n"
        + json.Replace("<", "\\u003C", StringComparison.Ordinal).Replace(">", "\\u003E", StringComparison.Ordinal)
        + "\n</question_input>";

    private async Task<List<GeneratedQuestion>> CallAsync(string userContent, CancellationToken ct)
    {
        var content = await llm.ChatJsonAsync(_system, userContent, ct);
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(content, LlmJson.Options)
                          ?? throw new InvalidOperationException("模型返回空结果");

            // 模型可能返回 [null]，过滤掉以免后续校验空引用
            return (payload.Questions ?? []).Where(q => q is not null).ToList();
        }
        catch (JsonException ex)
        {
            log.LogWarning("出题返回非 JSON，长度 {Length}", content.Length);
            throw new InvalidOperationException("模型返回的内容不是合法 JSON", ex);
        }
    }
}
