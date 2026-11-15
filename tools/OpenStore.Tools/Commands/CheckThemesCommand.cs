using System.Globalization;
using System.Text.Json.Nodes;
using Json.Schema;
using OpenStore.Tools.Validation;

namespace OpenStore.Tools.Commands;

/// <summary>
/// check-themes：主题包自检（docs/03-后端设计.md §9.1 步骤 10）。
///
/// 五件事：
///   ① manifest 对 schema/theme.manifest.schema.json 校验（formatVersion 必填）
///   ② blocks[] ⊆ 同主题 editable.schema.json 的区块白名单（BLOCK_NOT_DECLARED）
///   ③ layouts[] ⊆ 宿主 LayoutKey 全集（LAYOUT_NOT_SUPPORTED；M0 无前端可反射，常量写死在 HostContract）
///   ④ tokens 键为点分小写，且键集与 editable.schema.json 的 properties 完全一致（TOKEN_KEY_MISMATCH）
///   ⑤ formatVersion ∈ 宿主支持范围（FORMAT_VERSION_UNSUPPORTED）
///
/// 另外兼容单一真源形态：manifest 只声明 tokensSchema（JSON Schema，默认值 + 白名单同源）时，
/// 键集取它的 properties、默认值取 properties[key].default，跳过 tokens.json 比对。
/// </summary>
internal static class CheckThemesCommand
{
    public static int Run(string? rootOption)
    {
        var repoRoot = RepoRoot.Resolve(rootOption);
        if (repoRoot is null)
        {
            Console.WriteLine("check-themes: repository root not found (expected schema/template.schema.json); pass --root <path>");
            return 1;
        }

        var run = new CheckRun();
        var manifestSchemaPath = Path.Combine(repoRoot, "schema", "theme.manifest.schema.json");
        var manifestSchemaText = File.ReadAllText(manifestSchemaPath);
        var manifestSchema = SchemaChecks.LoadOrThrow(manifestSchemaPath);
        var manifestSchemaRoot = JsonNode.Parse(manifestSchemaText)!;

        var themesRoot = Path.Combine(repoRoot, "themes");
        var directories = Directory.Exists(themesRoot)
            ? Directory.GetDirectories(themesRoot)
            : Array.Empty<string>();
        Array.Sort(directories, StringComparer.Ordinal);

        var themeCount = 0;
        foreach (var directory in directories)
        {
            var manifestFile = Path.Combine(directory, "theme.manifest.json");
            if (!File.Exists(manifestFile))
            {
                continue;
            }

            themeCount++;
            CheckOne(repoRoot, directory, manifestFile, manifestSchema, manifestSchemaRoot, run);
        }

        return run.Finish("themes", themeCount, "themes");
    }

    private static void CheckOne(
        string repoRoot,
        string themeDirectory,
        string manifestFile,
        JsonSchema manifestSchema,
        JsonNode manifestSchemaRoot,
        CheckRun run)
    {
        var themeId = Path.GetFileName(themeDirectory);
        var manifestRelative = RepoRoot.Relative(repoRoot, manifestFile);

        var manifest = JsonIo.TryLoad(manifestFile, manifestRelative, run);
        if (manifest is null)
        {
            return;
        }

        // ---- ① schema ----
        SchemaChecks.Validate(manifestSchema, manifest, manifestRelative, run);
        SchemaChecks.ValidateRequired(manifest, manifestSchemaRoot, manifestRelative, run);

        if (manifest.At("id")?.Str() is { } declaredId && !string.Equals(declaredId, themeId, StringComparison.Ordinal))
        {
            run.Add(
                ErrorCodes.SchemaInvalid,
                manifestRelative,
                "/id",
                $"manifest id \"{declaredId}\" does not match its directory \"themes/{themeId}\"");
        }

        // ---- ⑤ formatVersion ----
        if (manifest.At("formatVersion")?.Int() is { } formatVersion
            && !HostContract.SupportedFormatVersions.Contains(formatVersion.ToString(CultureInfo.InvariantCulture)))
        {
            run.Add(
                ErrorCodes.FormatVersionUnsupported,
                manifestRelative,
                "/formatVersion",
                $"theme formatVersion {formatVersion} is not supported by this host (supports: {string.Join(", ", HostContract.SupportedFormatVersions)})");
        }

        // ---- ③ layouts ----
        if (manifest.At("layouts") is JsonArray layouts)
        {
            for (var i = 0; i < layouts.Count; i++)
            {
                if (layouts[i].Str() is not { } layoutKey)
                {
                    continue;
                }

                if (!HostContract.LayoutKeys.Contains(layoutKey, StringComparer.Ordinal))
                {
                    run.Add(
                        ErrorCodes.LayoutNotSupported,
                        manifestRelative,
                        $"/layouts/{i}",
                        $"layout \"{layoutKey}\" is not a supported LayoutKey (host supports: {string.Join(", ", HostContract.LayoutKeys)})");
                }
            }
        }

        // ---- 白名单来源：editable.schema.json（双文件）或 tokens.schema.json（单一真源）----
        var editableRelative = manifest.At("editableSchema")?.Str();
        var tokensSchemaRelative = manifest.At("tokensSchema")?.Str();
        var whitelistRelativePath = editableRelative ?? tokensSchemaRelative;

        if (whitelistRelativePath is null)
        {
            run.Add(
                ErrorCodes.SchemaInvalid,
                manifestRelative,
                string.Empty,
                "manifest declares neither editableSchema nor tokensSchema; the editable whitelist is unknown");
            return;
        }

        var whitelistFile = Path.Combine(themeDirectory, whitelistRelativePath);
        if (!File.Exists(whitelistFile))
        {
            run.Add(
                ErrorCodes.SchemaInvalid,
                manifestRelative,
                manifest.At("editableSchema") is null ? "/tokensSchema" : "/editableSchema",
                $"declared whitelist file \"{whitelistRelativePath}\" does not exist");
            return;
        }

        var whitelistRelative = RepoRoot.Relative(repoRoot, whitelistFile);
        var whitelistRoot = JsonIo.TryLoad(whitelistFile, whitelistRelative, run);
        var whitelist = ThemeAssets.ReadWhitelist(whitelistRoot, whitelistRelative, run);
        var whitelistFileName = Path.GetFileName(whitelistFile);

        // ---- ② blocks 必须在 editable.schema.json 里声明 ----
        if (manifest.At("blocks") is JsonArray blocks && whitelist.BlockKeys.Count > 0)
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                if (blocks[i].Str() is not { } blockKey)
                {
                    continue;
                }

                if (!whitelist.BlockKeys.Contains(blockKey, StringComparer.Ordinal))
                {
                    run.Add(
                        ErrorCodes.BlockNotDeclared,
                        manifestRelative,
                        $"/blocks/{i}",
                        $"{themeId}: block \"{blockKey}\" not declared in {whitelistFileName}");
                }
            }
        }

        // ---- ④ tokens 键形态与键集一致 ----
        var tokensRelative = manifest.At("tokens")?.Str();
        List<KeyValuePair<string, JsonNode?>> tokens;
        string tokensRelativeForDiagnostics;

        if (tokensRelative is not null)
        {
            var tokensFile = Path.Combine(themeDirectory, tokensRelative);
            tokensRelativeForDiagnostics = RepoRoot.Relative(repoRoot, tokensFile);
            if (!File.Exists(tokensFile))
            {
                run.Add(
                    ErrorCodes.SchemaInvalid,
                    manifestRelative,
                    "/tokens",
                    $"declared tokens file \"{tokensRelative}\" does not exist");
                return;
            }

            tokens = ThemeAssets.ReadTokenFile(tokensFile, tokensRelativeForDiagnostics, run);
        }
        else if (tokensSchemaRelative is not null)
        {
            // 单一真源：默认值来自 schema 的 default，键集天然一致，无需比对
            tokensRelativeForDiagnostics = RepoRoot.Relative(repoRoot, whitelistFile);
            tokens = ThemeAssets.ReadDefaultsFromSchema(whitelistFile, tokensRelativeForDiagnostics, run);
        }
        else
        {
            run.Add(
                ErrorCodes.SchemaInvalid,
                manifestRelative,
                "/tokens",
                "manifest declares no tokens file; default token values are unavailable");
            return;
        }

        var declaredKeys = whitelist.TokenKeys.ToHashSet(StringComparer.Ordinal);
        var actualKeys = tokens.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var (key, _) in tokens)
        {
            if (!ThemeAssets.IsWellFormedTokenKey(key))
            {
                run.Add(
                    ErrorCodes.TokenKeyMismatch,
                    tokensRelativeForDiagnostics,
                    JsonIo.Child(string.Empty, key),
                    $"token key \"{key}\" must be dotted lowercase matching {HostContract.TokenKeyPattern}");
            }
        }

        if (tokensRelative is not null)
        {
            foreach (var missing in declaredKeys.Except(actualKeys).OrderBy(key => key, StringComparer.Ordinal))
            {
                run.Add(
                    ErrorCodes.TokenKeyMismatch,
                    tokensRelativeForDiagnostics,
                    string.Empty,
                    $"token key \"{missing}\" is declared in {whitelistFileName} but missing from {Path.GetFileName(tokensRelativeForDiagnostics)}");
            }

            foreach (var extra in actualKeys.Except(declaredKeys).OrderBy(key => key, StringComparer.Ordinal))
            {
                run.Add(
                    ErrorCodes.TokenKeyMismatch,
                    tokensRelativeForDiagnostics,
                    JsonIo.Child(string.Empty, extra),
                    $"token key \"{extra}\" is not declared in {whitelistFileName} (undeclared tokens are ignored by the host, so the merchant would edit a value that never renders)");
            }
        }

        ThemeAssets.ValidateTokenValues(whitelistRoot, whitelistRelative, tokens, run);
    }
}
