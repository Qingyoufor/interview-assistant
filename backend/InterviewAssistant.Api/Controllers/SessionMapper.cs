using System.Text.Json;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Llm;
using InterviewAssistant.Api.Services.Match;

namespace InterviewAssistant.Api.Controllers;

/// <summary>会话相关的 DTO 组装与枚举兜底，供几个 Controller 共用。</summary>
public static class SessionMapper
{
    public static SessionResponse ToResponse(Session session, MatchResult match, List<Question> questions) =>
        new(session.Id,
            session.ResumeId ?? Guid.Empty,
            session.JobTargetId,
            session.MatchId ?? Guid.Empty,
            session.Status.ToString(),
            session.StartedAt,
            match,
            questions.Select(ToDto).ToList());

    public static SessionQuestionDto ToDto(Question q) =>
        new(q.Id, q.Ordinal, q.Type.ToString(), q.Source.ToString(), q.Content, q.Intent ?? "",
            DeserializeIds(q.AnchorIds));

    public static SessionQuestionDto? NextQuestion(Session session, Question current) =>
        session.Questions
            .Where(q => q.Ordinal > current.Ordinal)
            .OrderBy(q => q.Ordinal)
            .Select(ToDto)
            .FirstOrDefault();

    /// <summary>所有题都答完就把会话标记为完成（spec §6 的 complete 分支）。</summary>
    public static void MarkCompleteIfDone(Session session)
    {
        if (session.Questions.Count == 0 || session.Questions.Any(q => q.Answer is null)) return;

        session.Status = SessionStatus.completed;
        session.CompletedAt = DateTime.UtcNow;
    }

    public static List<string> DeserializeIds(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json, LlmJson.Options) ?? [];

    public static Assessment? ParseAssessment(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        var assessment = JsonSerializer.Deserialize<Assessment>(json, LlmJson.Options);
        assessment?.Normalize();
        return assessment;
    }

    public static QuestionType ParseType(string value) =>
        Enum.TryParse<QuestionType>(value, ignoreCase: true, out var parsed) ? parsed : QuestionType.basic;

    public static QuestionSource ParseSource(string value) =>
        Enum.TryParse<QuestionSource>(value, ignoreCase: true, out var parsed) ? parsed : QuestionSource.general;
}
