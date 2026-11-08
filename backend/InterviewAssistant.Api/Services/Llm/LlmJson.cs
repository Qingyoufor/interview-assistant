using System.Text.Encodings.Web;
using System.Text.Json;

namespace InterviewAssistant.Api.Services.Llm;

/// <summary>调用模型和落库时共用的 JSON 选项：camelCase、大小写不敏感、中文不转义。</summary>
public static class LlmJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
