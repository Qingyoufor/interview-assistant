using System.Text.Json;
using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

public sealed record CreateResumeRequest(string? Title, string? RawText);

[ApiController]
[Route("api/resumes")]
[Produces("application/json")]
public class ResumesController(AppDbContext db, ResumeParser parser) : ControllerBase
{
    private const int MaxChars = 12000;

    /// <summary>录入并解析一份简历，返回候选人画像与证据锚点。</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ResumeParseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create([FromBody] CreateResumeRequest req, CancellationToken ct)
    {
        var text = (req.RawText ?? string.Empty).Trim();
        if (text.Length < 50)
            return BadRequest(new { error = "简历内容太短，至少 50 字" });

        var truncated = text.Length > MaxChars;
        if (truncated) text = text[..MaxChars];

        ResumeParseResult result;
        try
        {
            result = await parser.ParseAsync(text, ct);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }

        var entity = new Resume
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(req.Title) ? $"简历 {DateTime.Now:MM-dd HH:mm}" : req.Title.Trim(),
            RawText = text,
            ParsedJson = JsonSerializer.Serialize(new { profile = result.Profile, anchors = result.Anchors }, ResumeParser.Json),
            CreatedAt = DateTime.UtcNow
        };

        db.Resumes.Add(entity);
        await db.SaveChangesAsync(ct);

        return Ok(new ResumeParseResponse(entity.Id, entity.Title, truncated, result.Profile, result.Anchors));
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

public sealed record ResumeParseResponse(Guid Id, string Title, bool Truncated, ResumeProfile Profile, List<Anchor> Anchors);

public sealed record ResumeListItem(Guid Id, string Title, DateTime CreatedAt);
