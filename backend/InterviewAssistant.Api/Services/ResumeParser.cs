using System.Text.Encodings.Web;
using System.Text.Json;
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

public sealed record Anchor(string Id, string Kind, string Label, string Quote, bool QuoteExact);

public sealed record ResumeParseResult(ResumeProfile Profile, List<Anchor> Anchors);

public sealed class ResumeParser(DeepSeekClient llm, IHostEnvironment env, ILogger<ResumeParser> log)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _system = LoadPrompt(env, "resume-parse");

    public async Task<ResumeParseResult> ParseAsync(string rawText, CancellationToken ct)
    {
        var content = await llm.ChatJsonAsync(_system, rawText, ct);

        ResumeProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<ResumeProfile>(content, Json);
        }
        catch (JsonException ex)
        {
            log.LogError(ex, "简历解析返回非 JSON：{Content}", content);
            throw new InvalidOperationException("模型返回的内容不是合法 JSON", ex);
        }

        if (profile is null) throw new InvalidOperationException("模型返回空结果");
        if (profile.Experiences.Count == 0 && profile.Projects.Count == 0 && profile.Skills.Count == 0)
            throw new InvalidOperationException("没解析出任何经历、项目或技能，可能不是一份简历");

        var anchors = BuildAnchors(profile, rawText);
        log.LogInformation("简历解析完成：经历 {Exp} 段、项目 {Proj} 个、技能 {Skill} 项、锚点 {Anchor} 个",
            profile.Experiences.Count, profile.Projects.Count, profile.Skills.Count, anchors.Count);

        return new ResumeParseResult(profile, anchors);
    }

    public static List<Anchor> BuildAnchors(ResumeProfile p, string rawText)
    {
        var list = new List<Anchor>();

        for (var i = 0; i < p.Experiences.Count; i++)
        {
            var e = p.Experiences[i];
            var label = Join(e.Company, e.Title);
            var text = string.Join("；", e.Highlights.Where(NotBlank));
            list.Add(Make($"r:exp:{i}", "experience", label, text, rawText, e.Company));
        }

        for (var i = 0; i < p.Projects.Count; i++)
        {
            var pr = p.Projects[i];
            var text = string.Join("；", new[] { pr.Description }.Concat(pr.Metrics).Where(NotBlank));
            list.Add(Make($"r:proj:{i}", "project", pr.Name, text, rawText, pr.Name));
        }

        var skills = p.Skills.Where(NotBlank).Distinct().Take(30).ToList();
        for (var i = 0; i < skills.Count; i++)
            list.Add(Make($"r:skill:{i}", "skill", skills[i], skills[i], rawText));

        return list;
    }

    /// <summary>
    /// 在原文里定位锚点，并截取所在段落（最多 4 行 / 320 字）作为 quote。
    /// 找不到原文时退回解析文本，并标记 QuoteExact = false。
    /// </summary>
    private static Anchor Make(string id, string kind, string label, string text, string raw, params string?[] extraProbes)
    {
        var probes = new List<string?> { label };
        probes.AddRange(extraProbes);
        probes.Add(Head(text, 12));

        foreach (var probe in probes)
        {
            if (string.IsNullOrWhiteSpace(probe)) continue;

            // 优先命中“行首”出现的位置（标题行），避免落到正文里同名的提及
            var idx = FindLineStart(raw, probe);
            if (idx < 0) idx = raw.IndexOf(probe, StringComparison.Ordinal);
            if (idx < 0) continue;

            var (start, end) = Block(raw, idx);
            return new Anchor(id, kind, label, raw[start..end].Trim(), true);
        }

        return new Anchor(id, kind, label, Head(text, 200), false);
    }

    private static int FindLineStart(string raw, string probe)
    {
        var pos = 0;
        while (pos < raw.Length)
        {
            var lineEnd = raw.IndexOf('\n', pos);
            var end = lineEnd < 0 ? raw.Length : lineEnd;
            var line = raw[pos..end];
            var trimmed = line.TrimStart(' ', '\t', '\r');
            if (trimmed.StartsWith(probe, StringComparison.Ordinal))
                return pos + (line.Length - trimmed.Length);
            pos = end + 1;
        }
        return -1;
    }

    private static (int Start, int End) Block(string raw, int idx)
    {
        var start = raw.LastIndexOf('\n', Math.Max(0, idx - 1)) + 1;
        var end = start;
        for (var lines = 0; lines < 4 && end < raw.Length; lines++)
        {
            var nl = raw.IndexOf('\n', end);
            if (nl < 0) { end = raw.Length; break; }
            end = nl + 1;
            if (end - start > 320) break;
        }
        return (start, Math.Min(end, raw.Length));
    }

    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string Head(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n]);

    private static bool NotBlank(string? s) => !string.IsNullOrWhiteSpace(s);

    private static string LoadPrompt(IHostEnvironment env, string name)
    {
        var path = Path.Combine(env.ContentRootPath, "Services", "Prompts", name + ".md");
        if (!File.Exists(path)) throw new FileNotFoundException($"找不到 prompt 文件：{path}");
        return File.ReadAllText(path);
    }
}
