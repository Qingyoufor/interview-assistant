using System.Text.Json;
using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services.Llm;
using MatchRow = InterviewAssistant.Api.Models.Match;
using Microsoft.EntityFrameworkCore;

namespace InterviewAssistant.Api.Services.Match;

public sealed record MatchEntry(MatchRow Entity, MatchResult Result, bool Cached);

/// <summary>匹配结果的读取与落库。缓存按输入指纹失效，输入变了就重算。</summary>
public sealed class MatchStore(AppDbContext db, MatchService service, ILogger<MatchStore> log)
{
    public async Task<MatchEntry> GetOrCreateAsync(Resume resume, JobTarget job, bool refresh, CancellationToken ct)
    {
        var hash = InputFingerprint.Of(resume.ParsedJson, job.ParsedJson);

        if (!refresh)
        {
            var cached = await db.Matches.AsNoTracking()
                .Where(m => m.ResumeId == resume.Id && m.JobTargetId == job.Id)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (cached is not null && cached.InputHash == hash)
                return new MatchEntry(cached, MatchResults.Read(cached.ResultJson), Cached: true);
        }

        var (resumeProfile, resumeAnchors) = StoredParsed.ReadResume(resume.ParsedJson);
        var (jobProfile, jdAnchors) = StoredParsed.ReadJob(job.ParsedJson);
        var outcome = await service.MatchAsync(new MatchInput(resumeProfile, resumeAnchors, jobProfile, jdAnchors), ct);

        var entity = new MatchRow
        {
            Id = Guid.NewGuid(),
            ResumeId = resume.Id,
            JobTargetId = job.Id,
            InputHash = hash,
            OverallMatch = (byte)Math.Clamp(outcome.Result.OverallMatch, 0, 100),
            HardBlock = outcome.Result.HardBlock,
            ResultJson = JsonSerializer.Serialize(outcome.Result, LlmJson.Options),
            CreatedAt = DateTime.UtcNow
        };

        db.Matches.Add(entity);
        await db.SaveChangesAsync(ct);

        log.LogInformation("匹配完成 overall={Overall} hardBlock={HardBlock} repairs={Repairs}",
            entity.OverallMatch, entity.HardBlock, outcome.Repairs);

        return new MatchEntry(entity, outcome.Result, Cached: false);
    }
}
