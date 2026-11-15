using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace OpenStore.Tools.Validation;

/// <summary>
/// token 白名单：tokens.schema.json 的 properties 键集（此文件同时是默认值来源与 admin 表单源）。
/// </summary>
internal sealed record TokenWhitelist(IReadOnlyList<string> TokenKeys)
{
    public static readonly TokenWhitelist Empty = new(Array.Empty<string>());
}

/// <summary>
/// 区块白名单：blocks.schema.json 的 $defs.blockKey.enum（哪些区块能插入）
/// 与 $defs.blocks 的键集（哪些区块有 props 可配）。两者必须同集，由 check-themes 校验。
/// </summary>
internal sealed record BlockWhitelist(IReadOnlyList<string> BlockKeys, IReadOnlySet<string> KeysWithProps)
{
    public static readonly BlockWhitelist Empty = new(Array.Empty<string>(), new HashSet<string>(StringComparer.Ordinal));
}

/// <summary>
/// 主题资产读取。两个命令（check-templates 校验模板 themeTokens 预设、check-themes 校验主题包本身）
/// 共用这里的取数逻辑，避免两处各写一份抽取规则而漂移。
/// </summary>
internal static partial class ThemeAssets
{
    [GeneratedRegex(HostContract.TokenKeyPattern, RegexOptions.CultureInvariant)]
    private static partial Regex TokenKeyRegex();

    public static bool IsWellFormedTokenKey(string key) => TokenKeyRegex().IsMatch(key);

    /// <summary>
    /// tokens.schema.json 既是 token 值的校验器，也是可编辑白名单声明处：properties 的键 = 可编辑 token 键集。
    /// 区块白名单不在这里 —— 它属于 blocks.schema.json（见 ReadBlockWhitelist）。
    /// 曾把两者塞进同一个 editable.schema.json，结果"哪个文件是白名单真源"在 manifest、check-themes、
    /// admin 表单三处各有一份答案；现在一个文件只管一件事。
    /// </summary>
    public static TokenWhitelist ReadTokenWhitelist(string fullPath, string relativePath, CheckRun run)
        => ReadTokenWhitelist(JsonIo.TryLoad(fullPath, relativePath, run), relativePath, run);

    public static TokenWhitelist ReadTokenWhitelist(JsonNode? node, string relativePath, CheckRun run)
    {
        if (node is not JsonObject)
        {
            return TokenWhitelist.Empty;
        }

        var tokenKeys = node["properties"] is JsonObject properties
            ? properties.Select(pair => pair.Key).ToList()
            : new List<string>();

        if (tokenKeys.Count == 0)
        {
            run.Add(
                ErrorCodes.SchemaInvalid,
                relativePath,
                "/properties",
                "no token keys declared (properties is empty): the merchant would open the theme editor and find nothing to edit");
        }

        return new TokenWhitelist(tokenKeys);
    }

    /// <summary>
    /// blocks.schema.json：$defs.blockKey.enum 为可插入区块白名单，$defs.blocks 为每个区块的 props schema。
    /// 这里只取两个键集，同集性由调用方报 BLOCK_PROPS_MISSING（需要精确到缺失的区块名，不适合在取数层报）。
    /// </summary>
    public static BlockWhitelist ReadBlockWhitelist(string fullPath, string relativePath, CheckRun run)
        => ReadBlockWhitelist(JsonIo.TryLoad(fullPath, relativePath, run), relativePath, run);

    public static BlockWhitelist ReadBlockWhitelist(JsonNode? node, string relativePath, CheckRun run)
    {
        if (node is not JsonObject)
        {
            return BlockWhitelist.Empty;
        }

        if (node.At("$defs")?.At("blockKey")?.At("enum") is not JsonArray declared)
        {
            run.Add(
                ErrorCodes.SchemaInvalid,
                relativePath,
                "/$defs/blockKey",
                "block whitelist not declared (expected $defs.blockKey.enum)");
            return BlockWhitelist.Empty;
        }

        var keysWithProps = node.At("$defs")?.At("blocks") is JsonObject blocks
            ? blocks.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        return new BlockWhitelist(declared.Strings().ToList(), keysWithProps);
    }

    /// <summary>
    /// token 默认值来源：tokens.schema.json 的 properties[key].default。
    /// 缺 default 单独报 TOKEN_DEFAULT_MISSING：它不是"schema 写错了"（schema 本体合法），
    /// 而是"这个 token 没有默认值"，后果是商家新建主题时该 token 空着、渲染出未定义 CSS 变量。
    /// </summary>
    public static List<KeyValuePair<string, JsonNode?>> ReadDefaultsFromSchema(string fullPath, string relativePath, CheckRun run)
    {
        var node = JsonIo.TryLoad(fullPath, relativePath, run);
        if (node?.At("properties") is not JsonObject properties)
        {
            return new List<KeyValuePair<string, JsonNode?>>();
        }

        var result = new List<KeyValuePair<string, JsonNode?>>();
        foreach (var pair in properties)
        {
            var defaultValue = pair.Value?.At("default");
            if (defaultValue is null)
            {
                run.Add(
                    ErrorCodes.TokenDefaultMissing,
                    relativePath,
                    JsonIo.Child("/properties", pair.Key),
                    "token has no default value (defaults are derived from the schema, so a missing default means the token renders as an undefined CSS variable)");
                continue;
            }

            result.Add(new KeyValuePair<string, JsonNode?>(pair.Key, defaultValue));
        }

        return result;
    }

    /// <summary>
    /// 用白名单 schema 里每个 key 自己的子 schema 校验 token 值。
    /// 只校验两边都存在的键（键集差异由 TOKEN_KEY_MISMATCH 单独报，避免同一问题出两个码）。
    /// JsonSchema.Net 8.x 不再暴露 properties 关键字的强类型访问，因此把子 schema 序列化成独立 schema 再评估；
    /// 这要求白名单里每个属性的 schema 是自包含的（不许用外部 $ref）—— 主题白名单本来就必须自包含，否则 admin 无法单文件读取。
    /// </summary>
    public static void ValidateTokenValues(
        JsonNode? whitelistRoot,
        string whitelistRelativePath,
        IReadOnlyList<KeyValuePair<string, JsonNode?>> tokens,
        CheckRun run)
    {
        if (whitelistRoot?.At("properties") is not JsonObject properties)
        {
            return;
        }

        foreach (var (key, value) in tokens)
        {
            if (value is null || properties.At(key) is not { } propertySchema)
            {
                continue;
            }

            JsonSchema compiled;
            try
            {
                // 子 schema 单独序列化时没有 $schema，库会用默认方言（并对 x-control 之类的注解报未知关键字）。
                // 显式补上 2020-12 —— 架构承诺"未知 x-* 注解必须忽略而非报错"，门禁工具自己必须先做到。
                var fragment = new JsonObject { ["$schema"] = "https://json-schema.org/draft/2020-12/schema" };
                if (propertySchema is JsonObject propertyObject)
                {
                    foreach (var property in propertyObject)
                    {
                        fragment[property.Key] = property.Value?.DeepClone();
                    }
                }
                else
                {
                    fragment["allOf"] = new JsonArray(propertySchema.DeepClone());
                }

                compiled = JsonSchema.FromText(fragment.ToJsonString());
            }
            catch (JsonException ex)
            {
                run.Add(
                    ErrorCodes.SchemaInvalid,
                    whitelistRelativePath,
                    JsonIo.Child("/properties", key),
                    $"token schema for \"{key}\" is not a valid self-contained schema: {ex.Message}");
                continue;
            }

            var evaluation = compiled.Evaluate(JsonSerializer.SerializeToElement(value), new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (evaluation.IsValid)
            {
                continue;
            }

            var reasons = CollectErrors(evaluation).Distinct(StringComparer.Ordinal).Take(3).ToList();
            var reason = reasons.Count > 0 ? string.Join("; ", reasons) : "value does not match schema";

            run.Add(
                ErrorCodes.SchemaInvalid,
                whitelistRelativePath,
                JsonIo.Child("/properties", key),
                $"token \"{key}\" value {value.ToJsonString()} is invalid: {reason}");
        }
    }

    private static IEnumerable<string> CollectErrors(EvaluationResults results)
    {
        if (results.Errors is { Count: > 0 } errors)
        {
            foreach (var error in errors)
            {
                yield return error.Value;
            }
        }

        foreach (var detail in results.Details ?? Enumerable.Empty<EvaluationResults>())
        {
            foreach (var message in CollectErrors(detail))
            {
                yield return message;
            }
        }
    }
}
