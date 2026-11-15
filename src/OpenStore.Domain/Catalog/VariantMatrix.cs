using System.Text.RegularExpressions;

namespace OpenStore.Domain.Catalog;

/// <summary>
/// 变体矩阵的规范化工具：把"一组轴值"降维成一个<b>稳定</b>的标量签名，
/// 这样 DB 才能对"变长子行集合"建唯一索引 <c>(ProductId, AxisSignature)</c>（数据模型 §D7）。
/// </summary>
public static partial class VariantMatrix
{
    /// <summary>
    /// 生成轴签名。有轴时形如 <c>color=red|size=m</c>；<b>无轴时返回空串</b>（单规格商品）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>必须按 key 排序</b>：<c>{color:red, size:m}</c> 与 <c>{size:m, color:red}</c> 是同一个组合，
    /// 如果按字典枚举顺序拼接会得到两个签名，唯一索引就形同虚设（同一组合能建出两条变体）。
    /// 排序用 <see cref="StringComparer.Ordinal"/> 而不是默认比较器：后者受区域性影响，
    /// 同一份数据在不同 locale 的服务器上会算出不同签名。
    /// </para>
    /// <para>
    /// <b>为什么允许空串（单规格商品）</b>：需求里"单规格商品"是真实存在的一类（无颜色无尺码），
    /// 它必须有且只有一个变体。若强制要求至少一个轴，这类商品就只能靠"伪造一个 spec=default 轴"
    /// 才能建出来——而伪造轴会出现在前台的规格表里，用户会看到一行莫名其妙的"规格：默认"。
    /// 空串在唯一索引 <c>(ProductId, AxisSignature)</c> 下天然保证"单规格商品最多一个变体"，
    /// 不需要 domain 层再写特判。
    /// </para>
    /// <para>
    /// <b>为什么签名用轴 code 而不是 <c>AttributeDefinitionId</c></b>（数据模型曾建议按 Id 排序）：
    /// 按 Id 排序确实能免疫"属性 code 改名"，但代价是签名变成 <c>&lt;guid&gt;:&lt;guid&gt;;</c> 这种
    /// 无法从日志/错误信息里读出含义的串。而 code 改名本身是需要重算全部签名的数据迁移
    /// （属显式操作，有迁移脚本可查）；让签名保持可读，换来的是每一次排障都不必再去 join 两张表。
    /// 漂移是一次性的、可脚本修；可读性是每天都要用的。
    /// </para>
    /// </remarks>
    public static string BuildSignature(IReadOnlyDictionary<string, string>? axes)
    {
        if (axes is null || axes.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(axes.Count);
        foreach (var axis in axes.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var key = Normalize(axis.Key, "key");
            var value = Normalize(axis.Value, "value");
            parts.Add($"{key}={value}");
        }

        return string.Join('|', parts);
    }

    private static string Normalize(string? raw, string what)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new Shared.InvariantViolationException(
                "VARIANT_AXIS_VALUE_REQUIRED",
                $"Variant axis {what} must not be blank.");
        }

        var normalized = raw.Trim().ToLowerInvariant();
        if (!AxisTokenRegex().IsMatch(normalized))
        {
            throw new Shared.InvariantViolationException(
                "VARIANT_AXIS_VALUE_INVALID",
                $"Variant axis {what} '{raw}' must match ^[a-z0-9][a-z0-9_-]*$ after normalization.");
        }

        return normalized;
    }

    // 轴值会进 SKU 与 URL 查询串，所以只允许小写字母数字与 - _
    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex AxisTokenRegex();
}
