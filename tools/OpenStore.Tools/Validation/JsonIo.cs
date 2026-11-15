using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenStore.Tools.Validation;

/// <summary>JSON 读取与小工具。所有读取都走这里，保证解析失败也能变成一条可读诊断而不是崩溃栈。</summary>
internal static class JsonIo
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>读取文件为 JsonNode；失败时写一条 SCHEMA_INVALID 诊断并返回 null（不抛异常）。</summary>
    public static JsonNode? TryLoad(string fullPath, string relativePath, CheckRun run)
    {
        string text;
        try
        {
            text = File.ReadAllText(fullPath);
        }
        catch (IOException ex)
        {
            run.Add(ErrorCodes.SchemaInvalid, relativePath, string.Empty, $"cannot read file: {ex.Message}");
            return null;
        }

        try
        {
            var node = JsonNode.Parse(text, documentOptions: DocumentOptions);
            if (node is null)
            {
                run.Add(ErrorCodes.SchemaInvalid, relativePath, string.Empty, "file is empty (expected a JSON object)");
            }

            return node;
        }
        catch (JsonException ex)
        {
            run.Add(ErrorCodes.SchemaInvalid, relativePath, string.Empty, $"invalid JSON: {ex.Message}");
            return null;
        }
    }

    /// <summary>JSON Pointer 转义：~ → ~0，/ → ~1（RFC 6901）。</summary>
    public static string Escape(string segment) => segment.Replace("~", "~0").Replace("/", "~1");

    public static string Child(string pointer, string segment) => $"{pointer}/{Escape(segment)}";

    public static string Child(string pointer, int index) => $"{pointer}/{index}";

    public static string? Str(this JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    public static int? Int(this JsonNode? node)
        => node is JsonValue value && value.TryGetValue<int>(out var i) ? i : null;

    public static JsonArray? Arr(this JsonNode? node) => node as JsonArray;

    public static JsonObject? Obj(this JsonNode? node) => node as JsonObject;

    /// <summary>取对象属性；缺省或为 null 时返回 null（JSON null 与缺失在本工具里同等对待）。</summary>
    public static JsonNode? At(this JsonNode? node, string name) => node?[name];

    public static IEnumerable<string> Strings(this JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            yield break;
        }

        foreach (var item in array)
        {
            if (item.Str() is { } s)
            {
                yield return s;
            }
        }
    }

    /// <summary>把 i18n 文件摊平成点分键集合。支持嵌套对象（a: { b: "x" } → a.b），因为翻译文件未来可能分组。</summary>
    public static void Flatten(JsonNode? node, string prefix, ISet<string> into)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    var key = prefix.Length == 0 ? pair.Key : $"{prefix}.{pair.Key}";
                    Flatten(pair.Value, key, into);
                }

                break;
            case JsonValue:
                if (prefix.Length > 0)
                {
                    into.Add(prefix);
                }

                break;
        }
    }

    /// <summary>按 "/" 切分的简易路径取值，只用于解析本仓库内部的 $ref（#/$defs/x）。</summary>
    public static JsonNode? ResolvePointer(JsonNode? root, string pointer)
    {
        if (root is null || pointer.Length == 0)
        {
            return root;
        }

        var current = root;
        foreach (var raw in pointer.Split('/').Skip(1))
        {
            var segment = raw.Replace("~1", "/").Replace("~0", "~");
            current = current switch
            {
                JsonObject obj => obj.TryGetPropertyValue(segment, out var child) ? child : null,
                JsonArray arr when int.TryParse(segment, out var index) && index >= 0 && index < arr.Count => arr[index],
                _ => null,
            };

            if (current is null)
            {
                return null;
            }
        }

        return current;
    }
}
