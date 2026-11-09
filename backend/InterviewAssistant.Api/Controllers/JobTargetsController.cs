using System.Text.Json;
using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services;
using InterviewAssistant.Api.Services.Llm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

public sealed record CreateJobTargetRequest(string? Title, string? JdText);

public sealed record JobTargetResponse(
    Guid Id, string Title, string Level, bool Truncated, string RawText, DateTime CreatedAt, JobProfile Profile, List<Anchor> Anchors);

public sealed record JobTargetListItem(Guid Id, string Title, string? Level, DateTime CreatedAt);

public sealed record JobTargetDetailResponse(
    Guid Id, string Title, string Level, string RawText, DateTime CreatedAt, JobProfile Profile, List<Anchor> Anchors);

[ApiController]
[Route("api/job-targets")]
[Produces("application/json")]
public class JobTargetsController(AppDbContext db, JdParser parser, ILogger<JobTargetsController> log) : ControllerBase
{
    /// <summary>录入并解析一份岗位 JD，返回结构化要求与证据锚点。</summary>
    [HttpPost]
    [ProducesResponseType(typeof(JobTargetResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create([FromBody] CreateJobTargetRequest req, CancellationToken ct)
    {
        var guard = InputGuard.Prepare(req.JdText, InputGuard.MinChars, InputGuard.MaxChars, "JD 内容");
        if (!guard.Ok)
            return BadRequest(new { error = guard.Error });

        var text = guard.Text!;

        JobParseResult result;
        try
        {
            result = await parser.ParseAsync(text, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "JD 解析失败");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "解析失败，请稍后重试" });
        }

        var rawTitle = FirstNonBlank(req.Title, result.Profile.Title) ?? $"JD {DateTime.UtcNow:MM-dd HH:mm}";

        var entity = new JobTarget
        {
            Id = Guid.NewGuid(),
            Title = InputGuard.Clamp(rawTitle, InputGuard.TitleMaxLength),
            Level = string.IsNullOrWhiteSpace(result.Profile.Level) ? null : InputGuard.Clamp(result.Profile.Level, InputGuard.LevelMaxLength),
            JdText = text,
            ParsedJson = JsonSerializer.Serialize(new { job = result.Profile, anchors = result.Anchors }, LlmJson.Options),
            CreatedAt = DateTime.UtcNow
        };

        db.JobTargets.Add(entity);
        await db.SaveChangesAsync(ct);

        return Ok(new JobTargetResponse(
            entity.Id, entity.Title, entity.Level ?? "", guard.Truncated, entity.JdText, AsUtc(entity.CreatedAt), result.Profile, result.Anchors));
    }

    /// <summary>JD 详情：原文 + 解析结果 + 锚点（资料页用于查看与再次编辑）。</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(JobTargetDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var entity = await db.JobTargets.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (entity is null) return NotFound(new { error = "JD 不存在" });

        try
        {
            var (profile, anchors) = StoredParsed.ReadJob(entity.ParsedJson);
            return Ok(new JobTargetDetailResponse(
                entity.Id, entity.Title, entity.Level ?? "", entity.JdText, AsUtc(entity.CreatedAt), profile, anchors));
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            log.LogWarning(ex, "JD 解析结果不可读 job={JobId}", id);
            return Conflict(new { error = "这份 JD 的解析结果不可用，请重新录入" });
        }
    }

    /// <summary>JD 列表（按创建时间倒序）。</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<JobTargetListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await db.JobTargets.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new { j.Id, j.Title, j.Level, j.CreatedAt })
            .ToListAsync(ct);

        return Ok(rows.Select(j => new JobTargetListItem(j.Id, j.Title, j.Level, AsUtc(j.CreatedAt))).ToList());
    }

    /// <summary>删除 JD。被面试/匹配引用时默认拒绝，force=true 会连带删除相关记录。</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool force, CancellationToken ct)
    {
        var job = await db.JobTargets.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return NotFound(new { error = "JD 不存在" });

        var sessionCount = await db.Sessions.CountAsync(s => s.JobTargetId == id, ct);
        var matchCount = await db.Matches.CountAsync(m => m.JobTargetId == id, ct);

        // matches 是可重算的缓存，顺手删掉就行；只有面试记录需要用户确认
        if (!force && sessionCount > 0)
        {
            return Conflict(new
            {
                error = $"这份 JD 被 {sessionCount} 场面试引用，删除会连带删掉这些记录。",
                sessions = sessionCount
            });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE qs FROM question_scores qs JOIN questions q ON q.id = qs.question_id JOIN sessions s ON s.id = q.session_id WHERE s.job_target_id = {id}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE q FROM questions q JOIN sessions s ON s.id = q.session_id WHERE s.job_target_id = {id}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM sessions WHERE job_target_id = {id}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM matches WHERE job_target_id = {id}", ct);
        await db.JobTargets.Where(j => j.Id == id).ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);
        log.LogInformation("删除 JD job={JobId}，连带会话 {Sessions} 条、匹配 {Matches} 条", id, sessionCount, matchCount);

        return NoContent();
    }

    /// <summary>MySQL 读回来的时间没有 Kind，序列化不带 Z；统一标成 UTC，前端按本地时区显示。</summary>
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
