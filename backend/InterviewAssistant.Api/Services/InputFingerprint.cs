using System.Security.Cryptography;
using System.Text;

namespace InterviewAssistant.Api.Services;

/// <summary>匹配缓存的输入指纹：简历与 JD 的解析结果都参与，任一方变化都会导致重算。</summary>
public static class InputFingerprint
{
    public static string Of(string? resumeJson, string? jobJson)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{resumeJson ?? ""}|{jobJson ?? ""}"));
        return Convert.ToHexStringLower(bytes);
    }
}
