namespace InterviewAssistant.Api.Models;

/// <summary>证据锚点：题目引用简历/JD 原文的最小单位。</summary>
/// <param name="Id">形如 r:proj:2、j:req:3，由代码分配</param>
/// <param name="Kind">project / experience / skill / requirement / responsibility</param>
/// <param name="Label">展示用标签</param>
/// <param name="Quote">原文片段；QuoteExact 为 false 时是模型转述</param>
/// <param name="QuoteExact">是否真的在原文里定位到</param>
public sealed record Anchor(string Id, string Kind, string Label, string Quote, bool QuoteExact);
