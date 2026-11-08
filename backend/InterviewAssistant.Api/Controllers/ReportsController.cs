using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Match;
using InterviewAssistant.Api.Services.Report;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

[ApiController]
[Route("api/sessions/{id:guid}")]
[Produces("application/json")]
public class ReportsController(AppDbContext db) : ControllerBase
{
    /// <summary>读取已完成的面试报告（只读，不写库）。</summary>
    [HttpGet("report")]
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
    [HttpPost("complete")]
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
                SessionMapper.ParseAssessment(q.AssessmentJson),
                byQuestion.GetValueOrDefault(q.Id) ?? [],
                SessionMapper.DeserializeIds(q.AnchorIds),
                q.Content))
            .ToList();

        var match = new MatchResult();
        if (session.MatchId is { } matchId)
        {
            var entity = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
            if (entity is not null) match = MatchResults.Read(entity.ResultJson);
        }

        return ReportBuilder.Build(session.Id, inputs, match);
    }
}
