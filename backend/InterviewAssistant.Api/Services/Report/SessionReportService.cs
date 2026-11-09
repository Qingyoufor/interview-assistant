using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Interview;
using InterviewAssistant.Api.Services.Match;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Services.Report;

/// <summary>把一场会话聚合成报告；完成时顺带写回总分与摘要。</summary>
public sealed class SessionReportService(AppDbContext db)
{
    public async Task<SessionReport> BuildAsync(Session session, CancellationToken ct)
    {
        var questionIds = session.Questions.Select(q => q.Id).ToList();
        var scores = await db.QuestionScores.AsNoTracking()
            .Where(s => questionIds.Contains(s.QuestionId))
            .ToListAsync(ct);

        var byQuestion = scores
            .GroupBy(s => s.QuestionId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(x => x.Dimension.ToString(), x => (int)x.Score));

        var inputs = session.Questions
            .OrderBy(q => q.Ordinal)
            .Select(q => new QuestionReport(
                q.Source.ToString(),
                AssessmentJson.Read(q.AssessmentJson),
                byQuestion.GetValueOrDefault(q.Id) ?? [],
                AssessmentJson.ReadIds(q.AnchorIds),
                q.Content))
            .ToList();

        var match = new MatchResult();
        if (session.MatchId is { } matchId)
        {
            var entity = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct);
            if (entity is not null) match = MatchResults.Read(entity.ResultJson);
        }

        return ReportBuilder.Build(session.Id, inputs, match);
    }

    /// <summary>算一次报告并写回会话总分与摘要；会话必须已答完。</summary>
    public async Task<SessionReport> CompleteAsync(Session session, CancellationToken ct)
    {
        var report = await BuildAsync(session, ct);

        session.OverallScore = (byte)Math.Clamp(report.OverallScore, 0, 100);
        session.Summary = report.Summary;
        await db.SaveChangesAsync(ct);

        return report;
    }
}
