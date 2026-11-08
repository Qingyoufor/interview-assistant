using System.Text.Json;
using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services;
using InterviewAssistant.Api.Services.Llm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

public sealed record CreateJobTargetRequest(string? Title, string? JdText);

public sealed record JobTargetResponse(Guid Id, string Title, string Level, bool Truncated, JobProfile Profile, List<Anchor> Anchors);

public sealed record JobTargetListItem(Guid Id, string Title, string? Level, DateTime CreatedAt);

[ApiController]
[Route("api/job-targets")]
[Produces("application/json")]
public class JobTargetsController(AppDbContext db, JdParser parser, ILogger<JobTargetsController> log) : ControllerBase
{
    private const int MinChars = 50;
    private const int MaxChars = 12000;
    private const int TitleMaxLength = 120;
    private const int LevelMaxLength = 32;

    /// <summary>录入并解析一份岗位 JD，返回结构化要求与证据锚点。</summary>
    [HttpPost]
    [ProducesResponseType(typeof(JobTargetResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create([FromBody] CreateJobTargetRequest req, CancellationToken ct)
    {
        var guard = InputGuard.Prepare(req.JdText, MinChars, MaxChars, "JD 内容");
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
            Title = InputGuard.Clamp(rawTitle, TitleMaxLength),
            Level = string.IsNullOrWhiteSpace(result.Profile.Level) ? null : InputGuard.Clamp(result.Profile.Level, LevelMaxLength),
            JdText = text,
            ParsedJson = JsonSerializer.Serialize(new { job = result.Profile, anchors = result.Anchors }, LlmJson.Options),
            CreatedAt = DateTime.UtcNow
        };

        db.JobTargets.Add(entity);
        await db.SaveChangesAsync(ct);

        return Ok(new JobTargetResponse(entity.Id, entity.Title, entity.Level ?? "", guard.Truncated, result.Profile, result.Anchors));
    }

    /// <summary>JD 列表（按创建时间倒序）。</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<JobTargetListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await db.JobTargets.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new JobTargetListItem(j.Id, j.Title, j.Level, j.CreatedAt))
            .ToListAsync(ct);

        return Ok(items);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
