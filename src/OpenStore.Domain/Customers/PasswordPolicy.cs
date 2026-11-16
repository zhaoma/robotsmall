namespace OpenStore.Domain.Customers;

/// <summary>
/// 密码策略（后端设计 §6.7：12–128 字符；不得纯数字；不得在 Top-1000 弱口令集合）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么放在 Domain 而不是 Api 的验证器里</b>：这条规则要同时约束三条写路径——<c>change-password</c>、
/// 将来的安装向导（§6.8 创建首个 Admin）、管理员重置他人密码。放在端点里，三条路径会各写一遍，
/// 而"哪一条忘了校验"没有任何门禁能发现（弱口令账号一旦建出来，后面所有审计都是在那之上做的）。
/// 放 Domain 后它是纯函数，域单测直接覆盖，且不依赖任何第三方包。
/// </para>
/// <para>
/// <b>返回违规枚举而不是异常、也不是中文文案</b>：一次提交可能同时违反多条（8 位纯数字同时是"太短"和"纯数字"），
/// 只抛第一条会让用户改一次被拒一次；文案属于表现层（本项目错误消息要跟 <c>openapi</c> 的
/// <c>errors.newPassword</c> 数组对齐），Domain 只给"违反了哪条"。
/// </para>
/// <para>
/// <b>弱口令判定是"集合 + 词根"两道，不是只有一道词表</b>：§6.7 写的是"不得在 Top-1000 弱口令集合"，
/// 但真实弱口令是"词根 + 装饰"——<c>Password123456!</c> 未必出现在任何一份 Top 列表里，
/// 它照样是第一小时就被撞开的密码。因此除集合判等外再加一条词根子串规则。这是<b>比契约更严</b>，
/// 已登记在 <c>docs/03</c> 的偏差表里，不是静默加码。
/// </para>
/// <para>
/// <b>名单是内置常量而非配置文件</b>：弱口令表是"越改越安全、不改也不会变坏"的静态数据，
/// 做成可配置项的唯一后果是某个部署把它指到空文件，然后策略静默变成只查长度。
/// 要替换整份名单，改 <see cref="WeakListRaw"/> 一个常量即可（编译期可见、评审可 diff）。
/// </para>
/// </remarks>
public static class PasswordPolicy
{
    /// <summary>最小长度（§6.7）。</summary>
    public const int MinLength = 12;

    /// <summary>最大长度（§6.7）。上界不是防暴力破解，而是防 PBKDF2 被超长输入拖垮（DoS）。</summary>
    public const int MaxLength = 128;

    /// <summary>内置弱口令基线（空格分隔，加载时拆成集合）。</summary>
    /// <remarks>
    /// 覆盖四类高频来源：纯数字连号、<c>password</c> 系列、键盘序、人名/品牌/流行词。
    /// <b>它是基线而不是"完整 Top-1000"</b>：真实 Top-1000 名单是外部数据资产（许可与体积都不适合进仓库），
    /// 因此本表按同样口径人工覆盖最高频的部分，并由 <see cref="WeakRoots"/> 的词根规则兜住"词根+装饰"那一半。
    /// 缺口已登记在 <c>docs/03</c>。
    /// </remarks>
    private const string WeakListRaw =
        "123456 1234567 12345678 123456789 1234567890 12345 1234 111111 000000 654321 666666 888888 999999 " +
        "121212 112233 123123 123321 147258 159753 5201314 1314520 123456a 123qwe 1q2w3e4r 1qaz2wsx q1w2e3r4 " +
        "password password1 password12 password123 password1234 password12345 password! password@123 passw0rd " +
        "p@ssw0rd p@ssword pass1234 passwd pa55word password123! password1234! password12345! " +
        "qwerty qwerty1 qwerty12 qwerty123 qwerty1234 qwertyuiop qwertyui asdfgh asdfghjkl zxcvbnm zxcvbnm123 " +
        "1qaz2wsx3edc qazwsx qazwsxedc zaq12wsx asdf1234 asdfasdf " +
        "abc123 abc1234 abcd1234 a1b2c3d4 aa123456 a123456 a1234567 aaa111 abcdef abcdefg abcdefgh " +
        "iloveyou iloveyou1 lovely loveme forever princess sunshine monkey dragon master shadow superman batman " +
        "football baseball basketball soccer hockey whatever trustno1 letmein letmein1 welcome welcome1 welcome123 " +
        "admin admin123 admin1234 administrator root toor system manager manage secret private company " +
        "changeme changeme123 changeit change123 letmein123 test test123 test1234 testing guest default " +
        "temp1234 temporary temp123456 " +
        "openstore openstore1 store store123 shop shop123 ecommerce onlinestore " +
        "michael jennifer jordan hunter bailey charlie donald soccer1 freedom ginger hammer summer winter spring " +
        "autumn january february march april june july august september october november december " +
        "hello hello123 hello1234 helloworld hellothere goodluck nothing nobody something anything " +
        "starwars pokemon naruto minecraft linux ubuntu windows apple google amazon facebook twitter " +
        "money money123 business business1 invoice orders payment payments customer customers " +
        "asdf jkl qwer poiu lkjh mnbv vbnm " +
        "1234qwer 1234qwerty 4321 qwe123 qweasd qweasdzxc qweqwe qwertyuiop123 " +
        "love1234 loveyou lover123 baby1234 angel1234 devil1234 god12345 " +
        "aaaaaa aaaaaa11 aaaaaaaa aaaaaaaa11 " +
        "11111111 22222222 33333333 44444444 55555555 77777777 88888888 99999999 00000000 " +
        "2020 2021 2022 2023 2024 2025 2026 2019 2018 2017";

    /// <summary>
    /// 弱口令词根：命中即拒（不区分大小写，子串匹配）。
    /// </summary>
    /// <remarks>
    /// 每条都必须在 5 字符以上：短词根（<c>abc</c>、<c>qwe</c>、<c>123</c>）会把正常密码误伤到不可用，
    /// 而误伤的用户反应是"再换个更简单的"，反而降级。
    /// </remarks>
    private static readonly string[] WeakRoots =
    [
        "password", "passwd", "passw0rd", "qwerty", "letmein", "welcome", "iloveyou", "trustno1",
        "changeme", "changeit", "administrator", "admin123", "root123", "abc123", "123456", "654321",
        "qweasd", "asdfgh", "zxcvbn", "1q2w3e", "1qaz2ws", "qazwsx", "openstore", "123qwe",
    ];

    private static readonly HashSet<string> WeakList = BuildWeakList();

    /// <summary>
    /// 校验密码。返回空数组表示通过。
    /// </summary>
    /// <remarks>
    /// 全部收集而不是首条即返：前端拿到的 <c>errors.newPassword</c> 是一次性提示，
    /// 逐条返回会让用户在同一张表单上反复试错。
    /// </remarks>
    public static IReadOnlyList<PasswordViolation> Validate(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return [PasswordViolation.TooShort];
        }

        var violations = new List<PasswordViolation>(3);

        if (password.Length < MinLength)
        {
            violations.Add(PasswordViolation.TooShort);
        }

        if (password.Length > MaxLength)
        {
            violations.Add(PasswordViolation.TooLong);
        }

        // 纯数字单列一条：它是撞库第一发子弹，且用户对自己"用了 12 位数字"往往很有信心
        if (IsAllDigits(password))
        {
            violations.Add(PasswordViolation.AllDigits);
        }

        if (IsSingleRepeatedCharacter(password))
        {
            violations.Add(PasswordViolation.SingleRepeatedCharacter);
        }

        if (WeakList.Contains(password))
        {
            violations.Add(PasswordViolation.WeakListEntry);
        }

        if (ContainsWeakRoot(password))
        {
            violations.Add(PasswordViolation.WeakRoot);
        }

        return violations;
    }

    /// <summary>是否通过策略。</summary>
    public static bool IsValid(string? password) => Validate(password).Count == 0;

    /// <summary>内置弱口令条数（启动日志与文档引用它，避免"文档说 1000、实际 200"的静默偏差）。</summary>
    public static int WeakListCount => WeakList.Count;

    private static bool IsAllDigits(string value)
    {
        foreach (var ch in value)
        {
            if (ch is < '0' or > '9')
            {
                return false;
            }
        }

        return value.Length > 0;
    }

    /// <summary>
    /// 全部字符相同（<c>aaaaaaaaaaaa</c>）。它既不是"纯数字"也不在任何名单里，
    /// 但熵只有 log2(94)≈6.5 bit —— 比 6 位随机还弱。
    /// </summary>
    private static bool IsSingleRepeatedCharacter(string value)
    {
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] != value[0])
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsWeakRoot(string value)
    {
        foreach (var root in WeakRoots)
        {
            if (value.Contains(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> BuildWeakList()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in WeakListRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(entry);
        }

        return set;
    }
}

/// <summary>
/// 密码策略违规项。<b>数值一旦发布不可改</b>——它只在本进程内流转（不进 API 响应），
/// 但改动会让域测试的失败信息指向错的规则。
/// </summary>
public enum PasswordViolation
{
    /// <summary>长度不足 <see cref="PasswordPolicy.MinLength"/>。</summary>
    TooShort = 0,

    /// <summary>长度超过 <see cref="PasswordPolicy.MaxLength"/>。</summary>
    TooLong = 1,

    /// <summary>纯数字。</summary>
    AllDigits = 2,

    /// <summary>全部字符相同。</summary>
    SingleRepeatedCharacter = 3,

    /// <summary>命中内置弱口令集合（整串判等）。</summary>
    WeakListEntry = 4,

    /// <summary>包含弱口令词根（子串匹配，如 <c>Password123456!</c>）。</summary>
    WeakRoot = 5,
}
