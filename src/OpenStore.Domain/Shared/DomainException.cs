namespace OpenStore.Domain.Shared;

/// <summary>
/// 领域层统一异常基类。区别于点抛出 <see cref="InvalidOperationException"/> 的地方在于
/// <see cref="Code"/> 是机器可读、稳定、可被 API 层映射成 ProblemDetails 的 <c>code</c> 字段
/// （后端设计 §8.2），前端据此显示本地化文案，不解析 <see cref="Exception.Message"/>。
/// </summary>
/// <remarks>
/// M0 只有 <see cref="Code"/>。按后端设计 §1.1，M1 会扩出 <c>MessageKey</c>（i18n 键）
/// 与 <c>Args</c>（插值参数），届时 <see cref="Code"/> 保持不变即可向后兼容。
/// </remarks>
public class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("DomainException.Code must not be empty.", nameof(code));
        }

        Code = code;
    }

    public DomainException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("DomainException.Code must not be empty.", nameof(code));
        }

        Code = code;
    }

    /// <summary>机器可读错误码，形如 <c>ORDER_INVALID_TRANSITION</c>。稳定契约，不允许按语言变化。</summary>
    public string Code { get; }
}
