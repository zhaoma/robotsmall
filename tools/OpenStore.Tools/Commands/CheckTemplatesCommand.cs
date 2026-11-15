using System.Globalization;
using System.Text.Json.Nodes;
using Json.Schema;
using OpenStore.Tools.Validation;

namespace OpenStore.Tools.Commands;

/// <summary>
/// check-templates：行业模板自检（docs/03-后端设计.md §9.1 步骤 9）。
///
/// 五件事，缺一不可：
///   1. schema       —— template.json 对 schema/template.schema.json
///   2. i18n 覆盖率   —— 以默认语种为基准键集，其他语种 &lt; 90% 即 KEY_COVERAGE_LOW
///   3. 引用完整性    —— categoryKey / attributeCode / attributeSet / 每个 i18n 键都必须存在
///   4. variantAxes  —— 轴必须是本分类 attributeSet 成员且 variantAxis=true
///   5. themeId      —— defaults.themeId 必须在 themes/ 下存在；themeTokens 预设键必须是该主题的可编辑键
/// </summary>
internal static class CheckTemplatesCommand
{
    /// <summary>i18n 覆盖率下限：低于它就等于"这个语种没做完"，上线后会露出中文兜底。</summary>
    private const double MinKeyCoverage = 0.90;

    private const string FallbackBaseLocale = "zh";

    private const int MaxListedKeys = 20;

    public static int Run(string? rootOption)
    {
        var repoRoot = RepoRoot.Resolve(rootOption);
        if (repoRoot is null)
        {
            Console.WriteLine("check-templates: repository root not found (expected schema/template.schema.json); pass --root <path>");
            return 1;
        }

        var run = new CheckRun();
        var schemaPath = Path.Combine(repoRoot, "schema", "template.schema.json");
        var schemaText = File.ReadAllText(schemaPath);
        var schema = SchemaChecks.LoadOrThrow(schemaPath);
        var schemaRoot = JsonNode.Parse(schemaText)!;

        var templatesRoot = Path.Combine(repoRoot, "assets", "industry-templates");
        var directories = Directory.Exists(templatesRoot)
            ? Directory.GetDirectories(templatesRoot)
            : Array.Empty<string>();
        Array.Sort(directories, StringComparer.Ordinal);

        var templateCount = 0;
        foreach (var directory in directories)
        {
            var templateFile = Path.Combine(directory, "template.json");
            if (!File.Exists(templateFile))
            {
                continue;
            }

            var templateRelativeDir = RepoRoot.Relative(templatesRoot, directory);
            var relative = RepoRoot.Relative(templatesRoot, templateFile);
            var template = JsonIo.TryLoad(templateFile, relative, run);
            if (template is null)
            {
                continue;
            }

            templateCount++;
            CheckOne(repoRoot, directory, templateRelativeDir, relative, template, schema, schemaRoot, run);
        }

        return run.Finish("templates", templateCount, "templates");
    }

    private static void CheckOne(
        string repoRoot,
        string templateDirectory,
        string templateRelativeDir,
        string relative,
        JsonNode template,
        JsonSchema schema,
        JsonNode schemaRoot,
        CheckRun run)
    {
        // ---- ① schema ----
        SchemaChecks.Validate(schema, template, relative, run);
        SchemaChecks.ValidateRequired(template, schemaRoot, relative, run);

        if (template.At("formatVersion")?.Int() is { } formatVersion
            && !HostContract.SupportedFormatVersions.Contains(formatVersion.ToString(CultureInfo.InvariantCulture)))
        {
            run.Add(
                ErrorCodes.FormatVersionUnsupported,
                relative,
                "/formatVersion",
                $"formatVersion {formatVersion} is not supported by this host (supports: {string.Join(", ", HostContract.SupportedFormatVersions)})");
        }

        var defaults = template.At("defaults");
        var baseLocale = defaults?.At("locale")?.Str() ?? FallbackBaseLocale;

        // ---- ② i18n 覆盖率 ----
        var localeKeys = LoadI18n(templateDirectory, templateRelativeDir, run);
        var baseKeys = localeKeys.TryGetValue(baseLocale, out var declared) ? declared : null;
        if (baseKeys is null)
        {
            run.Add(
                ErrorCodes.KeyCoverageLow,
                $"{templateRelativeDir}/i18n/{baseLocale}.json",
                string.Empty,
                $"base locale file \"{baseLocale}.json\" is missing; coverage cannot be measured");
        }
        else
        {
            ReportCoverage(localeKeys, baseLocale, baseKeys, templateRelativeDir, run);
        }

        void CheckI18nKey(string? key, string pointer, string what)
        {
            if (key is null || baseKeys is null)
            {
                return;
            }

            if (!baseKeys.Contains(key))
            {
                run.Add(
                    ErrorCodes.RefNotFound,
                    relative,
                    pointer,
                    $"{what} \"{key}\" not found in i18n/{baseLocale}.json");
            }
        }

        // ---- ③ 属性定义（后续所有引用检查的基础） ----
        var attributeByCode = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (template.At("attributes") is JsonArray attributes)
        {
            for (var i = 0; i < attributes.Count; i++)
            {
                var attribute = attributes[i];
                var pointer = JsonIo.Child("/attributes", i);
                if (attribute?.At("code")?.Str() is not { } code)
                {
                    continue;
                }

                if (!attributeByCode.TryAdd(code, attribute))
                {
                    run.Add(ErrorCodes.SchemaInvalid, relative, JsonIo.Child(pointer, "code"), $"duplicate attribute code \"{code}\"");
                }

                // dataType 与 options/unit 的联动（写成显式检查而不是 schema 里的 if/then：
                // 门禁的措辞与位置必须由自己决定，不能依赖校验库对条件分支的输出格式）
                var dataType = attribute.At("dataType")?.Str();
                var hasOptions = attribute.At("options") is JsonArray;

                if (dataType == "enum" && !hasOptions)
                {
                    run.Add(ErrorCodes.SchemaInvalid, relative, JsonIo.Child(pointer, "options"), $"attribute \"{code}\" has dataType=\"{dataType}\" but declares no options");
                }

                if (dataType == "number" && attribute.At("unit")?.Str() is null)
                {
                    run.Add(ErrorCodes.SchemaInvalid, relative, JsonIo.Child(pointer, "unit"), $"attribute \"{code}\" has dataType=\"number\" but no unit (unit lives in the column, not in the value)");
                }

                if (hasOptions && dataType is not null && dataType != "enum")
                {
                    run.Add(ErrorCodes.SchemaInvalid, relative, JsonIo.Child(pointer, "options"), $"attribute \"{code}\" declares options but dataType is \"{dataType}\" (only enum takes options)");
                }

                CheckI18nKey(attribute.At("labelKey")?.Str(), JsonIo.Child(pointer, "labelKey"), "attribute labelKey");

                if (attribute.At("options") is not JsonArray options)
                {
                    continue;
                }

                var optionCodes = new HashSet<string>(StringComparer.Ordinal);
                for (var j = 0; j < options.Count; j++)
                {
                    var option = options[j];
                    var optionPointer = JsonIo.Child(pointer, "options") + $"/{j}";
                    if (option?.At("code")?.Str() is { } optionCode && !optionCodes.Add(optionCode))
                    {
                        run.Add(
                            ErrorCodes.SchemaInvalid,
                            relative,
                            JsonIo.Child(optionPointer, "code"),
                            $"duplicate option code \"{optionCode}\" in attribute \"{code}\"");
                    }

                    CheckI18nKey(option.At("labelKey")?.Str(), JsonIo.Child(optionPointer, "labelKey"), $"option labelKey of attribute \"{code}\"");
                }
            }
        }

        // ---- 属性集：成员必须存在 ----
        var setMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (template.At("attributeSets") is JsonArray attributeSets)
        {
            for (var i = 0; i < attributeSets.Count; i++)
            {
                var set = attributeSets[i];
                var pointer = JsonIo.Child("/attributeSets", i);
                if (set?.At("code")?.Str() is not { } setCode)
                {
                    continue;
                }

                if (!setMembers.TryAdd(setCode, new HashSet<string>(StringComparer.Ordinal)))
                {
                    run.Add(ErrorCodes.SchemaInvalid, relative, JsonIo.Child(pointer, "code"), $"duplicate attributeSet code \"{setCode}\"");
                }

                CheckI18nKey(set.At("labelKey")?.Str(), JsonIo.Child(pointer, "labelKey"), "attributeSet labelKey");

                if (set.At("attributes") is not JsonArray members)
                {
                    continue;
                }

                for (var k = 0; k < members.Count; k++)
                {
                    if (members[k].Str() is not { } memberCode)
                    {
                        continue;
                    }

                    if (!attributeByCode.ContainsKey(memberCode))
                    {
                        run.Add(
                            ErrorCodes.RefNotFound,
                            relative,
                            $"{pointer}/attributes/{k}",
                            $"attributeSet \"{setCode}\" references undeclared attribute \"{memberCode}\"");
                    }

                    setMembers[setCode].Add(memberCode);
                }
            }
        }

        // ---- 分类：key 全集、attributeSet、父节点、文案 ----
        var categorySetByKey = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (template.At("categories") is JsonArray categories)
        {
            for (var i = 0; i < categories.Count; i++)
            {
                var category = categories[i];
                var pointer = JsonIo.Child("/categories", i);
                if (category?.At("key")?.Str() is not { } categoryKey)
                {
                    continue;
                }

                if (categorySetByKey.ContainsKey(categoryKey))
                {
                    run.Add(ErrorCodes.SchemaInvalid, relative, JsonIo.Child(pointer, "key"), $"duplicate category key \"{categoryKey}\"");
                }
                else
                {
                    categorySetByKey[categoryKey] = category.At("attributeSet")?.Str();
                }

                CheckI18nKey(category.At("labelKey")?.Str(), JsonIo.Child(pointer, "labelKey"), "category labelKey");

                if (category.At("attributeSet")?.Str() is { } setName && !setMembers.ContainsKey(setName))
                {
                    run.Add(
                        ErrorCodes.RefNotFound,
                        relative,
                        JsonIo.Child(pointer, "attributeSet"),
                        $"category \"{categoryKey}\" references undeclared attributeSet \"{setName}\"");
                }
            }

            // 第二遍：父节点 + 商品（此时分类 key 全集已完整，允许子分类先于父分类声明）
            for (var i = 0; i < categories.Count; i++)
            {
                var category = categories[i];
                var pointer = JsonIo.Child("/categories", i);
                if (category?.At("key")?.Str() is not { } categoryKey)
                {
                    continue;
                }

                if (category.At("parentKey")?.Str() is { } parentKey && !categorySetByKey.ContainsKey(parentKey))
                {
                    run.Add(
                        ErrorCodes.RefNotFound,
                        relative,
                        JsonIo.Child(pointer, "parentKey"),
                        $"category \"{categoryKey}\" references undeclared parentKey \"{parentKey}\"");
                }

                CheckProducts(category, pointer, categoryKey, attributeByCode, setMembers, relative, run, CheckI18nKey);
            }
        }

        // ---- 辅助定义（运费区/税率）的文案与语种 ----
        if (template.At("shippingZones") is JsonArray zones)
        {
            for (var i = 0; i < zones.Count; i++)
            {
                CheckI18nKey(zones[i]?.At("labelKey")?.Str(), $"/shippingZones/{i}/labelKey", "shippingZone labelKey");
            }
        }

        if (template.At("taxRules") is JsonArray taxRules)
        {
            for (var i = 0; i < taxRules.Count; i++)
            {
                CheckI18nKey(taxRules[i]?.At("labelKey")?.Str(), $"/taxRules/{i}/labelKey", "taxRule labelKey");
            }
        }

        // ---- ⑤ 默认主题 ----
        CheckThemeReference(repoRoot, relative, defaults, run);
    }

    private static void CheckProducts(
        JsonNode? category,
        string categoryPointer,
        string categoryKey,
        IReadOnlyDictionary<string, JsonNode?> attributeByCode,
        IReadOnlyDictionary<string, HashSet<string>> setMembers,
        string relative,
        CheckRun run,
        Action<string?, string, string> checkI18nKey)
    {
        if (category?.At("products") is not JsonArray products)
        {
            return;
        }

        var setName = category.At("attributeSet")?.Str();
        var members = setName is not null && setMembers.TryGetValue(setName, out var set) ? set : null;

        for (var j = 0; j < products.Count; j++)
        {
            var product = products[j];
            var pointer = $"{categoryPointer}/products/{j}";
            if (product?.At("key")?.Str() is not { } productKey)
            {
                continue;
            }

            checkI18nKey(product.At("nameKey")?.Str(), JsonIo.Child(pointer, "nameKey"), $"product \"{productKey}\" nameKey");

            // categoryKey 冗余字段（商品物理上嵌在分类下）：存在即必须与宿主分类一致，防搬运残留
            if (product.At("categoryKey")?.Str() is { } referencedCategory)
            {
                if (!string.Equals(referencedCategory, categoryKey, StringComparison.Ordinal))
                {
                    run.Add(
                        ErrorCodes.RefNotFound,
                        relative,
                        JsonIo.Child(pointer, "categoryKey"),
                        $"product \"{productKey}\" declares categoryKey \"{referencedCategory}\" but is nested under category \"{categoryKey}\"");
                }
            }

            // 商品级属性值：必须已定义，且属于本分类的属性集
            if (product.At("attributeCodes") is JsonArray attributeCodes)
            {
                for (var k = 0; k < attributeCodes.Count; k++)
                {
                    if (attributeCodes[k].Str() is not { } code)
                    {
                        continue;
                    }

                    var elementPointer = $"{pointer}/attributeCodes/{k}";
                    if (!attributeByCode.ContainsKey(code))
                    {
                        run.Add(ErrorCodes.RefNotFound, relative, elementPointer, $"product \"{productKey}\" references undeclared attribute \"{code}\"");
                    }
                    else if (members is not null && !members.Contains(code))
                    {
                        run.Add(
                            ErrorCodes.RefNotFound,
                            relative,
                            elementPointer,
                            $"product \"{productKey}\" uses attribute \"{code}\" which is not in attributeSet \"{setName}\" of category \"{categoryKey}\"");
                    }
                }
            }

            // ---- ④ 变体轴作用域 ----
            var axes = product.At("variantAxes") is JsonArray declaredAxes
                ? declaredAxes.Strings().ToList()
                : new List<string>();

            if (product.At("variantAxes") is JsonArray rawAxes)
            {
                for (var k = 0; k < rawAxes.Count; k++)
                {
                    if (rawAxes[k].Str() is not { } axis)
                    {
                        continue;
                    }

                    var axisPointer = $"{pointer}/variantAxes";

                    if (!attributeByCode.TryGetValue(axis, out var attribute))
                    {
                        run.Add(
                            ErrorCodes.RefNotFound,
                            relative,
                            axisPointer,
                            $"[{k}] product \"{productKey}\" declares variantAxis \"{axis}\" which is not a declared attribute");
                    }
                    else if (members is not null && !members.Contains(axis))
                    {
                        run.Add(
                            ErrorCodes.AxisScopeInvalid,
                            relative,
                            axisPointer,
                            $"[{k}] variantAxis \"{axis}\" of product \"{productKey}\" is not a member of attributeSet \"{setName}\" (category \"{categoryKey}\")");
                    }
                    else if (!IsVariantAxis(attribute))
                    {
                        run.Add(
                            ErrorCodes.AxisScopeInvalid,
                            relative,
                            axisPointer,
                            $"[{k}] variantAxis \"{axis}\" of product \"{productKey}\" is not declared variantAxis:true (scope must be \"variant\")");
                    }
                }
            }

            // variantOptions：键必须是已声明的轴，选项必须是该属性已定义的选项
            if (product.At("variantOptions") is JsonObject variantOptions)
            {
                foreach (var entry in variantOptions)
                {
                    var axisPointer = JsonIo.Child($"{pointer}/variantOptions", entry.Key);
                    if (!axes.Contains(entry.Key, StringComparer.Ordinal))
                    {
                        run.Add(
                            ErrorCodes.RefNotFound,
                            relative,
                            axisPointer,
                            $"variantOptions declares axis \"{entry.Key}\" which is not listed in variantAxes of product \"{productKey}\"");
                        continue;
                    }

                    if (!attributeByCode.TryGetValue(entry.Key, out var axisAttribute))
                    {
                        continue;
                    }

                    var knownOptions = axisAttribute.At("options") is JsonArray declaredOptions
                        ? declaredOptions.Select(option => option?.At("code")?.Str()).OfType<string>().ToHashSet(StringComparer.Ordinal)
                        : new HashSet<string>(StringComparer.Ordinal);

                    foreach (var optionCode in entry.Value.Strings())
                    {
                        if (knownOptions.Count > 0 && !knownOptions.Contains(optionCode))
                        {
                            run.Add(
                                ErrorCodes.RefNotFound,
                                relative,
                                axisPointer,
                                $"variantOption \"{optionCode}\" is not an option of attribute \"{entry.Key}\"");
                        }
                    }
                }
            }
        }
    }

    private static bool IsVariantAxis(JsonNode? attribute)
        => attribute.At("variantAxis") is JsonValue flag
           && flag.TryGetValue<bool>(out var isAxis)
           && isAxis
           && string.Equals(attribute.At("scope")?.Str(), "variant", StringComparison.Ordinal);

    private static Dictionary<string, HashSet<string>> LoadI18n(string templateDirectory, string templateRelativeDir, CheckRun run)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var i18nDirectory = Path.Combine(templateDirectory, "i18n");
        if (!Directory.Exists(i18nDirectory))
        {
            run.Add(
                ErrorCodes.KeyCoverageLow,
                $"{templateRelativeDir}/i18n",
                string.Empty,
                "i18n directory is missing; the template cannot be localized");
            return result;
        }

        var files = Directory.GetFiles(i18nDirectory, "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            var locale = Path.GetFileNameWithoutExtension(file);
            var relative = $"{templateRelativeDir}/i18n/{locale}.json";
            var node = JsonIo.TryLoad(file, relative, run);
            if (node is null)
            {
                continue;
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            JsonIo.Flatten(node, string.Empty, keys);
            result[locale] = keys;
        }

        return result;
    }

    private static void ReportCoverage(
        IReadOnlyDictionary<string, HashSet<string>> localeKeys,
        string baseLocale,
        HashSet<string> baseKeys,
        string templateRelativeDir,
        CheckRun run)
    {
        foreach (var (locale, keys) in localeKeys)
        {
            if (string.Equals(locale, baseLocale, StringComparison.Ordinal))
            {
                continue;
            }

            var missing = baseKeys.Except(keys).OrderBy(key => key, StringComparer.Ordinal).ToList();
            if (baseKeys.Count == 0)
            {
                continue;
            }

            var coverage = (baseKeys.Count - missing.Count) / (double)baseKeys.Count;
            if (coverage >= MinKeyCoverage)
            {
                continue;
            }

            var listed = string.Join(", ", missing.Take(MaxListedKeys));
            var suffix = missing.Count > MaxListedKeys ? $", …(+{missing.Count - MaxListedKeys})" : string.Empty;
            run.Add(
                ErrorCodes.KeyCoverageLow,
                $"{templateRelativeDir}/i18n/{locale}.json",
                string.Empty,
                $"coverage {coverage.ToString("0.0", CultureInfo.InvariantCulture)}% < {(MinKeyCoverage * 100).ToString("0", CultureInfo.InvariantCulture)}% of base locale \"{baseLocale}\"; missing {missing.Count}/{baseKeys.Count} key(s): {listed}{suffix}");
        }
    }

    private static void CheckThemeReference(string repoRoot, string relative, JsonNode? defaults, CheckRun run)
    {
        if (defaults?.At("themeId")?.Str() is not { } themeId)
        {
            // 缺失由 schema 的 required 报（V5 注入点），这里不重复报污染输出。
            return;
        }

        var themeDirectory = Path.Combine(repoRoot, "themes", themeId);
        if (!File.Exists(Path.Combine(themeDirectory, "theme.manifest.json")))
        {
            run.Add(
                ErrorCodes.ThemeNotFound,
                relative,
                "/defaults/themeId",
                $"theme \"{themeId}\" not found (expected themes/{themeId}/theme.manifest.json)");
            return;
        }

        if (defaults.At("themeTokens") is not JsonObject preset)
        {
            return;
        }

        var editableKeys = TryReadEditableTokenKeys(themeDirectory);
        if (editableKeys is null)
        {
            // 主题侧资产坏了由 check-themes 负责报；这里不越权输出。
            return;
        }

        foreach (var entry in preset)
        {
            if (!editableKeys.Contains(entry.Key))
            {
                run.Add(
                    ErrorCodes.TokenKeyMismatch,
                    relative,
                    JsonIo.Child("/defaults/themeTokens", entry.Key),
                    $"theme token preset \"{entry.Key}\" is not editable in theme \"{themeId}\" (absent from themes/{themeId}/editable.schema.json properties)");
            }
        }
    }

    /// <summary>静默读取主题可编辑 token 键集（模板命令不做主题侧诊断，避免两个命令重复报同一问题）。</summary>
    private static HashSet<string>? TryReadEditableTokenKeys(string themeDirectory)
    {
        var path = Path.Combine(themeDirectory, "editable.schema.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path))?.At("properties") is JsonObject properties
                ? properties.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal)
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
