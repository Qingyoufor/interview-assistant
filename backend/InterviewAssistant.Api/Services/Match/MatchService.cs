using System.Text.Json;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Llm;

namespace InterviewAssistant.Api.Services.Match;

public sealed record MatchInput(
    ResumeProfile Resume,
    IReadOnlyList<Anchor> ResumeAnchors,
    JobProfile Job,
    IReadOnlyList<Anchor> JdAnchors);

public sealed record MatchOutcome(MatchResult Result, int Repairs);

/// <summary>
/// 简历 × JD 加权匹配。模型只负责判断，分数合法性由 <see cref="MatchValidator"/> 校验，
/// 校验不过一次性 repair，仍不过就失败（不落库可疑结果）。
/// </summary>
public sealed class MatchService(DeepSeekClient llm, IHostEnvironment env, ILogger<MatchService> log)
{
    private readonly string _system = PromptLoader.Load(env, "match");

    public async Task<MatchOutcome> MatchAsync(MatchInput input, CancellationToken ct)
    {
        var jdKinds = MatchValidator.KindsOf(input.JdAnchors);
        var resumeIds = MatchValidator.IdsOf(input.ResumeAnchors);

        var userContent = Wrap(input, previous: null, problems: null);

        var result = await CallAsync(userContent, ct);
        var problems = MatchValidator.Validate(result, jdKinds, resumeIds);
        if (problems.Count == 0) return new MatchOutcome(result, 0);

        // 只记数量：problems 里可能含模型自由填写的文本，落日志会泄漏材料原文
        log.LogWarning("匹配结果校验未通过（{Count} 条），执行一次 repair", problems.Count);

        result = await CallAsync(Wrap(input, result, problems) + "\n\n上一版结果有以下问题，请逐条修正后重新输出完整 JSON：\n"
            + string.Join("\n", problems.Select(p => "- " + p)), ct);

        problems = MatchValidator.Validate(result, jdKinds, resumeIds);
        if (problems.Count > 0)
            throw new InvalidOperationException($"匹配结果两次校验未通过（{problems.Count} 项）");

        return new MatchOutcome(result, 1);
    }

    /// <summary>
    /// 把输入包进 &lt;match_input&gt; 标签。先转义标签本身，防止简历/JD 里的
    /// &lt;/match_input&gt; 提前闭合包裹、注入指令（CR-004）。
    /// </summary>
    private static string Wrap(MatchInput input, MatchResult? previous, List<string>? problems)
    {
        var json = JsonSerializer.Serialize(new
        {
            resume = input.Resume,
            job = input.Job,
            resumeAnchors = input.ResumeAnchors.Select(a => new { a.Id, a.Kind, a.Label, a.Quote }),
            jdAnchors = input.JdAnchors.Select(a => new { a.Id, a.Kind, a.Label, a.Quote }),
            previous,
            problems
        }, LlmJson.Options);

        // 把尖括号转成 Unicode 转义：材料里出现 </match_input> 也无法闭合边界（CR-004）
        json = json.Replace("<", "\\u003C", StringComparison.Ordinal)
                   .Replace(">", "\\u003E", StringComparison.Ordinal);

        return "<match_input>\n" + json + "\n</match_input>";
    }

    private async Task<MatchResult> CallAsync(string userContent, CancellationToken ct)
    {
        var content = await llm.ChatJsonAsync(_system, userContent, ct);
        try
        {
            return JsonSerializer.Deserialize<MatchResult>(content, LlmJson.Options)
                   ?? throw new InvalidOperationException("模型返回空结果");
        }
        catch (JsonException ex)
        {
            log.LogWarning("匹配返回非 JSON，长度 {Length}", content.Length);
            throw new InvalidOperationException("模型返回的内容不是合法 JSON", ex);
        }
    }
}
