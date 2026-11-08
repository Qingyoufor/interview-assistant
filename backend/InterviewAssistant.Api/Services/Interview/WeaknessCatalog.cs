namespace InterviewAssistant.Api.Services.Interview;

/// <summary>弱点标签固定词表（spec §8）。模型只能从中选，统计才能跨场次聚合。</summary>
public static class WeaknessCatalog
{
    public static readonly string[] All =
    [
        "自我介绍", "转行动机", "项目真实性", "技术深度", "性能优化", "并发",
        "数据库索引", "事务", "缓存一致性", "消息队列", "分布式", "异常处理",
        "测试", "部署运维", "系统设计", "取舍权衡", "量化结果", "团队协作",
        "沟通表达", "学习能力", "抗压", "薪资期望", "反问环节", "算法基础", "框架原理"
    ];

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    private static bool IsValid(string tag) => Set.Contains(tag);

    /// <summary>过滤掉词表外的标签，避免脏数据进统计。</summary>
    public static List<string> Filter(IEnumerable<string> tags) =>
        tags.Where(IsValid).Distinct(StringComparer.Ordinal).ToList();
}
