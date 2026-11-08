using Microsoft.AspNetCore.Mvc;

namespace InterviewAssistant.Api.Controllers;

public class HomeController(IWebHostEnvironment env) : ControllerBase
{
    /// <summary>根路径：开发环境跳到 Swagger，其他环境跳到健康检查。</summary>
    [HttpGet("/")]
    public IActionResult Index() =>
        Redirect(env.IsDevelopment() ? "/swagger/index.html" : "/health");
}
