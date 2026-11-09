using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Match;

namespace InterviewAssistant.Api.Controllers;

public sealed record CreateSessionRequest(Guid ResumeId, Guid JobTargetId);

public sealed record SubmitAnswerRequest(string? Answer);

public sealed record SessionQuestionDto(
    Guid Id, int Ordinal, string Type, string Source, string Content, string Intent,
    List<string> AnchorIds, bool Answered, string? FollowUp, bool FollowUpAnswered, Assessment? Assessment);

public sealed record SessionResponse(
    Guid Id, Guid ResumeId, Guid JobTargetId, Guid MatchId, string Status,
    DateTime StartedAt, MatchResult Match, List<SessionQuestionDto> Questions);

public sealed record AnswerResponse(
    string Action, string? FollowUp, bool FollowUpFallback, Assessment Assessment, SessionQuestionDto? NextQuestion);
