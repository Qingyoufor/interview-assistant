using InterviewAssistant.Api.Models;

namespace InterviewAssistant.Api.Services;

/// <summary>
/// 证据锚点的定位工具：在原文里找到锚点对应的段落，作为「为什么问这个」的引用。
/// 优先命中行首（标题行），避免落到正文里对同一名词的提及；ASCII 技术名词按词边界匹配。
/// </summary>
public static class AnchorLocator
{
    /// <summary>单条 quote 的字符上限（spec §4：≤200 字）。</summary>
    public const int MaxQuoteChars = 200;
    public const int MaxQuoteLines = 4;

    /// <summary>技能锚点上限，避免一份简历生成几十条技能锚点。</summary>
    public const int MaxSkillAnchors = 30;

    /// <summary>用正文开头做兜底探针时取的长度。</summary>
    private const int ProbeLength = 12;

    public static Anchor Locate(string id, string kind, string label, string text, string raw, params string?[] extraProbes)
    {
        var probes = new List<string?> { label };
        probes.AddRange(extraProbes);
        probes.Add(Head(text, ProbeLength));

        foreach (var probe in probes)
        {
            if (string.IsNullOrWhiteSpace(probe)) continue;

            var idx = FindLineStart(raw, probe);
            if (idx < 0) idx = IndexOfToken(raw, probe);
            if (idx < 0) continue;

            var (start, end) = Block(raw, idx);
            var quote = raw[start..end].Replace("\r", string.Empty).Trim();
            return new Anchor(id, kind, label, quote, true);
        }

        return new Anchor(id, kind, label, Head(text, MaxQuoteChars), false);
    }

    /// <summary>返回 probe 在某行行首出现的位置；没有则返回 -1。</summary>
    public static int FindLineStart(string raw, string probe)
    {
        var pos = 0;
        while (pos < raw.Length)
        {
            var lineEnd = raw.IndexOf('\n', pos);
            var end = lineEnd < 0 ? raw.Length : lineEnd;
            var line = raw[pos..end];
            var trimmed = line.TrimStart(' ', '\t', '\r');
            if (trimmed.StartsWith(probe, StringComparison.Ordinal))
                return pos + (line.Length - trimmed.Length);
            pos = end + 1;
        }
        return -1;
    }

    /// <summary>
    /// 查找 probe。ASCII 技术名词（Java / C++ / K8s）要求前后不是字母数字，避免 Java 命中 JavaScript。
    /// 非 ASCII（中文）直接按子串匹配。
    /// </summary>
    public static int IndexOfToken(string raw, string probe)
    {
        if (!IsAsciiToken(probe)) return raw.IndexOf(probe, StringComparison.Ordinal);

        var from = 0;
        while (from <= raw.Length - probe.Length)
        {
            var idx = raw.IndexOf(probe, from, StringComparison.Ordinal);
            if (idx < 0) return -1;

            var beforeOk = idx == 0 || !IsWordChar(raw[idx - 1]);
            var afterIdx = idx + probe.Length;
            var afterOk = afterIdx >= raw.Length || !IsWordChar(raw[afterIdx]);
            if (beforeOk && afterOk) return idx;

            from = idx + probe.Length;
        }
        return -1;
    }

    /// <summary>从命中位置所在行开始，取最多 4 行，并且绝不超过 320 字。</summary>
    public static (int Start, int End) Block(string raw, int idx)
    {
        var start = raw.LastIndexOf('\n', Math.Max(0, idx - 1)) + 1;
        var end = start;
        for (var lines = 0; lines < MaxQuoteLines && end < raw.Length; lines++)
        {
            var nl = raw.IndexOf('\n', end);
            if (nl < 0) { end = raw.Length; break; }
            end = nl + 1;
            if (end - start > MaxQuoteChars) break;
        }

        // 单行超长时上面的循环会一次吞到底，必须再硬截断一次
        var limit = Math.Min(raw.Length, start + MaxQuoteChars);
        if (end > limit) end = SafeCut(raw, limit);
        return (start, end);
    }

    /// <summary>避免在代理对中间截断（emoji 会被切成半个字符）。</summary>
    private static int SafeCut(string raw, int index)
    {
        if (index <= 0 || index >= raw.Length) return index;
        return char.IsHighSurrogate(raw[index - 1]) ? index - 1 : index;
    }

    private static bool IsAsciiToken(string s) =>
        s.Length > 0 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '+' or '#' or '-' or '_');

    private static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    private static string Head(string s, int n) => string.IsNullOrEmpty(s) || n <= 0 ? "" : (s.Length <= n ? s : s[..n]);
}
