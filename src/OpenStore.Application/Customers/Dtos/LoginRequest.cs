namespace OpenStore.Application.Customers.Dtos;

/// <summary>管理员登录请求体（POST /api/admin/auth/login）。</summary>
public sealed record LoginRequest(string Email, string Password);
