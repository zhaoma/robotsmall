using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace OpenStore.Tools.Validation;

/// <summary>可编辑白名单：token 键集 + 区块键集，来自同主题的 editable.schema.json（或单一真源的 tokens.schema.json）。</summary>
internal sealed record Whitelist(IReadOnlyList<string> TokenKeys, IReadOnlyList<string> BlockKeys)
{
    public static readonly Whitelist Empty = new(Array.Empty<string>(), Array.Empty<string>());
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
    /// editable.schema.json 既是 token 值的校验器，也是白名单声明处：
    ///   properties 的键 = 可编辑 token 键集；
    ///   $defs.blockKey.enum = 首页区块白名单（兼容旧的 x-blocks 注解）。
    /// </summary>
    public static Whitelist ReadWhitelist(string fullPath, string relativePath, CheckRun run)
        => ReadWhitelist(JsonIo.TryLoad(fullPath, relativePath, run), relativePath, run);

    public static Whitelist ReadWhitelist(JsonNode? node, string relativePath, CheckRun run)
    {
        if (node is not JsonObject)
        {
            return Whitelist.Empty;
        }

        var tokenKeys = node["properties"] is JsonObject properties
            ? properties.Select(pair => pair.Key).ToList()
            : new List<string>();

        if (tokenKeys.Count == 0)
        {
            run.Add(ErrorCodes.TokenKeyMismatch, relativePath, "/properties", "no token keys declared (properties is empty)");
        }

        List<string> blockKeys;
        if (node.At("$defs")?.At("blockKey")?.At("enum") is JsonArray declared)
        {
            blockKeys = declared.Strings().ToList();
        }
        else if (node.At("x-blocks") is JsonArray legacy)
        {
            blockKeys = legacy.Strings().ToList();
        }
        else
        {
            blockKeys = new List<string>();
            run.Add(
                ErrorCodes.SchemaInvalid,
                relativePath,
                "/$defs/blockKey",
                "block whitelist not declared (expected $defs.blockKey.enum)");
        }

        return new Whitelist(tokenKeys, blockKeys);
    }

    /// <summary>tokens.json：扁平点分键 → 值。</summary>
    public static List<KeyValuePair<string, JsonNode?>> ReadTokenFile(string fullPath, string relativePath, CheckRun run)
    {
        var node = JsonIo.TryLoad(fullPath, relativePath, run);
        if (node is not JsonObject obj)
        {
            return new List<KeyValuePair<string, JsonNode?>>();
        }

        return obj.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value)).ToList();
    }

    /// <summary>单一真源形态（tokens.schema.json）：默认值取 properties[key].default。</summary>
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
                    ErrorCodes.SchemaInvalid,
                    relativePath,
                    JsonIo.Child("/properties", pair.Key),
                    "token has no default value (single-source mode derives defaults from schema)");
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
