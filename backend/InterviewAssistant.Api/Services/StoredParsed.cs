using InterviewAssistant.Api.Models;

namespace InterviewAssistant.Api.Services;

/// <summary>读写实体里存的 parsed_json（{profile, anchors} / {job, anchors}）。</summary>
public static class StoredParsed
{
    private sealed record ResumeDoc(ResumeProfile Profile, List<Anchor> Anchors);
    private sealed record JobDoc(JobProfile Job, List<Anchor> Anchors);

    public static (ResumeProfile Profile, List<Anchor> Anchors) ReadResume(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("简历还没有解析结果");
        var doc = System.Text.Json.JsonSerializer.Deserialize<ResumeDoc>(json, Services.Llm.LlmJson.Options)
                  ?? throw new InvalidOperationException("简历解析结果无法读取");
        if (doc.Profile is null) throw new InvalidOperationException("简历解析结果缺少 profile");
        return (doc.Profile, doc.Anchors ?? []);
    }

    public static (JobProfile Job, List<Anchor> Anchors) ReadJob(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("JD 还没有解析结果");
        var doc = System.Text.Json.JsonSerializer.Deserialize<JobDoc>(json, Services.Llm.LlmJson.Options)
                  ?? throw new InvalidOperationException("JD 解析结果无法读取");
        if (doc.Job is null) throw new InvalidOperationException("JD 解析结果缺少 job");
        return (doc.Job, doc.Anchors ?? []);
    }
}
