namespace InterviewAssistant.Api.Models;

public enum SessionStatus { active, completed, abandoned }
public enum QuestionType { basic, project, scenario, behavior }
public enum ScoreDimension { structure, depth, evidence, delivery }

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

public class Session
{
    public Guid Id { get; set; }
    public Guid JobTargetId { get; set; }
    public string UserId { get; set; } = "local";
    public SessionStatus Status { get; set; } = SessionStatus.active;
    public decimal? OverallScore { get; set; }
    public string? Summary { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public JobTarget? JobTarget { get; set; }
    public List<Question> Questions { get; set; } = new();
}

public class Question
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public int Ordinal { get; set; }
    public QuestionType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Intent { get; set; }
    public string? Answer { get; set; }
    public string? FollowUp { get; set; }
    public string? FollowUpAnswer { get; set; }
    public string? Feedback { get; set; }
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
