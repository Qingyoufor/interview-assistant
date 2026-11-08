using System.Text.Json;
using InterviewAssistant.Api.Services.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace InterviewAssistant.Tests;

public class DeepSeekClientTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"{}"}}]}""")
            };
        }
    }

    private sealed class StubConfig : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "DeepSeek:ApiKey" ? "test-key" : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => throw new NotSupportedException();
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }

    [Fact]
    public async Task 隐私字段不会出现在模型请求里()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.deepseek.com/") };
        var client = new DeepSeekClient(http, new StubConfig(), NullLogger<DeepSeekClient>.Instance);

        await client.ChatJsonAsync("system",
            "手机号 13800138000，邮箱 zhang.san@example.com，身份证 11010119900307123X，姓名：张三", CancellationToken.None);

        var body = handler.Body;
        Assert.NotNull(body);

        // 原始请求体里 PII 是 ASCII 数字/字母，被转义与否都会原样出现，所以这里可以直接断言
        Assert.DoesNotContain("13800138000", body);
        Assert.DoesNotContain("zhang.san@example.com", body);
        Assert.DoesNotContain("11010119900307123X", body);

        // 中文内容会被转义成 \uXXXX，解码后再比对
        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
        Assert.NotNull(content);
        Assert.DoesNotContain("张三", content);
        Assert.Contains(Redactor.Phone, content);
        Assert.Contains(Redactor.Email, content);
        Assert.Contains(Redactor.IdCard, content);
        Assert.Contains(Redactor.Name, content);
    }
}
