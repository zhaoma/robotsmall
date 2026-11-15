using System.Text;

namespace OpenStore.ArchitectureTests;

/// <summary>
/// 文本型门禁的源码预处理。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须剥注释</b>：本仓库在 M0 实际踩过两次——<c>Program.cs</c> 的注释里写了
/// ".SaveChangesAsync"（为了说明这条规则），<c>ProductAdminEndpoints.cs</c> 的注释里写了
/// "裸 .RequireAuthorization()"（为了说明禁止什么），结果两条门禁都把<b>文档</b>判成违规。
/// 一个"写下规则就违规"的门禁会迫使团队把规则从代码里删除，最终只剩门禁本身还记得它。
/// </para>
/// <para>
/// 只剥注释，不剥字符串字面量内容：字符串里的 <c>//</c> 不会被当成注释开始符
/// （字面量整体原样拷贝），因此不会出现"漏扫半行"的假绿。
/// </para>
/// </remarks>
internal static class SourceText
{
    public static string StripComments(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var output = new StringBuilder(source.Length);
        var i = 0;

        while (i < source.Length)
        {
            var current = source[i];

            // 行注释：跳到行尾（保留换行，行号不漂）
            if (current == '/' && Peek(source, i + 1) == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            // 块注释：跳到 */，内部的换行照样输出
            if (current == '/' && Peek(source, i + 1) == '*')
            {
                i += 2;
                while (i < source.Length && !(source[i] == '*' && Peek(source, i + 1) == '/'))
                {
                    if (source[i] == '\n')
                    {
                        output.Append('\n');
                    }

                    i++;
                }

                i = Math.Min(i + 2, source.Length);
                continue;
            }

            // 原始字符串字面量 """..."""
            if (current == '"' && Peek(source, i + 1) == '"' && Peek(source, i + 2) == '"')
            {
                var end = source.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                var stop = end < 0 ? source.Length : end + 3;
                output.Append(source, i, stop - i);
                i = stop;
                continue;
            }

            // 逐字字符串 @"..."（内部 "" 是转义的单引号）
            if (current == '@' && Peek(source, i + 1) == '"')
            {
                var j = i + 2;
                while (j < source.Length)
                {
                    if (source[j] == '"')
                    {
                        if (Peek(source, j + 1) == '"')
                        {
                            j += 2;
                            continue;
                        }

                        j++;
                        break;
                    }

                    j++;
                }

                output.Append(source, i, Math.Min(j, source.Length) - i);
                i = j;
                continue;
            }

            // 普通字符串与字符字面量：连同转义整体拷贝
            if (current is '"' or '\'')
            {
                var quote = current;
                var j = i + 1;
                while (j < source.Length)
                {
                    if (source[j] == '\\')
                    {
                        j += 2;
                        continue;
                    }

                    if (source[j] == quote || source[j] == '\n')
                    {
                        j++;
                        break;
                    }

                    j++;
                }

                output.Append(source, i, Math.Min(j, source.Length) - i);
                i = j;
                continue;
            }

            output.Append(current);
            i++;
        }

        return output.ToString();
    }

    private static char Peek(string source, int index) => index < source.Length ? source[index] : '\0';
}
