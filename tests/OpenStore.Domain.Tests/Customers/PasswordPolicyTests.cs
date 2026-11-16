using OpenStore.Domain.Customers;
using Shouldly;

namespace OpenStore.Domain.Tests.Customers;

/// <summary>
/// 密码策略（后端设计 §6.7）的域单测。
/// </summary>
/// <remarks>
/// 每条规则都必须有一个"该拒的拒"与一个"不该拒的不拒"，只测前者时一个
/// <c>return [TooShort]</c> 的桩实现能让全部断言绿——而它会把所有用户挡在门外。
/// </remarks>
public sealed class PasswordPolicyTests
{
    /// <summary>一个确实合规的密码，用作"不该拒"的基准。</summary>
    private const string StrongPassword = "Tr0ub4dor&3xK";

    [Fact]
    public void 合规密码必须通过()
    {
        PasswordPolicy.Validate(StrongPassword).ShouldBeEmpty();
        PasswordPolicy.IsValid(StrongPassword).ShouldBeTrue();
    }

    [Fact]
    public void 长度上界内但不足12位必须拒()
    {
        // 11 位、混合字符、不在任何名单里 —— 唯一的问题就是短
        PasswordPolicy.Validate("Tr0ub4dor&3").ShouldBe([PasswordViolation.TooShort]);
    }

    [Fact]
    public void 恰好12位必须通过()
    {
        // 边界值：MinLength 是 12，写死 12 位合法密码，防止有人把 >= 写成 >
        PasswordPolicy.Validate("Tr0ub4dor&3x").ShouldBeEmpty();
    }

    [Fact]
    public void 超过128位必须拒()
    {
        var longPassword = new string('x', 128) + "Aa1!";
        PasswordPolicy.Validate(longPassword).ShouldContain(PasswordViolation.TooLong);
    }

    [Fact]
    public void 纯数字必须拒()
    {
        PasswordPolicy.Validate("593028461739").ShouldContain(PasswordViolation.AllDigits);
    }

    [Fact]
    public void 包含数字的混合密码不算纯数字()
    {
        PasswordPolicy.Validate("59302846173a").ShouldNotContain(PasswordViolation.AllDigits);
    }

    [Fact]
    public void 全同字符必须拒()
    {
        // 既不是纯数字，也不在任何名单里，但熵约 6.5 bit
        PasswordPolicy.Validate("aaaaaaaaaaaa").ShouldContain(PasswordViolation.SingleRepeatedCharacter);
    }

    [Fact]
    public void 命中弱口令集合必须拒()
    {
        PasswordPolicy.Validate("password1234").ShouldContain(PasswordViolation.WeakListEntry);
    }

    [Fact]
    public void 弱口令集合判定不区分大小写()
    {
        PasswordPolicy.Validate("PassWord1234").ShouldContain(PasswordViolation.WeakListEntry);
    }

    [Theory]
    // 这些都不在任何 Top 名单里，但都是"词根 + 装饰"，撞库第一小时就破
    [InlineData("Password123456!")]
    [InlineData("MyQwerty2026!")]
    [InlineData("OpenStore@2026")]
    [InlineData("XWelcome2OpenStore")]
    public void 词根加装饰必须拒(string candidate)
    {
        PasswordPolicy.Validate(candidate).ShouldContain(PasswordViolation.WeakRoot);
    }

    [Fact]
    public void 违规定必须全部收集而不是首条即返()
    {
        // "123456789012" 同时是纯数字、又含词根 123456；用户应当一次看到全部原因
        var violations = PasswordPolicy.Validate("123456789012");

        violations.ShouldContain(PasswordViolation.AllDigits);
        violations.ShouldContain(PasswordViolation.WeakRoot);
        violations.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void 空密码按过短处理而不是抛异常()
    {
        // 登录/改密端点把空值直接交给策略，抛异常会变成 500
        PasswordPolicy.Validate(null).ShouldBe([PasswordViolation.TooShort]);
        PasswordPolicy.Validate(string.Empty).ShouldBe([PasswordViolation.TooShort]);
    }

    [Fact]
    public void 内置弱口令基线不得为空()
    {
        // 名单被误删成空串时，策略会静默退化成"只查长度"，没有任何其它地方会红
        PasswordPolicy.WeakListCount.ShouldBeGreaterThan(100);
    }
}
