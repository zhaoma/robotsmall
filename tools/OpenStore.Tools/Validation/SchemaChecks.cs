using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace OpenStore.Tools.Validation;

/// <summary>JSON Schema 校验 + 把 JsonSchema.Net 的评估树摊平成可 grep 的诊断。</summary>
internal static class SchemaChecks
{
    private static readonly EvaluationOptions Options = new()
    {
        OutputFormat = OutputFormat.List,
        // 主题/模板里的 "format": "color" 之类不做断言：格式断言会让未知 format 变成硬错误，
        // 与「向前兼容：未知注解忽略」的承诺冲突。值的形态用 pattern 表达。
        RequireFormatValidation = false,
    };

    public static JsonSchema LoadOrThrow(string fullPath) => JsonSchema.FromText(File.ReadAllText(fullPath));

    /// <summary>把 schema 的全部违规摊平成诊断。required 关键字不在这里报（见 ValidateRequired）。</summary>
    public static void Validate(JsonSchema schema, JsonNode instance, string file, CheckRun run)
    {
        EvaluationResults results;
        try
        {
            // JsonSchema.Net 8.x 的 Evaluate 只接受 JsonElement；节点树转过去一次即可（模板/清单都很小）。
            var element = JsonSerializer.SerializeToElement(instance);
            results = schema.Evaluate(element, Options);
        }
        catch (JsonException ex)
        {
            run.Add(ErrorCodes.SchemaInvalid, file, string.Empty, $"schema evaluation failed: {ex.Message}");
            return;
        }

        Flatten(results, file, run);
    }

    private static void Flatten(EvaluationResults results, string file, CheckRun run)
    {
        if (results.Errors is { Count: > 0 } errors)
        {
            foreach (var error in errors)
            {
                // required 交给 ValidateRequired 输出确定性措辞（"required property X missing"），
                // 库自带的措辞随版本变，不能作为门禁断言文本。
                if (error.Key == "required")
                {
                    continue;
                }

                run.Add(ErrorCodes.SchemaInvalid, file, results.InstanceLocation.ToString() ?? string.Empty, error.Value);
            }
        }

        foreach (var detail in results.Details ?? Enumerable.Empty<EvaluationResults>())
        {
            Flatten(detail, file, run);
        }
    }

    /// <summary>
    /// 自己实现 required 检查（只走 properties / items / $ref 三条路径）。
    /// 理由：V5 断言字符串是 `/defaults: required property themeId missing`，
    /// 必须由工具决定措辞与位置，而不是被第三方库的消息格式绑住。
    /// </summary>
    public static void ValidateRequired(JsonNode instance, JsonNode schemaRoot, string file, CheckRun run)
        => WalkRequired(instance, schemaRoot, schemaRoot, string.Empty, file, run, depth: 0);

    private static void WalkRequired(
        JsonNode instance,
        JsonNode? schema,
        JsonNode schemaRoot,
        string pointer,
        string file,
        CheckRun run,
        int depth)
    {
        if (depth > 64)
        {
            return;
        }

        schema = Deref(schema, schemaRoot);
        if (schema is null)
        {
            return;
        }

        if (instance is JsonObject obj && schema.At("required") is JsonArray required)
        {
            foreach (var entry in required)
            {
                if (entry.Str() is { } name && !obj.ContainsKey(name))
                {
                    run.Add(ErrorCodes.SchemaInvalid, file, pointer, $"required property {name} missing");
                }
            }
        }

        if (instance is JsonObject target && schema.At("properties") is JsonObject properties)
        {
            foreach (var pair in properties)
            {
                var child = target[pair.Key];
                if (child is JsonObject or JsonArray)
                {
                    WalkRequired(child, pair.Value, schemaRoot, JsonIo.Child(pointer, pair.Key), file, run, depth + 1);
                }
            }
        }

        if (instance is JsonArray array && schema.At("items") is { } items)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var item = array[i];
                if (item is JsonObject or JsonArray)
                {
                    WalkRequired(item, items, schemaRoot, JsonIo.Child(pointer, i), file, run, depth + 1);
                }
            }
        }
    }

    /// <summary>只解析本文档内部的 $ref（#/...）；外部引用在本仓库不存在，遇到就当缺失处理。</summary>
    private static JsonNode? Deref(JsonNode? schema, JsonNode schemaRoot)
    {
        var guard = 0;
        while (schema is JsonObject obj && obj.At("$ref")?.Str() is { } reference && guard++ < 16)
        {
            if (reference.Length == 0 || reference[0] != '#')
            {
                return null;
            }

            schema = JsonIo.ResolvePointer(schemaRoot, reference[1..]);
        }

        return schema;
    }
}
