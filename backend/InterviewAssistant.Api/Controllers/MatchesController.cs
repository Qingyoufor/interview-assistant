using System.Text.Json;
using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services;
using InterviewAssistant.Api.Services.Llm;
using InterviewAssistant.Api.Services.Match;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

public sealed record CreateMatchRequest(Guid ResumeId, Guid JobTargetId, bool Refresh = false);

public sealed record MatchResponse(Guid Id, Guid ResumeId, Guid JobTargetId, bool Cached, MatchResult Result, DateTime CreatedAt);

[ApiController]
[Route("api/matches")]
[Produces("application/json")]
public class MatchesController(AppDbContext db, MatchService service, ILogger<MatchesController> log) : ControllerBase
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

        var inputHash = Fingerprint(resume.ParsedJson, job.ParsedJson);

        if (!req.Refresh)
        {
            var cached = await db.Matches.AsNoTracking()
                .Where(m => m.ResumeId == req.ResumeId && m.JobTargetId == req.JobTargetId)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync(ct);

            // 输入变了（简历/JD 重新解析过）就作废旧缓存，避免静默返回过期结论
            if (cached is not null && cached.InputHash == inputHash)
                return Ok(ToResponse(cached, cached: true));
        }

        MatchOutcome outcome;
        try
        {
            var (resumeProfile, resumeAnchors) = StoredParsed.ReadResume(resume.ParsedJson);
            var (jobProfile, jdAnchors) = StoredParsed.ReadJob(job.ParsedJson);
            outcome = await service.MatchAsync(new MatchInput(resumeProfile, resumeAnchors, jobProfile, jdAnchors), ct);
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

        var entity = new Match
        {
            Id = Guid.NewGuid(),
            ResumeId = req.ResumeId,
            JobTargetId = req.JobTargetId,
            InputHash = inputHash,
            OverallMatch = (byte)Math.Clamp(outcome.Result.OverallMatch, 0, 100),
            HardBlock = outcome.Result.HardBlock,
            ResultJson = JsonSerializer.Serialize(outcome.Result, LlmJson.Options),
            CreatedAt = DateTime.UtcNow
        };

        db.Matches.Add(entity);
        await db.SaveChangesAsync(ct);

        log.LogInformation("匹配完成 overall={Overall} hardBlock={HardBlock} repairs={Repairs}",
            entity.OverallMatch, entity.HardBlock, outcome.Repairs);

        return Ok(ToResponse(entity, cached: false));
    }

    /// <summary>按 id 读取一次历史匹配结果。</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var entity = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        return entity is null ? NotFound(new { error = "匹配结果不存在" }) : Ok(ToResponse(entity, cached: true));
    }

    private static MatchResponse ToResponse(Match entity, bool cached)
    {
        var result = JsonSerializer.Deserialize<MatchResult>(entity.ResultJson, LlmJson.Options) ?? new MatchResult();
        return new MatchResponse(entity.Id, entity.ResumeId, entity.JobTargetId, cached, result, entity.CreatedAt);
    }

    /// <summary>输入指纹：简历与 JD 的解析结果都参与，任一方变化都会导致重算。</summary>
    private static string Fingerprint(string? resumeJson, string? jobJson)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{resumeJson ?? ""}|{jobJson ?? ""}"));
        return Convert.ToHexStringLower(bytes);
    }
}
