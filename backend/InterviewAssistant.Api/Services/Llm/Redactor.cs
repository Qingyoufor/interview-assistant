using System.Globalization;
using System.Text.RegularExpressions;

namespace InterviewAssistant.Api.Services.Llm;

/// <summary>
/// 调模型前把姓名 / 手机号 / 邮箱 / 身份证换掉（spec §13）。
/// 占位符里不含原值，模型即使把占位符抄回输出也不会泄露隐私。
/// 覆盖范围：格式化号码、带国家码或分隔符的手机号、行首「姓名：/ 名字：/ 我叫」以及
/// 「张三，男」这类简历首行姓名；认不出的自由文本姓名会漏，属已知取舍。
/// </summary>
public static partial class Redactor
{
    public const string Phone = "【手机号】";
    public const string Email = "【邮箱】";
    public const string IdCard = "【身份证】";
    public const string Name = "【姓名】";

    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // 18 位数字不一定是身份证，出生日期不合法就原样保留（订单号、流水号、雪花 id）
        text = IdCardRegex().Replace(text, m => IsIdCardNumber(m.Value) ? IdCard : m.Value);
        text = PhoneRegex().Replace(text, Phone);
        text = EmailRegex().Replace(text, Email);
        text = NameLabelRegex().Replace(text, Name);
        text = NamedFieldRegex().Replace(text, "$1" + Name);
        text = MyNameRegex().Replace(text, Name);
        return HeaderNameRegex().Replace(text, Name);
    }

    private static bool IsIdCardNumber(string value) =>
        value.Length == 18
        && DateTime.TryParseExact(value[6..14], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth)
        && birth.Year is >= 1900 and <= 2100;

    [GeneratedRegex(@"(?<!\d)\d{17}[\dXx](?!\d)")]
    private static partial Regex IdCardRegex();

    // 11 位手机号，允许 138 0013 8000 / 138-0013-8000 以及 +86 前缀
    [GeneratedRegex(@"(?<!\d)(?:\+?86[-\s]?)?1[3-9]\d(?:[-\s]?\d{4}){2}(?!\d)")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    // 姓名：张三
    [GeneratedRegex(@"(?<=姓名\s*[:：]\s*)[\u4e00-\u9fa5]{2,4}")]
    private static partial Regex NameLabelRegex();

    // 名字 / Name 只在行首才算姓名字段，避免 Project Name、Company Name 被替换
    [GeneratedRegex(@"(?m)^(\s*(?:名字|Name)\s*[:：]\s*)[\u4e00-\u9fa5]{2,4}")]
    private static partial Regex NamedFieldRegex();

    // 我叫张三：后面必须紧跟标点/空白/行尾，避免「我叫这种设计」被误伤
    [GeneratedRegex(@"(?<=我叫)[\u4e00-\u9fa5]{2,3}(?=[，,。！!；;、\s]|$)")]
    private static partial Regex MyNameRegex();

    // 简历首行式姓名：张三，男，25 岁 / 欧阳娜娜 | 女 | 5 年经验
    [GeneratedRegex(@"(?m)^[\u4e00-\u9fa5]{2,4}(?=\s*[，,|｜]\s*(?:男|女|\d{1,2}\s*岁))")]
    private static partial Regex HeaderNameRegex();
}
