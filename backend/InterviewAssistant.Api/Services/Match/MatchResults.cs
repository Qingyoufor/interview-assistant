using System.Text.Json;
using InterviewAssistant.Api.Services.Llm;

namespace InterviewAssistant.Api.Services.Match;

/// <summary>从数据库里存的 JSON 读匹配结果，统一做归一化（模型可能返回 null 字段）。</summary>
public static class MatchResults
{
    public static MatchResult Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new MatchResult();

        var result = JsonSerializer.Deserialize<MatchResult>(json, LlmJson.Options) ?? new MatchResult();
        result.Normalize();
        return result;
    }
}
