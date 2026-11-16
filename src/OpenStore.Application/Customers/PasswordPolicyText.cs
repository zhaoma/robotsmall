using OpenStore.Domain.Customers;

namespace OpenStore.Application.Customers;

/// <summary>
/// 密码策略违规 → 用户可见文案（<c>openapi</c> 的 <c>errors.newPassword</c> 数组元素）。
/// </summary>
/// <remarks>
/// <b>为什么文案在这里而不在 <see cref="PasswordPolicy"/></b>：策略是纯规则，将来客户注册、安装向导、
/// 后台重置他人密码都会复用它，而它们对同一个违规要说的措辞不同（给自己改密可以说"太短了"，
/// 给他人重置要说"至少 12 位"）。规则层只给"违反了哪条"，措辞由用例层决定。
/// <b>为什么不在端点里</b>：三条写路径各写一份文案，必然出现同一规则三种说法，
/// 前端无法用一条断言检查提示是否正确。
/// </remarks>
internal static class PasswordPolicyText
{
    internal const string CurrentPasswordMismatch = "当前密码不正确";

    /// <summary>把违规项翻译成提示。顺序与 <see cref="PasswordPolicy.Validate"/> 返回顺序一致（先长度后字符）。</summary>
    internal static string[] Describe(IReadOnlyList<PasswordViolation> violations)
    {
        var messages = new List<string>(violations.Count);

        foreach (var violation in violations)
        {
            messages.Add(violation switch
            {
                PasswordViolation.TooShort => $"密码至少 {PasswordPolicy.MinLength} 位",
                PasswordViolation.TooLong => $"密码最多 {PasswordPolicy.MaxLength} 位",
                PasswordViolation.AllDigits => "密码不能全是数字",
                PasswordViolation.SingleRepeatedCharacter => "密码不能是同一个字符的重复",
                PasswordViolation.WeakListEntry => "该密码是常见弱口令，请换一个",
                PasswordViolation.WeakRoot => "密码不能包含 password / qwerty / 123456 这类常见词根",
                // 新增违规项时在这里漏掉会让用户看到一句空提示，所以给一条兜底而不是 null
                _ => "密码不符合安全要求",
            });
        }

        return [.. messages];
    }
}
