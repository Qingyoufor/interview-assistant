using System.Text.Json;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Llm;

namespace InterviewAssistant.Api.Services;

public sealed class JobProfile
{
    public string Title { get; set; } = "";
    public string Level { get; set; } = "";
    public List<string> Responsibilities { get; set; } = [];
    public List<string> MustHave { get; set; } = [];
    public List<string> NiceToHave { get; set; } = [];
    public List<string> FocusAreas { get; set; } = [];
}

public sealed record JobParseResult(JobProfile Profile, List<Anchor> Anchors);

public sealed class JdParser(DeepSeekClient llm, IHostEnvironment env, ILogger<JdParser> log)
{
    private readonly string _system = PromptLoader.Load(env, "jd-parse");

    public async Task<JobParseResult> ParseAsync(string jdText, CancellationToken ct)
    {
        var content = await llm.ChatJsonAsync(_system, $"<jd>\n{jdText}\n</jd>", ct);

        JobProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<JobProfile>(content, LlmJson.Options);
        }
        catch (JsonException ex)
        {
            // 不记录模型输出原文：JD 里常带招聘方联系方式，只留长度
            log.LogWarning("JD 解析返回非 JSON，长度 {Length}", content.Length);
            throw new InvalidOperationException("模型返回的内容不是合法 JSON", ex);
        }

        if (profile is null) throw new InvalidOperationException("模型返回空结果");

        // 模型可能显式返回 null（"mustHave": null），先归一化再判断
        profile.Responsibilities ??= [];
        profile.MustHave ??= [];
        profile.NiceToHave ??= [];
        profile.FocusAreas ??= [];

        if (profile.Responsibilities.Count == 0 && profile.MustHave.Count == 0)
            throw new InvalidOperationException("没解析出任何职责或要求，可能不是一份 JD");

        var anchors = BuildAnchors(profile, jdText);
        var exact = anchors.Count(a => a.QuoteExact);
        log.LogInformation("JD 解析完成：职责 {Resp} 条、硬性要求 {Must} 条、加分 {Nice} 条、锚点 {Anchor} 个（原文命中 {Exact}）",
            profile.Responsibilities.Count, profile.MustHave.Count, profile.NiceToHave.Count, anchors.Count, exact);

        // 原文命中率偏低说明模型在改写，gap 题将无法引用原文（spec §15.2）
        if (anchors.Count > 0 && exact < anchors.Count)
            log.LogWarning("JD 锚点原文命中率 {Rate:P0}，低于 100% 会导致 gap 题无法引用原文", (double)exact / anchors.Count);

        return new JobParseResult(profile, anchors);
    }

    public static List<Anchor> BuildAnchors(JobProfile p, string rawText)
    {
        var list = new List<Anchor>();
        var mustHave = p.MustHave ?? [];
        var responsibilities = p.Responsibilities ?? [];

        for (var i = 0; i < mustHave.Count; i++)
        {
            var item = mustHave[i];
            list.Add(AnchorLocator.Locate($"j:req:{i}", "requirement", item, item, rawText));
        }

        for (var i = 0; i < responsibilities.Count; i++)
        {
            var item = responsibilities[i];
            list.Add(AnchorLocator.Locate($"j:resp:{i}", "responsibility", item, item, rawText));
        }

        return list;
    }
}
