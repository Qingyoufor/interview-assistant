using System.Text.Encodings.Web;
using System.Text.Json;
using InterviewAssistant.Api.Models;
using InterviewAssistant.Api.Services;
using InterviewAssistant.Api.Services.Interview;
using InterviewAssistant.Api.Services.Llm;
using InterviewAssistant.Api.Services.Match;
using InterviewAssistant.Api.Services.Questions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Qy.Eval;

var casesLimit = IntArg(args, "--cases", int.MaxValue);
var answersPerCase = IntArg(args, "--answers", 2);
var outPath = TextArg(args, "--out", "evals/report.json");

var environment = new EvalEnvironment();
using var loggerFactory = LoggerFactory.Create(builder => builder
    .AddSimpleConsole(o => o.SingleLine = true)
    .SetMinimumLevel(LogLevel.Warning));

using var http = new HttpClient
{
    BaseAddress = new Uri("https://api.deepseek.com/"),
    Timeout = TimeSpan.FromSeconds(120)
};

// 只在本地跑：key 从环境变量 DEEPSEEK_API_KEY 读，不落盘
var llm = new DeepSeekClient(http, new ConfigurationBuilder().Build(), loggerFactory.CreateLogger<DeepSeekClient>());
var resumeParser = new ResumeParser(llm, environment, loggerFactory.CreateLogger<ResumeParser>());
var jdParser = new JdParser(llm, environment, loggerFactory.CreateLogger<JdParser>());
var matchService = new MatchService(llm, environment, loggerFactory.CreateLogger<MatchService>());
var generator = new QuestionGenerator(llm, environment, loggerFactory.CreateLogger<QuestionGenerator>());
var interview = new InterviewService(llm, environment, loggerFactory.CreateLogger<InterviewService>());

var cases = EvalCases.All.Take(Math.Max(1, casesLimit)).ToList();
var runs = new List<CaseRun>();
var errors = new List<string>();

Console.WriteLine($"评测开始：{cases.Count} 个 case，每个 case 评估 {Math.Max(0, answersPerCase)} 道作答");

foreach (var evalCase in cases)
{
    Console.WriteLine($"[{evalCase.Id}] {evalCase.Level}/{evalCase.Kind} …");

    try
    {
        var run = await RunCaseAsync(evalCase, resumeParser, jdParser, matchService, generator, interview, Math.Max(0, answersPerCase));
        runs.Add(run);
        Console.WriteLine($"[{evalCase.Id}] 题目 {run.Questions.Count} 道，调用 {run.Calls} 次，repair {run.Repairs} 次");
    }
    catch (Exception ex)
    {
        errors.Add($"{evalCase.Id}: {ex.Message}");
        Console.WriteLine($"[{evalCase.Id}] 失败：{ex.Message}");
    }
}

var markers = cases.SelectMany(c => c.Markers).ToList();
var metrics = EvalCalculator.Compute(runs, markers);
var gapQuestions = runs.Sum(run => run.Questions.Count(q => q.Source == "gap"));
var failures = EvalCalculator.Failures(metrics);

// 没有缺口题时 gapCitationRate 恒为 1，指标没有样本就等于没测
if (gapQuestions == 0) failures.Add("gapCitationRate 没有缺口题样本");
failures.AddRange(errors);

var report = new
{
    generatedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
    cases = runs.Select(run => new
    {
        id = run.Id,
        questions = run.Questions.Count,
        gapQuestions = run.Questions.Count(q => q.Source == "gap"),
        calls = run.Calls,
        repairs = run.Repairs,
        privacyHits = EvalCalculator.PrivacyHits([run], markers)
    }),
    metrics = new
    {
        duplicateQuestionRate = metrics.DuplicateQuestionRate,
        evidenceValidityRate = metrics.EvidenceValidityRate,
        gapCitationRate = metrics.GapCitationRate,
        privacyMarkerHits = metrics.PrivacyMarkerHits,
        jsonRepairRate = metrics.JsonRepairRate,
        gapQuestions
    },
    failedCases = errors,
    failures,
    passed = failures.Count == 0
};

var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var fullPath = Path.GetFullPath(outPath);
Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(report, options));

Console.WriteLine();
Console.WriteLine($"duplicateQuestionRate {metrics.DuplicateQuestionRate}（≤ {EvalCalculator.MaxDuplicateRate}）");
Console.WriteLine($"evidenceValidityRate  {metrics.EvidenceValidityRate}（≥ {EvalCalculator.MinEvidenceValidityRate}）");
Console.WriteLine($"gapCitationRate       {metrics.GapCitationRate}（= {EvalCalculator.MinGapCitationRate}）");
Console.WriteLine($"privacyMarkerHits     {metrics.PrivacyMarkerHits}（= {EvalCalculator.MaxPrivacyMarkerHits}）");
Console.WriteLine($"jsonRepairRate        {metrics.JsonRepairRate}（≤ {EvalCalculator.MaxJsonRepairRate}）");
Console.WriteLine(failures.Count == 0 ? "五项指标全部达标" : "未达标：" + string.Join("；", failures));
Console.WriteLine($"报告已写入 {fullPath}");

return failures.Count == 0 ? 0 : 1;

static async Task<CaseRun> RunCaseAsync(
    EvalCase evalCase,
    ResumeParser resumeParser,
    JdParser jdParser,
    MatchService matchService,
    QuestionGenerator generator,
    InterviewService interview,
    int answersPerCase)
{
    var ct = CancellationToken.None;
    var resume = await resumeParser.ParseAsync(evalCase.Resume, ct);
    var job = await jdParser.ParseAsync(evalCase.Jd, ct);

    // 两次解析 + 一次匹配 + 一次出题
    var calls = 4;
    var repairs = 0;
    var outputs = new List<string>();

    var match = await matchService.MatchAsync(
        new MatchInput(resume.Profile, resume.Anchors, job.Profile, job.Anchors), ct);
    outputs.Add(Json(match.Result));
    repairs += match.Repairs;

    var generated = await generator.GenerateAsync(
        new QuestionGenInput(resume.Profile, resume.Anchors, job.Profile, job.Anchors, match.Result), ct);
    outputs.Add(Json(generated.Questions));
    repairs += generated.Repairs;

    var anchors = resume.Anchors.Concat(job.Anchors).ToList();
    var toAssess = generated.Questions.Take(Math.Min(answersPerCase, generated.Questions.Count)).ToList();

    for (var i = 0; i < toAssess.Count; i++)
    {
        var question = toAssess[i];
        var answer = evalCase.Answers[i % evalCase.Answers.Count];
        var outcome = await interview.EvaluateAsync(
            new InterviewContext(question.Content, question.Intent, question.Source, 0, answer, anchors), ct);

        outputs.Add(Json(outcome.Assessment));
        if (outcome.FollowUpText is not null) outputs.Add(outcome.FollowUpText);

        repairs += outcome.Repairs;
        calls += outcome.Decision.IsFollowUp ? 2 : 1;
    }

    return new CaseRun(
        evalCase.Id,
        generated.Questions.Select(q => new EvalQuestion(q.Source, q.AnchorIds, q.Content)).ToList(),
        anchors.Select(a => a.Id).ToHashSet(StringComparer.Ordinal),
        job.Anchors
            .GroupBy(a => a.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().QuoteExact, StringComparer.Ordinal),
        outputs,
        calls,
        repairs);
}

static string Json(object value) => JsonSerializer.Serialize(value, LlmJson.Options);

static int IntArg(string[] args, string name, int fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : fallback;
}

static string TextArg(string[] args, string name, string fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

/// <summary>评测项目不是 Web host，但 prompt 从 ContentRootPath 读，所以给个最小实现。</summary>
internal sealed class EvalEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Qy.Eval";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
