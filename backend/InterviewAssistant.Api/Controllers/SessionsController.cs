using System.Text.Json;
using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services;
using InterviewAssistant.Api.Services.Interview;
using InterviewAssistant.Api.Services.Llm;
using InterviewAssistant.Api.Services.Match;
using InterviewAssistant.Api.Services.Questions;
using InterviewAssistant.Api.Services.Report;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

public sealed record CreateSessionRequest(Guid ResumeId, Guid JobTargetId);

public sealed record SubmitAnswerRequest(string? Answer);

public sealed record SessionQuestionDto(Guid Id, int Ordinal, string Type, string Source, string Content, string Intent, List<string> AnchorIds);

public sealed record SessionResponse(
    Guid Id, Guid ResumeId, Guid JobTargetId, Guid MatchId, string Status,
    DateTime StartedAt, MatchResult Match, List<SessionQuestionDto> Questions);

public sealed record AnswerResponse(
    string Action, string? FollowUp, bool FollowUpFallback, Assessment Assessment, SessionQuestionDto? NextQuestion);

[ApiController]
[Route("api/sessions")]
[Produces("application/json")]
public class SessionsController(
    AppDbContext db,
    MatchStore matchStore,
    QuestionGenerator generator,
    InterviewService interview,
    ILogger<SessionsController> log) : ControllerBase
{
    private const int MinAnswerChars = 10;

    /// <summary>开始一场练习：复用或计算匹配结果，再按配比生成 8 道带证据锚点的题。</summary>
    [HttpPost]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create([FromBody] CreateSessionRequest req, CancellationToken ct)
    {
        var resume = await db.Resumes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.ResumeId, ct);
        if (resume is null) return NotFound(new { error = "简历不存在" });

        var job = await db.JobTargets.AsNoTracking().FirstOrDefaultAsync(j => j.Id == req.JobTargetId, ct);
        if (job is null) return NotFound(new { error = "JD 不存在" });

        Session session;
        List<Question> questions;
        MatchResult matchResult;

        try
        {
            var entry = await matchStore.GetOrCreateAsync(resume, job, refresh: false, ct);
            matchResult = entry.Result;

            var (resumeProfile, resumeAnchors) = StoredParsed.ReadResume(resume.ParsedJson);
            var (jobProfile, jdAnchors) = StoredParsed.ReadJob(job.ParsedJson);
            var outcome = await generator.GenerateAsync(
                new QuestionGenInput(resumeProfile, resumeAnchors, jobProfile, jdAnchors, matchResult), ct);

            session = new Session
            {
                Id = Guid.NewGuid(),
                JobTargetId = job.Id,
                ResumeId = resume.Id,
                MatchId = entry.Entity.Id,
                Status = SessionStatus.active,
                StartedAt = DateTime.UtcNow
            };

            questions = outcome.Questions.Select((q, i) => new Question
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                Ordinal = i + 1,
                Type = ParseType(q.Type),
                Source = ParseSource(q.Source),
                Content = q.Content.Trim(),
                Intent = q.Intent,
                AnchorIds = JsonSerializer.Serialize(q.AnchorIds, LlmJson.Options),
                CreatedAt = DateTime.UtcNow
            }).ToList();

            db.Sessions.Add(session);
            db.Questions.AddRange(questions);
            await db.SaveChangesAsync(ct);

            log.LogInformation("会话创建 session={SessionId} 题数={Count}", session.Id, questions.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "创建练习失败 resume={ResumeId} job={JobId}", req.ResumeId, req.JobTargetId);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "出题失败，请稍后重试" });
        }

        return Ok(ToResponse(session, matchResult, questions));
    }

    /// <summary>会话详情（含题目与匹配结果）。</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var session = await db.Sessions.AsNoTracking()
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (session is null) return NotFound(new { error = "会话不存在" });

        var matchResult = new MatchResult();
        if (session.MatchId is { } matchId)
        {
            var match = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
            if (match is not null)
                matchResult = JsonSerializer.Deserialize<MatchResult>(match.ResultJson, LlmJson.Options) ?? new MatchResult();
        }

        return Ok(ToResponse(session, matchResult, session.Questions.OrderBy(q => q.Ordinal).ToList()));
    }

    /// <summary>提交一次作答：评估 + 策略决定「追问还是下一题」。</summary>
    [HttpPost("{id:guid}/questions/{qid:guid}/answer")]
    [ProducesResponseType(typeof(AnswerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SubmitAnswer(Guid id, Guid qid, [FromBody] SubmitAnswerRequest req, CancellationToken ct)
    {
        var answer = (req.Answer ?? string.Empty).Trim();
        if (answer.Length < MinAnswerChars)
            return BadRequest(new { error = $"回答太短，至少 {MinAnswerChars} 字" });

        var session = await db.Sessions.Include(s => s.Questions).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return NotFound(new { error = "会话不存在" });

        var question = session.Questions.FirstOrDefault(q => q.Id == qid);
        if (question is null) return NotFound(new { error = "题目不存在" });

        if (question.Answer is not null) return Conflict(new { error = "这道题已经作答过" });

        List<Anchor> anchors;
        try
        {
            anchors = await LoadAnchorsAsync(session, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "读取会话锚点失败 session={SessionId}", id);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "会话数据不完整，请重新开始" });
        }

        AssessOutcome outcome;
        try
        {
            outcome = await interview.EvaluateAsync(
                new InterviewContext(question.Content, question.Intent ?? "", question.Source.ToString(), 0, answer, anchors), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "评估失败 session={SessionId} question={QuestionId}", id, qid);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "评估失败，请稍后重试" });
        }

        question.Answer = answer;
        try
        {
            await ApplyAssessmentAsync(question, outcome.Assessment, ct);
            if (outcome.FollowUpText is not null) question.FollowUp = outcome.FollowUpText;
            MarkCompleteIfDone(session);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // 并发提交同一道题：唯一键冲突，按冲突处理而不是 500
            log.LogWarning(ex, "作答保存冲突 session={SessionId} question={QuestionId}", id, qid);
            return Conflict(new { error = "这道题已经被提交过" });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "作答保存失败 session={SessionId} question={QuestionId}", id, qid);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "保存失败，请稍后重试" });
        }

        return Ok(new AnswerResponse(
            outcome.Decision.Action,
            outcome.FollowUpText,
            outcome.FollowUpFallback,
            outcome.Assessment,
            outcome.FollowUpText is null ? NextQuestion(session, question) : null));
    }

    /// <summary>提交追问的回答，完成后推进下一题。</summary>
    [HttpPost("{id:guid}/questions/{qid:guid}/followup")]
    [ProducesResponseType(typeof(AnswerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SubmitFollowUp(Guid id, Guid qid, [FromBody] SubmitAnswerRequest req, CancellationToken ct)
    {
        var answer = (req.Answer ?? string.Empty).Trim();
        if (answer.Length < MinAnswerChars)
            return BadRequest(new { error = $"回答太短，至少 {MinAnswerChars} 字" });

        var session = await db.Sessions.Include(s => s.Questions).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return NotFound(new { error = "会话不存在" });

        var question = session.Questions.FirstOrDefault(q => q.Id == qid);
        if (question is null) return NotFound(new { error = "题目不存在" });

        if (string.IsNullOrWhiteSpace(question.FollowUp)) return Conflict(new { error = "这道题没有追问" });
        if (question.FollowUpAnswer is not null) return Conflict(new { error = "这道题的追问已经作答过" });

        List<Anchor> anchors;
        try
        {
            anchors = await LoadAnchorsAsync(session, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "读取会话锚点失败 session={SessionId}", id);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "会话数据不完整，请重新开始" });
        }

        AssessOutcome outcome;
        try
        {
            outcome = await interview.EvaluateAsync(
                new InterviewContext(question.Content, question.Intent ?? "", question.Source.ToString(), 1, answer, anchors, question.FollowUp), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "追问评估失败 session={SessionId} question={QuestionId}", id, qid);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "评估失败，请稍后重试" });
        }

        question.FollowUpAnswer = answer;
        try
        {
            await ApplyAssessmentAsync(question, outcome.Assessment, ct);
            MarkCompleteIfDone(session);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            log.LogWarning(ex, "追问保存冲突 session={SessionId} question={QuestionId}", id, qid);
            return Conflict(new { error = "这道题的追问已经被提交过" });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "追问保存失败 session={SessionId} question={QuestionId}", id, qid);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "保存失败，请稍后重试" });
        }

        return Ok(new AnswerResponse(outcome.Decision.Action, null, outcome.FollowUpFallback, outcome.Assessment, NextQuestion(session, question)));
    }

    /// <summary>读取已完成的面试报告（只读，不写库）。</summary>
    [HttpGet("{id:guid}/report")]
    [ProducesResponseType(typeof(SessionReport), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Report(Guid id, CancellationToken ct)
    {
        var session = await db.Sessions.Include(s => s.Questions).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return NotFound(new { error = "会话不存在" });

        if (session.Status != SessionStatus.completed)
            return Conflict(new { error = "练习还没完成，答完所有题后再看报告" });

        return Ok(await BuildReportAsync(session, ct));
    }

    /// <summary>收尾：生成报告并写回会话的总分与摘要（spec §10 的 complete 入口）。</summary>
    [HttpPost("{id:guid}/complete")]
    [ProducesResponseType(typeof(SessionReport), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        var session = await db.Sessions.Include(s => s.Questions).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return NotFound(new { error = "会话不存在" });

        if (session.Status != SessionStatus.completed)
            return Conflict(new { error = "还有题没作答，无法收尾" });

        var report = await BuildReportAsync(session, ct);

        session.OverallScore = (byte)Math.Clamp(report.OverallScore, 0, 100);
        session.Summary = report.Summary;
        await db.SaveChangesAsync(ct);

        return Ok(report);
    }

    private async Task<SessionReport> BuildReportAsync(Session session, CancellationToken ct)
    {
        var questionIds = session.Questions.Select(q => q.Id).ToList();
        var scores = await db.QuestionScores.AsNoTracking()
            .Where(s => questionIds.Contains(s.QuestionId))
            .ToListAsync(ct);

        var byQuestion = scores
            .GroupBy(s => s.QuestionId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(x => x.Dimension.ToString(), x => (int)x.Score));

        var inputs = session.Questions
            .OrderBy(q => q.Ordinal)
            .Select(q => new QuestionReport(
                q.Source.ToString(),
                ParseAssessment(q.AssessmentJson),
                byQuestion.GetValueOrDefault(q.Id) ?? [],
                DeserializeIds(q.AnchorIds),
                q.Content))
            .ToList();

        var match = new MatchResult();
        if (session.MatchId is { } matchId)
        {
            var entity = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
            if (entity is not null)
                match = JsonSerializer.Deserialize<MatchResult>(entity.ResultJson, LlmJson.Options) ?? new MatchResult();
        }

        return ReportBuilder.Build(session.Id, inputs, match);
    }

    /// <summary>所有题都答完就把会话标记为完成（spec §6 的 complete 分支）。</summary>
    private static void MarkCompleteIfDone(Session session)
    {
        if (session.Questions.Count == 0 || session.Questions.Any(q => q.Answer is null)) return;

        session.Status = SessionStatus.completed;
        session.CompletedAt = DateTime.UtcNow;
    }

    private async Task<List<Anchor>> LoadAnchorsAsync(Session session, CancellationToken ct)
    {
        var anchors = new List<Anchor>();

        if (session.ResumeId is { } resumeId)
        {
            var resume = await db.Resumes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == resumeId, ct);
            if (resume is not null) anchors.AddRange(StoredParsed.ReadResume(resume.ParsedJson).Anchors);
        }

        var job = await db.JobTargets.AsNoTracking().FirstOrDefaultAsync(j => j.Id == session.JobTargetId, ct);
        if (job is not null) anchors.AddRange(StoredParsed.ReadJob(job.ParsedJson).Anchors);

        return anchors;
    }

    private async Task ApplyAssessmentAsync(Question question, Assessment assessment, CancellationToken ct)
    {
        assessment.Normalize();

        question.AssessmentJson = JsonSerializer.Serialize(assessment, LlmJson.Options);
        question.ClaimChecks = JsonSerializer.Serialize(assessment.ClaimChecks, LlmJson.Options);
        question.Feedback = string.Join("；", assessment.Improvements);

        var existing = await db.QuestionScores.Where(s => s.QuestionId == question.Id).ToListAsync(ct);
        if (existing.Count > 0) db.QuestionScores.RemoveRange(existing);

        (ScoreDimension Dimension, int Value)[] scores =
        [
            (ScoreDimension.structure, assessment.Display.Structure),
            (ScoreDimension.depth, assessment.Display.Depth),
            (ScoreDimension.evidence, assessment.Display.Evidence),
            (ScoreDimension.delivery, assessment.Display.Delivery)
        ];

        foreach (var (dimension, value) in scores)
        {
            db.QuestionScores.Add(new QuestionScore
            {
                Id = Guid.NewGuid(),
                QuestionId = question.Id,
                Dimension = dimension,
                Score = (byte)Math.Clamp(value, 1, 5)
            });
        }
    }

    private static SessionQuestionDto? NextQuestion(Session session, Question current) =>
        session.Questions
            .Where(q => q.Ordinal > current.Ordinal)
            .OrderBy(q => q.Ordinal)
            .Select(ToDto)
            .FirstOrDefault();

    private static SessionResponse ToResponse(Session session, MatchResult match, List<Question> questions) =>
        new(session.Id,
            session.ResumeId ?? Guid.Empty,
            session.JobTargetId,
            session.MatchId ?? Guid.Empty,
            session.Status.ToString(),
            session.StartedAt,
            match,
            questions.Select(ToDto).ToList());

    private static SessionQuestionDto ToDto(Question q) =>
        new(q.Id, q.Ordinal, q.Type.ToString(), q.Source.ToString(), q.Content, q.Intent ?? "",
            DeserializeIds(q.AnchorIds));

    private static List<string> DeserializeIds(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json, LlmJson.Options) ?? [];

    private static Assessment? ParseAssessment(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        var assessment = JsonSerializer.Deserialize<Assessment>(json, LlmJson.Options);
        assessment?.Normalize();
        return assessment;
    }

    private static QuestionType ParseType(string value) =>
        Enum.TryParse<QuestionType>(value, ignoreCase: true, out var parsed) ? parsed : QuestionType.basic;

    private static QuestionSource ParseSource(string value) =>
        Enum.TryParse<QuestionSource>(value, ignoreCase: true, out var parsed) ? parsed : QuestionSource.general;
}
