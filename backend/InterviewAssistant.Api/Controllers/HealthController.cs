using InterviewAssistant.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

[ApiController]
[Route("health")]
[Produces("application/json")]
public class HealthController(AppDbContext db) : ControllerBase
{
    /// <summary>健康检查：确认进程存活且数据库可连接。</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try
        {
            var ok = await db.Database.CanConnectAsync(ct);
            return ok
                ? Ok(new { status = "ok", db = "up" })
                : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "degraded", db = "down" });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "error", error = ex.Message });
        }
    }
}
