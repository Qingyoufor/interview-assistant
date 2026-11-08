using System.Text.Json;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Llm;

namespace InterviewAssistant.Api.Services;

public sealed class ResumeProfile
{
    public string Summary { get; set; } = "";
    public int TotalYears { get; set; }
    public List<string> Skills { get; set; } = [];
    public List<ExperienceItem> Experiences { get; set; } = [];
    public List<ProjectItem> Projects { get; set; } = [];
    public List<string> Strengths { get; set; } = [];
    public List<string> Gaps { get; set; } = [];
}

public sealed class ExperienceItem
{
    public string Company { get; set; } = "";
    public string Title { get; set; } = "";
    public string Period { get; set; } = "";
    public List<string> Highlights { get; set; } = [];
}

public sealed class ProjectItem
{
    public string Name { get; set; } = "";
    public string Period { get; set; } = "";
    public List<string> Tech { get; set; } = [];
    public string Description { get; set; } = "";
    public List<string> Metrics { get; set; } = [];
}

public sealed record ResumeParseResult(ResumeProfile Profile, List<Anchor> Anchors);

public sealed class ResumeParser(DeepSeekClient llm, IHostEnvironment env, ILogger<ResumeParser> log)
{
    private readonly string _system = PromptLoader.Load(env, "resume-parse");

    public async Task<ResumeParseResult> ParseAsync(string rawText, CancellationToken ct)
    {
        var content = await llm.ChatJsonAsync(_system, $"<resume>\n{rawText}\n</resume>", ct);

        ResumeProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<ResumeProfile>(content, LlmJson.Options);
        }
        catch (JsonException ex)
        {
            // 不记录模型输出原文：简历内容属敏感数据，只留长度便于排查
            log.LogWarning("简历解析返回非 JSON，长度 {Length}", content.Length);
            throw new InvalidOperationException("模型返回的内容不是合法 JSON", ex);
        }

        if (profile is null) throw new InvalidOperationException("模型返回空结果");

        // 模型可能显式返回 null（"skills": null），先归一化再判断
        profile.Skills ??= [];
        profile.Experiences ??= [];
        profile.Projects ??= [];
        profile.Strengths ??= [];
        profile.Gaps ??= [];
        foreach (var e in profile.Experiences) e.Highlights ??= [];
        foreach (var p in profile.Projects) { p.Tech ??= []; p.Metrics ??= []; }

        if (profile.Experiences.Count == 0 && profile.Projects.Count == 0 && profile.Skills.Count == 0)
            throw new InvalidOperationException("没解析出任何经历、项目或技能，可能不是一份简历");

        var anchors = BuildAnchors(profile, rawText);
        log.LogInformation("简历解析完成：经历 {Exp} 段、项目 {Proj} 个、技能 {Skill} 项、锚点 {Anchor} 个（原文命中 {Exact}）",
            profile.Experiences.Count, profile.Projects.Count, profile.Skills.Count, anchors.Count, anchors.Count(a => a.QuoteExact));

        return new ResumeParseResult(profile, anchors);
    }

    public static List<Anchor> BuildAnchors(ResumeProfile p, string rawText)
    {
        var list = new List<Anchor>();

        for (var i = 0; i < p.Experiences.Count; i++)
        {
            var e = p.Experiences[i];
            var label = string.Join(" · ", new[] { e.Company, e.Title }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var text = string.Join("；", (e.Highlights ?? []).Where(s => !string.IsNullOrWhiteSpace(s)));
            list.Add(AnchorLocator.Locate($"r:exp:{i}", "experience", label, text, rawText, e.Company));
        }

        for (var i = 0; i < p.Projects.Count; i++)
        {
            var pr = p.Projects[i];
            var text = string.Join("；", new[] { pr.Description }.Concat(pr.Metrics ?? []).Where(s => !string.IsNullOrWhiteSpace(s)));
            list.Add(AnchorLocator.Locate($"r:proj:{i}", "project", pr.Name, text, rawText, pr.Name));
        }

        var skills = (p.Skills ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Take(AnchorLocator.MaxSkillAnchors).ToList();
        for (var i = 0; i < skills.Count; i++)
            list.Add(AnchorLocator.Locate($"r:skill:{i}", "skill", skills[i], skills[i], rawText));

        return list;
    }
}
