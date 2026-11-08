using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Services.Stats;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Controllers;

[ApiController]
[Route("api/stats")]
[Produces("application/json")]
public class StatsController(AppDbContext db) : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, int> NoScores = new Dictionary<string, int>();

    /// <summary>
    /// 弱点标签跨场次聚合（spec §8 / §17 S7）。
    /// 只按「已评估的题目」算，所以没答完的场次也会计入；不存在场次维度的过滤。
    /// </summary>
    [HttpGet("weakness")]
    [ProducesResponseType(typeof(WeaknessStats), StatusCodes.Status200OK)]
    public async Task<ActionResult<WeaknessStats>> Weakness(CancellationToken ct)
    {
        var questions = await db.Questions.AsNoTracking()
            .Where(q => q.AssessmentJson != null)
            .Select(q => new { q.Id, q.SessionId, q.AssessmentJson })
            .ToListAsync(ct);

        if (questions.Count == 0) return Ok(WeaknessStatsBuilder.Build([]));

        var questionIds = questions.Select(q => q.Id).ToList();
        var scores = await db.QuestionScores.AsNoTracking()
            .Where(s => questionIds.Contains(s.QuestionId))
            .Select(s => new { s.QuestionId, s.Dimension, s.Score })
            .ToListAsync(ct);

        var byQuestion = scores
            .GroupBy(s => s.QuestionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, int>)g.ToDictionary(
                    x => x.Dimension.ToString(),
                    x => (int)x.Score));

        var inputs = questions
            .Select(q => (q.Id, q.SessionId, Assessment: SessionMapper.ParseAssessment(q.AssessmentJson)))
            .Where(x => x.Assessment is not null)
            .Select(x => new StatsQuestion(
                x.SessionId,
                x.Assessment!.Weaknesses,
                byQuestion.GetValueOrDefault(x.Id) ?? NoScores))
            .ToList();

        return Ok(WeaknessStatsBuilder.Build(inputs));
    }
}
