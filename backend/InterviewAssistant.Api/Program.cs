using InterviewAssistant.Api.Data;
using InterviewAssistant.Api.Services;
using InterviewAssistant.Api.Services.Llm;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var conn = builder.Configuration.GetConnectionString("InterviewAssistant")
           ?? throw new InvalidOperationException("缺少连接串 ConnectionStrings:InterviewAssistant");
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseMySql(conn, new MySqlServerVersion(new Version(8, 0, 46))));

var baseUrl = builder.Configuration["DeepSeek:BaseUrl"] ?? "https://api.deepseek.com";
builder.Services.AddHttpClient<DeepSeekClient>(c =>
{
    c.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddScoped<ResumeParser>();
builder.Services.AddScoped<JdParser>();
builder.Services.AddScoped<InterviewAssistant.Api.Services.Match.MatchService>();
builder.Services.AddScoped<InterviewAssistant.Api.Services.Match.MatchStore>();
builder.Services.AddScoped<InterviewAssistant.Api.Services.Questions.QuestionGenerator>();
builder.Services.AddScoped<InterviewAssistant.Api.Services.Interview.InterviewService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Interview Assistant API v1");
        c.DocumentTitle = "Interview Assistant API";
    });
}

app.UseCors();
app.MapControllers();

app.Run();
