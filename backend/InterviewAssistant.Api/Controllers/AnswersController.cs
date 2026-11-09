using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services;
using InterviewAssistant.Api.Services.Interview;
using InterviewAssistant.Api.Services.Report;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

[ApiController]
[Route("api/sessions/{sessionId:guid}/questions/{questionId:guid}")]
[Produces("application/json")]
public class AnswersController(
    AppDbContext db,
    InterviewService interview,
    SessionReportService reporter,
    ILogger<AnswersController> log) : ControllerBase
{
    private const int MinAnswerChars = 10;

    /// <summary>提交一次作答：评估 + 策略决定「追问还是下一题」。</summary>
    [HttpPost("answer")]
    [ProducesResponseType(typeof(AnswerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SubmitAnswer(
        Guid sessionId, Guid questionId, [FromBody] SubmitAnswerRequest req, CancellationToken ct)
    {
        var answer = (req.Answer ?? string.Empty).Trim();
        if (answer.Length < MinAnswerChars)
            return BadRequest(new { error = $"回答太短，至少 {MinAnswerChars} 字" });

        var session = await db.Sessions.Include(s => s.Questions).FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return NotFound(new { error = "会话不存在" });

        var question = session.Questions.FirstOrDefault(q => q.Id == questionId);
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
            log.LogError(ex, "读取会话锚点失败 session={SessionId}", sessionId);
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
            log.LogError(ex, "评估失败 session={SessionId} question={QuestionId}", sessionId, questionId);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "评估失败，请稍后重试" });
        }

        question.Answer = answer;
        if (outcome.FollowUpText is not null) question.FollowUp = outcome.FollowUpText;

        var saveError = await SaveAsync(session, question, outcome, "作答", sessionId, questionId, ct);
        if (saveError is not null) return saveError;

        return Ok(new AnswerResponse(
            outcome.Decision.Action,
            outcome.FollowUpText,
            outcome.FollowUpFallback,
            outcome.Assessment,
            outcome.FollowUpText is null ? SessionMapper.NextQuestion(session, question) : null));
    }

    /// <summary>提交追问的回答，完成后推进下一题。</summary>
    [HttpPost("followup")]
    [ProducesResponseType(typeof(AnswerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SubmitFollowUp(
        Guid sessionId, Guid questionId, [FromBody] SubmitAnswerRequest req, CancellationToken ct)
    {
        var answer = (req.Answer ?? string.Empty).Trim();
        if (answer.Length < MinAnswerChars)
            return BadRequest(new { error = $"回答太短，至少 {MinAnswerChars} 字" });

        var session = await db.Sessions.Include(s => s.Questions).FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return NotFound(new { error = "会话不存在" });

        var question = session.Questions.FirstOrDefault(q => q.Id == questionId);
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
            log.LogError(ex, "读取会话锚点失败 session={SessionId}", sessionId);
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
            log.LogError(ex, "追问评估失败 session={SessionId} question={QuestionId}", sessionId, questionId);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "评估失败，请稍后重试" });
        }

        question.FollowUpAnswer = answer;

        var saveError = await SaveAsync(session, question, outcome, "追问", sessionId, questionId, ct);
        if (saveError is not null) return saveError;

        return Ok(new AnswerResponse(
            outcome.Decision.Action, null, outcome.FollowUpFallback, outcome.Assessment, SessionMapper.NextQuestion(session, question)));
    }

    /// <summary>写评估、写分数、标记完成，一次保存；返回 null 表示成功。</summary>
    private async Task<IActionResult?> SaveAsync(
        Session session, Question question, AssessOutcome outcome, string stage, Guid sessionId, Guid questionId, CancellationToken ct)
    {
        try
        {
            await ApplyAssessmentAsync(question, outcome.Assessment, ct);
            SessionMapper.MarkCompleteIfDone(session);
            await db.SaveChangesAsync(ct);

            // 答完最后一题就顺手把总分写回，历史记录里「已完成」的场次才有分
            if (session.Status == SessionStatus.completed && session.OverallScore is null)
            {
                try
                {
                    await reporter.CompleteAsync(session, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "自动收尾失败 session={SessionId}", sessionId);
                }
            }

            return null;
        }
        catch (DbUpdateException ex)
        {
            log.LogWarning(ex, "{Stage}保存冲突 session={SessionId} question={QuestionId}", stage, sessionId, questionId);
            return Conflict(new { error = $"这道题的{stage}已经被提交过" });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "{Stage}保存失败 session={SessionId} question={QuestionId}", stage, sessionId, questionId);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "保存失败，请稍后重试" });
        }
    }

    private async Task ApplyAssessmentAsync(Question question, Assessment assessment, CancellationToken ct)
    {
        assessment.Normalize();
        question.AssessmentJson = System.Text.Json.JsonSerializer.Serialize(assessment, Services.Llm.LlmJson.Options);
        question.ClaimChecks = System.Text.Json.JsonSerializer.Serialize(assessment.ClaimChecks, Services.Llm.LlmJson.Options);
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
}
