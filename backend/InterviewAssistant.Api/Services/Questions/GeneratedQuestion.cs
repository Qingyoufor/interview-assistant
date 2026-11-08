namespace InterviewAssistant.Api.Services.Questions;

/// <summary>模型生成的一道题。source 决定它必须引用哪一侧的锚点。</summary>
public sealed class GeneratedQuestion
{
    public string Type { get; set; } = "basic";
    public string Source { get; set; } = "general";
    public List<string> AnchorIds { get; set; } = [];
    public string Content { get; set; } = "";
    public string Intent { get; set; } = "";

    /// <summary>模型可能显式返回 null，校验前先归一化，避免校验器空引用。</summary>
    public void Normalize()
    {
        Type = string.IsNullOrWhiteSpace(Type) ? "basic" : Type;
        Source = string.IsNullOrWhiteSpace(Source) ? "general" : Source;
        Content ??= "";
        Intent ??= "";
        AnchorIds = (AnchorIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
    }
}
