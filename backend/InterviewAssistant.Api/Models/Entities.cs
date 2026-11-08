namespace InterviewAssistant.Api.Models;

public enum SessionStatus { active, completed, abandoned }
public enum QuestionType { basic, project, scenario, behavior }
public enum QuestionSource { resume, gap, general }
public enum ScoreDimension { structure, depth, evidence, delivery }

public class Resume
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "local";
    public string Title { get; set; } = string.Empty;
    public string RawText { get; set; } = string.Empty;
    public string? ParsedJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class JobTarget
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "local";
    public string Title { get; set; } = string.Empty;
    public string? Level { get; set; }
    public string JdText { get; set; } = string.Empty;
    public string? ParsedJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Match
{
    public Guid Id { get; set; }
    public Guid ResumeId { get; set; }
    public Guid JobTargetId { get; set; }
    public string InputHash { get; set; } = string.Empty;
    public byte OverallMatch { get; set; }
    public bool HardBlock { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Resume? Resume { get; set; }
    public JobTarget? JobTarget { get; set; }
}

public class Session
{
    public Guid Id { get; set; }
    public Guid JobTargetId { get; set; }
    public Guid? ResumeId { get; set; }
    public Guid? MatchId { get; set; }
    public string UserId { get; set; } = "local";
    public SessionStatus Status { get; set; } = SessionStatus.active;
    public decimal? OverallScore { get; set; }
    public string? Summary { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public JobTarget? JobTarget { get; set; }
    public Resume? Resume { get; set; }
    public Match? Match { get; set; }
    public List<Question> Questions { get; set; } = new();
}

public class Question
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public int Ordinal { get; set; }
    public QuestionType Type { get; set; }
    public QuestionSource Source { get; set; } = QuestionSource.general;
    public string Content { get; set; } = string.Empty;
    public string? Intent { get; set; }
    public string? AnchorIds { get; set; }
    public string? Answer { get; set; }
    public string? FollowUp { get; set; }
    public string? FollowUpAnswer { get; set; }
    public string? Feedback { get; set; }
    public string? AssessmentJson { get; set; }
    public string? ClaimChecks { get; set; }
    public DateTime CreatedAt { get; set; }

    public Session? Session { get; set; }
    public List<QuestionScore> Scores { get; set; } = new();
}

public class QuestionScore
{
    public Guid Id { get; set; }
    public Guid QuestionId { get; set; }
    public ScoreDimension Dimension { get; set; }
    public byte Score { get; set; }
    public string? Comment { get; set; }

    public Question? Question { get; set; }
}
