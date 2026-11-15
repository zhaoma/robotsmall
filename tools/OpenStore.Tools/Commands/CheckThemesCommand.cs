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
///   ② blocksSchema 的 $defs.blockKey.enum 每一项必须有 $defs.blocks 里的 props schema（BLOCK_PROPS_MISSING）
///   ③ layouts[] ⊆ 宿主 LayoutKey 全集（LAYOUT_NOT_SUPPORTED；M0 无前端可反射，常量写死在 HostContract）
///   ④ tokensSchema 的 properties 键为点分小写，且默认值自身符合各自子 schema（TOKEN_KEY_MISMATCH / TOKEN_DEFAULT_MISSING）
///   ⑤ formatVersion ∈ 宿主支持范围（FORMAT_VERSION_UNSUPPORTED）
///
/// 两个真源、一个文件管一件事：
///   tokens.schema.json —— token 白名单 + 默认值 + admin 表单源（架构 §5.1.2 / 决策 W5）
///   blocks.schema.json —— 区块白名单 + 每个区块的 props schema
/// manifest 不再有 blocks[]（已删除）：它的唯一消费者是这条校验本身，而区块白名单的真源是 blocksSchema，
/// 于是"manifest 列了区块、schema 没列"这个错误只能由门禁自己制造，也就只该由门禁自己防。
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

        // ---- 白名单：tokensSchema（token 键集 + 默认值）----
        var tokensSchemaRelative = manifest.At("tokensSchema")?.Str();
        if (tokensSchemaRelative is null)
        {
            run.Add(
                ErrorCodes.SchemaInvalid,
                manifestRelative,
                "/tokensSchema",
                "manifest declares no tokensSchema; the token whitelist and default values are unknown");
            return;
        }

        var tokensSchemaFile = Path.Combine(themeDirectory, tokensSchemaRelative);
        if (!File.Exists(tokensSchemaFile))
        {
            run.Add(
                ErrorCodes.SchemaInvalid,
                manifestRelative,
                "/tokensSchema",
                $"declared tokensSchema file \"{tokensSchemaRelative}\" does not exist");
            return;
        }

        var tokensSchemaRelativeForDiagnostics = RepoRoot.Relative(repoRoot, tokensSchemaFile);
        var tokensSchemaRoot = JsonIo.TryLoad(tokensSchemaFile, tokensSchemaRelativeForDiagnostics, run);

        // 只为拿到"properties 为空"这条诊断：重复源下的键集比对已经不存在，
        // 但"一个 token 都没声明"仍然是个坏主题（商家进后台看不到任何可调项）。
        ThemeAssets.ReadTokenWhitelist(tokensSchemaRoot, tokensSchemaRelativeForDiagnostics, run);

        // ---- ② 每个声明可插入的区块，必须有 props schema ----
        // 方向是"枚举 → props"：反向（props 里有、枚举里没有）不报 —— 那是没人能插入的死 schema，
        // 没有用户可见症状，报它只会逼人删掉正在做的一半改动。
        var blocksSchemaRelative = manifest.At("blocksSchema")?.Str();
        if (blocksSchemaRelative is not null)
        {
            var blocksSchemaFile = Path.Combine(themeDirectory, blocksSchemaRelative);
            if (!File.Exists(blocksSchemaFile))
            {
                run.Add(
                    ErrorCodes.SchemaInvalid,
                    manifestRelative,
                    "/blocksSchema",
                    $"declared blocksSchema file \"{blocksSchemaRelative}\" does not exist");
                return;
            }

            var blocksSchemaRelativeForDiagnostics = RepoRoot.Relative(repoRoot, blocksSchemaFile);
            var blocksSchemaRoot = JsonIo.TryLoad(blocksSchemaFile, blocksSchemaRelativeForDiagnostics, run);
            var blockWhitelist = ThemeAssets.ReadBlockWhitelist(blocksSchemaRoot, blocksSchemaRelativeForDiagnostics, run);

            foreach (var blockKey in blockWhitelist.BlockKeys)
            {
                if (blockWhitelist.KeysWithProps.Contains(blockKey))
                {
                    continue;
                }

                run.Add(
                    ErrorCodes.BlockPropsMissing,
                    blocksSchemaRelativeForDiagnostics,
                    JsonIo.Child("/$defs/blocks", blockKey),
                    $"block \"{blockKey}\" is insertable ($defs.blockKey.enum) but has no props schema; the merchant can add it and then cannot configure anything, which reads as \"the theme is broken\"");
            }
        }

        // ---- ④ tokens 键形态，并校验默认值本身符合各自 schema ----
        var tokens = ThemeAssets.ReadDefaultsFromSchema(tokensSchemaFile, tokensSchemaRelativeForDiagnostics, run);

        foreach (var (key, _) in tokens)
        {
            if (!ThemeAssets.IsWellFormedTokenKey(key))
            {
                run.Add(
                    ErrorCodes.TokenKeyInvalid,
                    tokensSchemaRelativeForDiagnostics,
                    JsonIo.Child(string.Empty, key),
                    $"token key \"{key}\" must be dotted lowercase matching {HostContract.TokenKeyPattern}");
            }
        }

        // 这里刻意**没有**"白名单键集 vs 默认值键集"的比对：
        // 单一真源下两者是同一个 properties，比对恒等；而"某键没有 default"已由
        // ReadDefaultsFromSchema 报 TOKEN_DEFAULT_MISSING。再比一次只会让同一个问题
        // 出两个码，让人以为有两处要修。

        ThemeAssets.ValidateTokenValues(tokensSchemaRoot, tokensSchemaRelativeForDiagnostics, tokens, run);
    }
}
