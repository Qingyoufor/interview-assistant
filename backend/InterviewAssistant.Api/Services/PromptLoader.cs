namespace InterviewAssistant.Api.Services;

/// <summary>读取 Services/Prompts 下的 prompt 文件。刻意不做缓存，方便边调 prompt 边生效。</summary>
public static class PromptLoader
{
    public static string Load(IHostEnvironment env, string name)
    {
        var path = Path.Combine(env.ContentRootPath, "Services", "Prompts", name + ".md");
        if (!File.Exists(path)) throw new FileNotFoundException($"找不到 prompt 文件：{path}");
        return File.ReadAllText(path);
    }
}
