using Microsoft.EntityFrameworkCore;
using InterviewAssistant.Api.Models;

namespace InterviewAssistant.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<JobTarget> JobTargets => Set<JobTarget>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionScore> QuestionScores => Set<QuestionScore>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Resume>(e =>
        {
            e.ToTable("resumes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
            e.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(64);
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(120);
            e.Property(x => x.RawText).HasColumnName("raw_text").HasColumnType("mediumtext");
            e.Property(x => x.ParsedJson).HasColumnName("parsed_json").HasColumnType("json");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(3)");
        });

        b.Entity<JobTarget>(e =>
        {
            e.ToTable("job_targets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasMaxLength(36).IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(64);
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(120);
            e.Property(x => x.Level).HasColumnName("level").HasMaxLength(32);
            e.Property(x => x.JdText).HasColumnName("jd_text").HasColumnType("mediumtext");
            e.Property(x => x.ParsedJson).HasColumnName("parsed_json").HasColumnType("json");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(3)");
        });

        b.Entity<Match>(e =>
        {
            e.ToTable("matches");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
            e.Property(x => x.ResumeId).HasColumnName("resume_id").HasMaxLength(36);
            e.Property(x => x.JobTargetId).HasColumnName("job_target_id").HasMaxLength(36);
            e.Property(x => x.InputHash).HasColumnName("input_hash").HasMaxLength(64);
            e.Property(x => x.OverallMatch).HasColumnName("overall_match");
            e.Property(x => x.HardBlock).HasColumnName("hard_block");
            e.Property(x => x.ResultJson).HasColumnName("result_json").HasColumnType("json");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(3)");
            e.HasOne(x => x.Resume).WithMany().HasForeignKey(x => x.ResumeId);
            e.HasOne(x => x.JobTarget).WithMany().HasForeignKey(x => x.JobTargetId);
            e.HasIndex(x => new { x.ResumeId, x.JobTargetId, x.CreatedAt });
        });

        b.Entity<Session>(e =>
        {
            e.ToTable("sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
            e.Property(x => x.JobTargetId).HasColumnName("job_target_id").HasMaxLength(36);
            e.Property(x => x.ResumeId).HasColumnName("resume_id").HasMaxLength(36);
            e.Property(x => x.MatchId).HasColumnName("match_id").HasMaxLength(36);
            e.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(64);
            e.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.OverallScore).HasColumnName("overall_score");
            e.Property(x => x.Summary).HasColumnName("summary").HasColumnType("text");
            e.Property(x => x.StartedAt).HasColumnName("started_at").HasColumnType("datetime(3)");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at").HasColumnType("datetime(3)");
            e.HasOne(x => x.JobTarget).WithMany().HasForeignKey(x => x.JobTargetId);
            e.HasOne(x => x.Resume).WithMany().HasForeignKey(x => x.ResumeId);
            e.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId);
        });

        b.Entity<Question>(e =>
        {
            e.ToTable("questions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
            e.Property(x => x.SessionId).HasColumnName("session_id").HasMaxLength(36);
            e.Property(x => x.Ordinal).HasColumnName("ordinal");
            e.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Content).HasColumnName("content").HasColumnType("text");
            e.Property(x => x.Intent).HasColumnName("intent").HasColumnType("text");
            e.Property(x => x.AnchorIds).HasColumnName("anchor_ids").HasColumnType("json");
            e.Property(x => x.Answer).HasColumnName("answer").HasColumnType("mediumtext");
            e.Property(x => x.FollowUp).HasColumnName("follow_up").HasColumnType("text");
            e.Property(x => x.FollowUpAnswer).HasColumnName("follow_up_answer").HasColumnType("mediumtext");
            e.Property(x => x.Feedback).HasColumnName("feedback").HasColumnType("text");
            e.Property(x => x.AssessmentJson).HasColumnName("assessment_json").HasColumnType("json");
            e.Property(x => x.ClaimChecks).HasColumnName("claim_checks").HasColumnType("json");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(3)");
            e.HasOne(x => x.Session).WithMany(s => s.Questions).HasForeignKey(x => x.SessionId);
            e.HasIndex(x => new { x.SessionId, x.Ordinal }).IsUnique();
        });

        b.Entity<QuestionScore>(e =>
        {
            e.ToTable("question_scores");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasMaxLength(36);
            e.Property(x => x.QuestionId).HasColumnName("question_id").HasMaxLength(36);
            e.Property(x => x.Dimension).HasColumnName("dimension").HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Score).HasColumnName("score");
            e.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(255);
            e.HasOne(x => x.Question).WithMany(q => q.Scores).HasForeignKey(x => x.QuestionId);
            e.HasIndex(x => new { x.QuestionId, x.Dimension }).IsUnique();
        });
    }
}
