using System.CommandLine;
using OpenStore.Tools.Commands;

// OpenStore 工程门禁工具入口。
// 契约（docs/03-后端设计.md §9.1 步骤 9/10，脚本按退出码判定）：
//   dotnet run --project tools/OpenStore.Tools -- check-templates
//   dotnet run --project tools/OpenStore.Tools -- check-themes
// 全部通过 → 打印 `OK <集合>: N`，退出码 0；
// 任一失败 → 逐条打印 `<相对路径> <JSON Pointer>: <错误码> <说明>` + `FAILED: n error(s)` + `GATE FAILED: <规则名>`，退出码 1。

var rootCommand = new RootCommand("OpenStore 工程门禁工具：行业模板与主题包的 schema / 引用完整性校验");

rootCommand.Add(CreateCheckTemplatesCommand());
rootCommand.Add(CreateCheckThemesCommand());

return rootCommand.Parse(args).Invoke();

static Command CreateCheckTemplatesCommand()
{
    var rootOption = new Option<string?>("--root")
    {
        Description = "仓库根目录（含 schema/ 的目录）。默认从当前工作目录与程序目录逐级向上探测。",
    };

    var command = new Command(
        "check-templates",
        "校验 assets/industry-templates/*/template.json：schema、i18n 键覆盖率、引用完整性、变体轴作用域、默认主题存在性");

    command.Options.Add(rootOption);
    command.SetAction(parseResult => CheckTemplatesCommand.Run(parseResult.GetValue(rootOption)));
    return command;
}

static Command CreateCheckThemesCommand()
{
    var rootOption = new Option<string?>("--root")
    {
        Description = "仓库根目录（含 schema/ 的目录）。默认从当前工作目录与程序目录逐级向上探测。",
    };

    var command = new Command(
        "check-themes",
        "校验 themes/*：manifest schema、formatVersion、layouts 是否受宿主支持、blocks 是否在 editable.schema.json 声明、tokens 键集一致性");

    command.Options.Add(rootOption);
    command.SetAction(parseResult => CheckThemesCommand.Run(parseResult.GetValue(rootOption)));
    return command;
}
