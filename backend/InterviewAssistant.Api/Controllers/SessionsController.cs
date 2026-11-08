using System.Text.Json;
using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services;
using InterviewAssistant.Api.Services.Llm;
using InterviewAssistant.Api.Services.Match;
using InterviewAssistant.Api.Services.Questions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

[ApiController]
[Route("api/sessions")]
[Produces("application/json")]
public class SessionsController(
    AppDbContext db,
    MatchStore matchStore,
    QuestionGenerator generator,
    ILogger<SessionsController> log) : ControllerBase
{
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
                Type = SessionMapper.ParseType(q.Type),
                Source = SessionMapper.ParseSource(q.Source),
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

        return Ok(SessionMapper.ToResponse(session, matchResult, questions));
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

        var match = new MatchResult();
        if (session.MatchId is { } matchId)
        {
            var entity = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
            if (entity is not null) match = MatchResults.Read(entity.ResultJson);
        }

        return Ok(SessionMapper.ToResponse(session, match, session.Questions.OrderBy(q => q.Ordinal).ToList()));
    }
}
