using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Report;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

[ApiController]
[Route("api/sessions/{id:guid}")]
[Produces("application/json")]
public class ReportsController(AppDbContext db, SessionReportService reporter) : ControllerBase
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

        return Ok(await reporter.BuildAsync(session, ct));
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

        return Ok(await reporter.CompleteAsync(session, ct));
    }
}
