using OpenStore.Domain.Customers;

namespace OpenStore.Application.Customers.Dtos;

/// <summary>
/// 管理员登录 / me 的响应体：<c>{id,email,displayName,role,permissions[]}</c>。
/// 前端只拿它渲染菜单（仅 UX），服务端权限策略是唯一权威（后端设计 §6.4）。
/// </summary>
public sealed record AdminAuthResponse(
    Guid Id,
    string Email,
    string DisplayName,
    AdminRole Role,
    IReadOnlyList<string> Permissions);
