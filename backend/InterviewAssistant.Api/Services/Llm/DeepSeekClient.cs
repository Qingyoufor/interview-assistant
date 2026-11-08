using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace InterviewAssistant.Api.Services.Llm;

public sealed class DeepSeekClient(HttpClient http, IConfiguration cfg, ILogger<DeepSeekClient> log)
{
    public async Task<string> ChatJsonAsync(string systemPrompt, string userContent, CancellationToken ct)
    {
        var key = cfg["DeepSeek:ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("未配置 DeepSeek:ApiKey（appsettings.Development.json 或环境变量 DEEPSEEK_API_KEY）");

        // 唯一出口：简历、JD、回答都从这里进模型，脱敏放在这一处，避免各调用点漏做（spec §13）
        userContent = Redactor.Redact(userContent);

        var payload = new
        {
            model = cfg["DeepSeek:Model"] ?? "deepseek-chat",
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            },
            temperature = 0.2,
            max_tokens = 4096,
            response_format = new { type = "json_object" }
        };

        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                {
                    Content = JsonContent.Create(payload)
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

                using var resp = await http.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException($"DeepSeek {(int)resp.StatusCode}: {Truncate(body, 300)}");

                using var doc = JsonDocument.Parse(body);
                var content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (string.IsNullOrWhiteSpace(content))
                    throw new InvalidOperationException("DeepSeek 返回空内容");
                return content;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
                log.LogWarning("DeepSeek 调用失败（第 {Attempt} 次）：{Message}", attempt + 1, ex.Message);
                if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }

        throw new InvalidOperationException("DeepSeek 调用失败，已重试 2 次", last);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}
