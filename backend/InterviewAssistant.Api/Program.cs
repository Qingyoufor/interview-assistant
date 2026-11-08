using Microsoft.EntityFrameworkCore;
using InterviewAssistant.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var conn = builder.Configuration.GetConnectionString("Qy")
           ?? throw new InvalidOperationException("缺少连接串 ConnectionStrings:Qy");
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseMySql(conn, new MySqlServerVersion(new Version(8, 0, 46))));

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

app.MapGet("/health", async (AppDbContext db) =>
{
    try
    {
        var ok = await db.Database.CanConnectAsync();
        return ok
            ? Results.Ok(new { status = "ok", db = "up" })
            : Results.Problem("数据库连不上");
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.Run();
