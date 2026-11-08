using System.Text.Json;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Llm;

namespace InterviewAssistant.Api.Services.Interview;

public sealed record InterviewContext(
    string Question,
    string Intent,
    string Source,
    int Depth,
    string Answer,
    IReadOnlyList<Anchor> Anchors,
    string? FollowUpQuestion = null);

public sealed record AssessOutcome(
    Assessment Assessment,
    StrategyDecision Decision,
    string? FollowUpText,
    int Repairs,
    bool FollowUpFallback);

/// <summary>
/// 作答评估 + 策略追问。模型只负责打分与措辞，下一步动作由 <see cref="StrategyEngine"/> 决定（spec §6）。
/// </summary>
public sealed class InterviewService(DeepSeekClient llm, IHostEnvironment env, ILogger<InterviewService> log)
{
    private const int MaxFollowUpChars = 200;

    private sealed class FollowUpPayload
    {
        public string Text { get; set; } = "";
    }

    private readonly string _assessPrompt = PromptLoader.Load(env, "assess");
    private readonly string _followUpPrompt = PromptLoader.Load(env, "followup");

    public async Task<AssessOutcome> EvaluateAsync(InterviewContext ctx, CancellationToken ct)
    {
        var anchorKinds = AnchorKinds(ctx.Anchors);
        var (assessment, repairs) = await AssessAsync(ctx, anchorKinds, ct);

        var decision = StrategyEngine.Decide(assessment, ctx.Source == "gap", ctx.Depth);

        var (followUp, fallback) = decision.IsFollowUp
            ? await FollowUpAsync(ctx, assessment, decision, ct)
            : ((string?)null, false);

        return new AssessOutcome(assessment, decision, followUp, repairs, fallback);
    }

    private async Task<(Assessment Assessment, int Repairs)> AssessAsync(
        InterviewContext ctx, IReadOnlyDictionary<string, string> anchorKinds, CancellationToken ct)
    {
        var userContent = Tag("assess_input", Wrap(new
        {
            question = ctx.Question,
            intent = ctx.Intent,
            source = ctx.Source,
            depth = ctx.Depth,
            followUpQuestion = ctx.FollowUpQuestion,
            answer = ctx.Answer,
            anchors = ctx.Anchors.Select(a => new { a.Id, a.Kind, a.Label, a.Quote })
        }));

        var assessment = await CallAssessWithRetryAsync(userContent, ct);
        var problems = AssessmentValidator.Validate(assessment, anchorKinds);
        if (problems.Count == 0) return (assessment, 0);

        log.LogWarning("评估校验未通过（{Count} 条），执行一次 repair", problems.Count);

        assessment = await CallAssessAsync(
            userContent + "\n\n上一版结果有以下问题，请逐条修正后重新输出完整 JSON：\n"
                         + string.Join("\n", problems.Select(p => "- " + p)), ct);

        problems = AssessmentValidator.Validate(assessment, anchorKinds);
        if (problems.Count > 0)
            throw new InvalidOperationException($"评估两次校验未通过（{problems.Count} 项）");

        return (assessment, 1);
    }

    private async Task<(string? Text, bool Fallback)> FollowUpAsync(
        InterviewContext ctx, Assessment assessment, StrategyDecision decision, CancellationToken ct)
    {
        var payload = Tag("followup_input", Wrap(new
        {
            axis = decision.Axis.ToString().ToLowerInvariant(),
            reason = decision.Reason,
            question = ctx.Question,
            answer = ctx.Answer,
            atoms = assessment.Atoms,
            contradictions = assessment.ClaimChecks
                .Where(c => c.Verdict == ClaimVerdicts.Contradicted)
                .Select(c => c.Claim)
        }));

        var content = await CallFollowUpAsync(payload, ct);

        try
        {
            var result = JsonSerializer.Deserialize<FollowUpPayload>(content, LlmJson.Options);
            var text = result?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                log.LogWarning("追问文本为空，使用兜底追问");
                return (Fallback(decision.Axis), true);
            }

            return (text.Length <= MaxFollowUpChars ? text : text[..MaxFollowUpChars], false);
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "追问返回非 JSON，使用兜底追问");
            return (Fallback(decision.Axis), true);
        }
    }

    /// <summary>模型偶尔返回非 JSON；先重试一次「只输出 JSON」，仍失败才抛。</summary>
    private async Task<Assessment> CallAssessWithRetryAsync(string userContent, CancellationToken ct)
    {
        try
        {
            return await CallAssessAsync(userContent, ct);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is JsonException)
        {
            log.LogWarning("评估返回非 JSON，重试一次");
            return await CallAssessAsync(userContent + "\n\n只输出一个合法 JSON 对象，不要任何解释。", ct);
        }
    }

    private async Task<Assessment> CallAssessAsync(string userContent, CancellationToken ct)
    {
        var content = await llm.ChatJsonAsync(_assessPrompt, userContent, ct);
        try
        {
            var assessment = JsonSerializer.Deserialize<Assessment>(content, LlmJson.Options)
                             ?? throw new InvalidOperationException("模型返回空结果");
            assessment.Normalize();
            return assessment;
        }
        catch (JsonException ex)
        {
            log.LogWarning("评估返回非 JSON，长度 {Length}", content.Length);
            throw new InvalidOperationException("模型返回的内容不是合法 JSON", ex);
        }
    }

    /// <summary>追问措辞的传输异常不能连累已经算出的评估——失败就走兜底文案。</summary>
    private async Task<string> CallFollowUpAsync(string payload, CancellationToken ct)
    {
        try
        {
            return await llm.ChatJsonAsync(_followUpPrompt, payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "追问调用失败，使用兜底追问");
            return "{}";
        }
    }

    /// <summary>锚点 id → kind。重复 id 只保留第一个，避免脏数据抛异常。</summary>
    private static Dictionary<string, string> AnchorKinds(IReadOnlyList<Anchor> anchors) =>
        anchors
            .GroupBy(a => a.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Kind, StringComparer.Ordinal);

    private static string Fallback(FollowUpAxis axis) => axis switch
    {
        FollowUpAxis.Prerequisite => "先回到基础：这个结论成立的前提是什么？如果前提不成立会怎样？",
        FollowUpAxis.Verification => "刚才这段和你在简历里写的不太一致，能说清当时具体是谁做的、做到哪一步吗？",
        FollowUpAxis.Metrics => "给几个具体数字：量级、基线，以及你优化前后的对比。",
        _ => "如果没有你说的这个条件，你原来是怎么解决的？换成它，原方案哪里会失效？"
    };

    private static string Tag(string name, string body) => $"<{name}>\n{body}\n</{name}>";

    /// <summary>把尖括号转义掉，材料里的标签无法伪造边界。</summary>
    private static string Wrap(object payload) =>
        JsonSerializer.Serialize(payload, LlmJson.Options)
            .Replace("<", "\\u003C", StringComparison.Ordinal)
            .Replace(">", "\\u003E", StringComparison.Ordinal);
}
