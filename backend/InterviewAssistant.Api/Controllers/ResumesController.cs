using System.Text.Json;
using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services;
using InterviewAssistant.Api.Services.Llm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

public sealed record CreateResumeRequest(string? Title, string? RawText);

public sealed record ResumeParseResponse(Guid Id, string Title, bool Truncated, ResumeProfile Profile, List<Anchor> Anchors);

public sealed record ResumeListItem(Guid Id, string Title, DateTime CreatedAt);

[ApiController]
[Route("api/resumes")]
[Produces("application/json")]
public class ResumesController(AppDbContext db, ResumeParser parser, ILogger<ResumesController> log) : ControllerBase
{
    private const int MinChars = 50;
    private const int MaxChars = 12000;
    private const int TitleMaxLength = 120;

    /// <summary>录入并解析一份简历，返回候选人画像与证据锚点。</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ResumeParseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create([FromBody] CreateResumeRequest req, CancellationToken ct)
    {
        var guard = InputGuard.Prepare(req.RawText, MinChars, MaxChars, "简历内容");
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
            Title = InputGuard.Clamp(rawTitle, TitleMaxLength),
            RawText = text,
            ParsedJson = JsonSerializer.Serialize(new { profile = result.Profile, anchors = result.Anchors }, LlmJson.Options),
            CreatedAt = DateTime.UtcNow
        };

        db.Resumes.Add(entity);
        await db.SaveChangesAsync(ct);

        return Ok(new ResumeParseResponse(entity.Id, entity.Title, guard.Truncated, result.Profile, result.Anchors));
    }

    /// <summary>简历列表（按创建时间倒序）。</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ResumeListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await db.Resumes.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ResumeListItem(r.Id, r.Title, r.CreatedAt))
            .ToListAsync(ct);

        return Ok(items);
    }
}
