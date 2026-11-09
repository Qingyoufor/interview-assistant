using System.Text.Json;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Llm;

namespace InterviewAssistant.Api.Services.Interview;

/// <summary>评估 / 锚点 id 的 JSON 读写（控制器和报告服务共用）。</summary>
public static class AssessmentJson
{
    /// <summary>脏行（手工改库或 schema 漂移）当作「没有评估」，不让接口 500。</summary>
    public static Assessment? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var assessment = JsonSerializer.Deserialize<Assessment>(json, LlmJson.Options);
            assessment?.Normalize();
            return assessment;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static List<string> ReadIds(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json, LlmJson.Options) ?? [];
}
