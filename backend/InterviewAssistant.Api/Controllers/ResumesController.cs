using System.Text.Json;
using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services;
using InterviewAssistant.Api.Services.Llm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

public sealed record CreateResumeRequest(string? Title, string? RawText);

public sealed record ResumeParseResponse(
    Guid Id, string Title, bool Truncated, string RawText, DateTime CreatedAt, ResumeProfile Profile, List<Anchor> Anchors);

public sealed record ResumeListItem(Guid Id, string Title, DateTime CreatedAt);

public sealed record ResumeDetailResponse(
    Guid Id, string Title, string RawText, DateTime CreatedAt, ResumeProfile Profile, List<Anchor> Anchors);

[ApiController]
[Route("api/resumes")]
[Produces("application/json")]
public class ResumesController(AppDbContext db, ResumeParser parser, ILogger<ResumesController> log) : ControllerBase
{
    /// <summary>录入并解析一份简历，返回候选人画像与证据锚点。</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ResumeParseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create([FromBody] CreateResumeRequest req, CancellationToken ct)
    {
        var guard = InputGuard.Prepare(req.RawText, InputGuard.MinChars, InputGuard.MaxChars, "简历内容");
        if (!guard.Ok)
            return BadRequest(new { error = guard.Error });

        var text = guard.Text!;

        ResumeParseResult result;
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
            log.LogError(ex, "简历解析失败");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "解析失败，请稍后重试" });
        }

        var rawTitle = string.IsNullOrWhiteSpace(req.Title)
            ? $"简历 {DateTime.UtcNow:MM-dd HH:mm}"
            : req.Title.Trim();

        var entity = new Resume
        {
            Id = Guid.NewGuid(),
            Title = InputGuard.Clamp(rawTitle, InputGuard.TitleMaxLength),
            RawText = text,
            ParsedJson = JsonSerializer.Serialize(new { profile = result.Profile, anchors = result.Anchors }, LlmJson.Options),
            CreatedAt = DateTime.UtcNow
        };

        db.Resumes.Add(entity);
        await db.SaveChangesAsync(ct);

        return Ok(new ResumeParseResponse(
            entity.Id, entity.Title, guard.Truncated, entity.RawText, AsUtc(entity.CreatedAt), result.Profile, result.Anchors));
    }

    /// <summary>简历详情：原文 + 解析结果 + 锚点（资料页用于查看与再次编辑）。</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResumeDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var entity = await db.Resumes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null) return NotFound(new { error = "简历不存在" });

        try
        {
            var (profile, anchors) = StoredParsed.ReadResume(entity.ParsedJson);
            return Ok(new ResumeDetailResponse(entity.Id, entity.Title, entity.RawText, AsUtc(entity.CreatedAt), profile, anchors));
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            log.LogWarning(ex, "简历解析结果不可读 resume={ResumeId}", id);
            return Conflict(new { error = "这份简历的解析结果不可用，请重新录入" });
        }
    }

    /// <summary>简历列表（按创建时间倒序）。</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ResumeListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await db.Resumes.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.Id, r.Title, r.CreatedAt })
            .ToListAsync(ct);

        return Ok(rows.Select(r => new ResumeListItem(r.Id, r.Title, AsUtc(r.CreatedAt))).ToList());
    }

    /// <summary>删除简历。被面试/匹配引用时默认拒绝，force=true 会连带删除相关记录。</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool force, CancellationToken ct)
    {
        var resume = await db.Resumes.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (resume is null) return NotFound(new { error = "简历不存在" });

        var sessionCount = await db.Sessions.CountAsync(s => s.ResumeId == id, ct);
        var matchCount = await db.Matches.CountAsync(m => m.ResumeId == id, ct);

        // matches 是可重算的缓存，顺手删掉就行；只有面试记录需要用户确认
        if (!force && sessionCount > 0)
        {
            return Conflict(new
            {
                error = $"这份简历被 {sessionCount} 场面试引用，删除会连带删掉这些记录。",
                sessions = sessionCount
            });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE qs FROM question_scores qs JOIN questions q ON q.id = qs.question_id JOIN sessions s ON s.id = q.session_id WHERE s.resume_id = {id}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE q FROM questions q JOIN sessions s ON s.id = q.session_id WHERE s.resume_id = {id}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM sessions WHERE resume_id = {id}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM matches WHERE resume_id = {id}", ct);
        await db.Resumes.Where(r => r.Id == id).ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);
        log.LogInformation("删除简历 resume={ResumeId}，连带会话 {Sessions} 条、匹配 {Matches} 条", id, sessionCount, matchCount);

        return NoContent();
    }

    /// <summary>MySQL 读回来的时间没有 Kind，序列化不带 Z；统一标成 UTC，前端按本地时区显示。</summary>
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
