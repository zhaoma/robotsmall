namespace OpenStore.Domain.Shared;

/// <summary>
/// 不变量（invariant）被破坏时抛出。错误码统一带 <c>INV_</c> 前缀，便于在日志与告警里
/// 把"用户输入不合法"（400 校验失败）与"代码把聚合改坏了"（500 级、应触发告警）区分开。
/// </summary>
public sealed class InvariantViolationException : DomainException
{
    public const string CodePrefix = "INV_";

    public InvariantViolationException(string code, string message)
        : base(EnsurePrefixed(code), message)
    {
    }

    private static string EnsurePrefixed(string code)
        => code.StartsWith(CodePrefix, StringComparison.Ordinal) ? code : CodePrefix + code;
}
