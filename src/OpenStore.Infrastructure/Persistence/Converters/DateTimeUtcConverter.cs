using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OpenStore.Infrastructure.Persistence.Converters;

/// <summary>
/// 所有 <see cref="DateTime"/> 列的读取侧修正：<b>只把 <c>Kind</c> 标成 <see cref="DateTimeKind.Utc"/>，不改值</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>要解决的问题</b>（对照表 C11 / T2）：SQLite 没有原生日期类型，<c>TEXT</c> 读回来的
/// <see cref="DateTime"/> 一律是 <c>Kind = Unspecified</c>。一旦序列化成 JSON，前端拿到的是没有
/// 时区标记的字符串（<c>2026-11-17T06:04:35</c>），JS 的 <c>new Date()</c> 会按<b>浏览器本地时区</b>解析
/// —— 同一行数据在东八区与 UTC 的服务器上显示成两个不同时刻，且不报任何错。
/// </para>
/// <para>
/// <b>为什么不在这里做时区换算</b>：<c>ToUniversalTime()</c> 对 <c>Kind=Unspecified</c> 的值会按
/// 「服务器本地时区」折算。开发机在 UTC+8、容器在 UTC，同一条写入会在两个环境得到相差 8 小时的值 ——
/// 这是比"没有 Kind"更坏的错误（它连原始数值都改了）。约定是「<b>库里存的永远是 UTC 数值</b>」，
/// 写入侧由领域/应用层保证，读取侧只补 Kind 标记。
/// </para>
/// <para>
/// <b>为什么是实例类而不是静态工具</b>：<c>Properties&lt;DateTime&gt;().HaveConversion&lt;T&gt;()</c>
/// 要求 <c>T</c> 是带无参构造的 <c>ValueConverter</c> 子类（EF 会 new 一个实例放进模型缓存）。
/// 静态类无法作为泛型实参，所以转换逻辑必须写在构造函数里。
/// </para>
/// </remarks>
public sealed class DateTimeUtcConverter : ValueConverter<DateTime, DateTime>
{
    public DateTimeUtcConverter()
        : base(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}

/// <summary><see cref="DateTimeUtcConverter"/> 的可空版本：<c>null</c> 保持 <c>null</c>。</summary>
/// <remarks>
/// <b>不能把 null 变成 <c>default(DateTime)</c></b>：那会让"未上架"（<c>PublishedAt = NULL</c>）
/// 变成"0001-01-01 上架"，列表按上架时间排序时这些商品会全部沉底，看起来像"没有数据"。
/// </remarks>
public sealed class NullableDateTimeUtcConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableDateTimeUtcConverter()
        : base(
            v => v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
    {
    }
}
