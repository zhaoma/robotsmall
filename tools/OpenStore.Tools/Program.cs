using System.CommandLine;
using OpenStore.Tools.Commands;

// OpenStore 工具入口。三类子命令，退出码语义各不相同，不要混用：
//   门禁类（check-*）：契约见 docs/03-后端设计.md §9.1 步骤 9/10
//     dotnet run --project tools/OpenStore.Tools -- check-templates
//     dotnet run --project tools/OpenStore.Tools -- check-themes
//     全部通过 → 打印 `OK <集合>: N`，退出码 0；
//     任一失败 → 逐条打印 `<相对路径> <JSON Pointer>: <错误码> <说明>` + `FAILED: n error(s)` + `GATE FAILED: <规则名>`，退出码 1。
//   造数类（seed-bench）：0 成功 / 1 数据不一致或自证失败 / 2 用法错误或前置条件不满足（含"拒绝写受保护库"）。

var rootCommand = new RootCommand("OpenStore 工程工具：门禁校验（check-*）与 AC-12 基准数据生成（seed-bench）");

rootCommand.Add(CreateCheckTemplatesCommand());
rootCommand.Add(CreateCheckThemesCommand());
rootCommand.Add(CreateSeedBenchCommand());

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

static Command CreateSeedBenchCommand()
{
    var dbOption = new Option<string?>("--db")
    {
        Description = "目标 SQLite 库路径（必填）。文件名是 openstore.dev.db、或等于 appsettings 里配置的库时会拒绝并以 rc=2 退出。",
    };

    var tierOption = new Option<string?>("--tier")
    {
        Description = "数据档位：small（1,000 商品 / 5,000 变体，默认）或 standard（10,000 / 50,000 / 每分类 500）。",
    };

    var selfTestOption = new Option<bool>("--self-test")
    {
        Description = "只跑自证（幂等 / 确定性 / 缺 --db / 受保护库 / 非法 tier / 门禁解耦），在临时目录造数并清理，不碰任何真实库。",
    };

    var command = new Command(
        "seed-bench",
        "造 AC-12 的基准数据集：确定性主键、按 slug 幂等、打印规格与数据量指纹（docs/06 §4.1）");

    command.Options.Add(dbOption);
    command.Options.Add(tierOption);
    command.Options.Add(selfTestOption);
    command.SetAction(parseResult => SeedBenchCommand.Run(
        parseResult.GetValue(dbOption),
        parseResult.GetValue(tierOption),
        parseResult.GetValue(selfTestOption)));

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
        "校验 themes/*：manifest schema、formatVersion、layouts 是否受宿主支持、每个可插入区块是否都有 props schema、token 键形态与默认值");

    command.Options.Add(rootOption);
    command.SetAction(parseResult => CheckThemesCommand.Run(parseResult.GetValue(rootOption)));
    return command;
}
