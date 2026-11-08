using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Services.Match;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

public sealed record CreateMatchRequest(Guid ResumeId, Guid JobTargetId, bool Refresh = false);

public sealed record MatchResponse(Guid Id, Guid ResumeId, Guid JobTargetId, bool Cached, MatchResult Result, DateTime CreatedAt);

[ApiController]
[Route("api/matches")]
[Produces("application/json")]
public class MatchesController(AppDbContext db, MatchStore store, ILogger<MatchesController> log) : ControllerBase
{
    /// <summary>简历 × JD 加权匹配。同一组合默认复用最近一次结果，传 refresh=true 强制重算。</summary>
    [HttpPost]
    [ProducesResponseType(typeof(MatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create([FromBody] CreateMatchRequest req, CancellationToken ct)
    {
        var resume = await db.Resumes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.ResumeId, ct);
        if (resume is null) return NotFound(new { error = "简历不存在" });

        var job = await db.JobTargets.AsNoTracking().FirstOrDefaultAsync(j => j.Id == req.JobTargetId, ct);
        if (job is null) return NotFound(new { error = "JD 不存在" });

        try
        {
            var entry = await store.GetOrCreateAsync(resume, job, req.Refresh, ct);
            return Ok(ToResponse(entry));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "匹配失败 resume={ResumeId} job={JobId}", req.ResumeId, req.JobTargetId);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "匹配失败，请稍后重试" });
        }
    }

    /// <summary>按 id 读取一次历史匹配结果。</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var entity = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (entity is null) return NotFound(new { error = "匹配结果不存在" });

        var result = MatchResults.Read(entity.ResultJson);
        return Ok(new MatchResponse(entity.Id, entity.ResumeId, entity.JobTargetId, true, result, entity.CreatedAt));
    }

    private static MatchResponse ToResponse(MatchEntry entry) =>
        new(entry.Entity.Id, entry.Entity.ResumeId, entry.Entity.JobTargetId, entry.Cached, entry.Result, entry.Entity.CreatedAt);
}
