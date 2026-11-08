namespace InterviewAssistant.Api.Services;

/// <summary>录入文本的统一校验与截断，两个 Controller 共用。</summary>
public static class InputGuard
{
    /// <summary>录入文本的通用上限，两个录入接口共用一处。</summary>
    public const int MinChars = 50;
    public const int MaxChars = 12000;
    public const int TitleMaxLength = 120;
    public const int LevelMaxLength = 32;

    public static InputGuardResult Prepare(string? input, int minChars, int maxChars, string label = "内容")
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length < minChars)
            return new InputGuardResult(null, false, $"{label}太短，至少 {minChars} 字");

        var truncated = text.Length > maxChars;
        if (truncated) text = SafeTruncate(text, maxChars);

        return new InputGuardResult(text, truncated, null);
    }

    /// <summary>按字符截断，但不切断代理对（emoji）。</summary>
    private static string SafeTruncate(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        var cut = maxChars;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut];
    }

    /// <summary>把任意文本收敛到数据库列宽以内（标题、级别等）。</summary>
    public static string Clamp(string? value, int maxChars)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= maxChars ? text : SafeTruncate(text, maxChars);
    }
}

public sealed record InputGuardResult(string? Text, bool Truncated, string? Error)
{
    public bool Ok => Error is null && Text is not null;
}
