#!/usr/bin/env bash
# scripts/check-tables.sh
#
# 硬门禁：docs 里的 Markdown 表格必须是**真正的表格**。
#
# 为什么需要它：表格坏掉时**没有任何东西会报错**。GFM 遇到断掉的表格不会警告，
# 它只是把那些行当普通段落渲染——于是"一份看起来在表里的内容"实际不在表里，
# 而且它长得仍然像表格（文本里全是竖线），评审时扫过去不会停。
#
# 这不是假想：同一族缺陷已经**两次成规模出现**。
#   第一次 docs/05 的 Q15 行：表头 5 列、正文 4 格 → 那一行整行降级为文本；
#   第二次本轮全仓扫描：00 / 03 / 04 / 06 共 4 处（表头被粘在段落行末、引用块内
#   4 行丢了 "> " 前缀、空行先把表截断再插引用块）。
# 按本项目的判据分工——**低频、语义相关的错误用评审治；高频、结构相关的错误用门禁治**——
# 这一族已经够"结构"也够"高频"，该进门禁。手工评审在这个位置的成本是"每次都要盯着看"，
# 而它恰恰是看漏也不会有人知道的那一类。
#
# ── 三条判据 ───────────────────────────────────────────────────────────────────
#   T1 孤行：一行以 "|" 开头，但**既不是某张表的表头、也不是同一引用层级内上一行的续行**
#      → 它一定被渲染成带竖线的普通文本。三种真实成因都收敛到这一条：
#        · 表头被粘在段落同一行的末尾（00 的形态：分隔行前面没有表头，表根本没起来）
#        · 空行先把表截断，后面又是表格行（04 / 06 的形态）
#        · 引用块里的行丢了 "> " 前缀（03 的形态：深度变了就不再是续行）
#   T2 列数：同一张表内每行的单元格数必须等于表头。多一格少一格都意味着**那一行不在表里**
#      （GFM 会按表头列数截断或补齐，多出来的内容静默消失）。
#   T3 围栏配对：围栏标记奇数 → 文件后半段全部被当成代码。这一条不查，"表格"和"代码"
#      的边界就是错的，T1/T2 会在错误的分区上跑。
#
# ── 为什么不会误伤（误伤的门禁最后一定会被关掉，所以这条必须写清楚）──────────────
#   1. **引用块内的表格（"> | a | b |"）是合法的**，深度按 ">" 个数算，同深度续行才算续行。
#   2. **代码块里的 "|" 是 shell 管道，不是表格**（`grep x | awk` / `jq '.a | .b'`）。
#      围栏状态内的行一律跳过——本仓 05 有三处这种行，误判过一次。
#   3. **引用块内的代码围栏（"> ```sh"）同样是围栏**。只认行首 ``` 会把 04 的
#      "> ```sh ... | awk ..." 判成孤行。这条是真踩过的假阳性。
#   4. 表格紧跟在围栏结束行之后不算孤立（下一行是分隔行 → 它是表头）。
#
# ── 扫描范围不含本脚本自身 ─────────────────────────────────────────────────────
#   默认只扫 docs/。判据命令不得把自己所在的位置纳入扫描范围：本文件里为自证写入了
#   若干**故意坏掉**的表格样本，若扫到自己，"检查自己"就永远命中——不报错、不漂移、
#   从出生就无信息。
#
# 用法:
#   ./scripts/check-tables.sh                 # 扫 docs/（默认）
#   ./scripts/check-tables.sh <dir> [...]     # 扫指定目录
#   ./scripts/check-tables.sh --list          # 导出识别到的每张表（文件:行 列数），供人审
#   ./scripts/check-tables.sh --self-test     # fail-before-pass 自证（1 正样本 + 5 注入）
# 退出码: 0 干净 / 1 有违规 / 2 环境问题
#
# 自证结论行契约（与同组脚本一致，最后一行必是二者之一）：
#   == 自证通过：<N> 项（<明细>）   → 0
#   == 自证失败：<N> 项不符合预期    → 1
# 自证若抛异常，刻意不打印结论行——"检查器崩了"与"拦住了问题"在退出码上是同一个值，
# 只能靠结论行区分，所以绝不能在崩掉时伪造一行结论。
set -uo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
# 变量引用一律带花括号：后面紧跟中文全角标点时，标点首字节会被并进变量名，
# 在 set -u 下直接让脚本退出（"xxx: unbound variable"）。
exec python3 - "${ROOT}" "$@" <<'PY'
import io
import os
import re
import sys
import tempfile
import shutil

# 输出被 `| head` 截断时不要抛 BrokenPipeError 的 traceback：门禁脚本在管道里
# 吐一坨 traceback，读的人第一反应是"脚本坏了"，而不是"我截断了它的输出"。
try:
    import signal
    signal.signal(signal.SIGPIPE, signal.SIG_DFL)
except (AttributeError, ValueError):
    pass

root = sys.argv[1]
args = list(sys.argv[2:])

FENCE = re.compile(r'^\s*(?:>\s*)*(?:```|~~~)')
SEPCELL = re.compile(r'^\s*:?-{3,}:?\s*$')


def read(p):
    return io.open(p, encoding='utf-8', errors='replace').read().split('\n')


def split_prefix(s):
    """剥掉行首空白与 ">" 标记，返回 (引用深度, 正文)。"""
    i, d, n = 0, 0, len(s)
    while True:
        j = i
        while j < n and s[j] in ' \t':
            j += 1
        if j < n and s[j] == '>':
            d += 1
            i = j + 1
            continue
        return d, s[j:]


def cells(content):
    """把一行表格切成单元格；不是表格行返回 None。

    单元格内的转义竖线 "\\|" 不是分隔符 —— 它是最常见的正文内容（正则、管道命令）。
    """
    if not content.startswith('|'):
        return None
    parts = re.split(r'(?<!\\)\|', content.strip())
    if parts and parts[0].strip() == '':
        parts = parts[1:]
    if parts and parts[-1].strip() == '':
        parts = parts[:-1]
    return parts


def is_sep(content):
    c = cells(content)
    if not c:
        return False
    return all(SEPCELL.match(x) for x in c)


def md_files(paths):
    out = []
    for p in paths:
        if os.path.isfile(p):
            out.append(p)
            continue
        for dirpath, _, names in os.walk(p):
            for n in sorted(names):
                if n.endswith('.md'):
                    out.append(os.path.join(dirpath, n))
    out.sort()
    return out


def judge_file(path, rel):
    """返回 (fails, tables, fence_ok)。fails 项为 (判据ID, 行号, 说明)。"""
    lines = read(path)
    fails = []

    # ---- 先标出围栏状态：围栏内的行不参与 T1/T2 ----
    live = [True] * len(lines)
    inside = False
    opened = 0
    for k, raw in enumerate(lines):
        if FENCE.match(raw):
            opened += 1
            inside = not inside
            live[k] = False
            continue
        if inside:
            live[k] = False
    fence_ok = (opened % 2 == 0)
    if not fence_ok:
        fails.append(('T3', 0, '围栏标记 %d 个（奇数）→ 文件后半段整体被当成代码；'
                               'T1/T2 会在错误的分区上跑' % opened))

    def info(k):
        if not (0 <= k < len(lines)) or not live[k]:
            return None
        d, c = split_prefix(lines[k])
        if not c.startswith('|'):
            return None
        return d, c

    # ---- T1 孤行 ----
    # 先算出"哪些行真的在某张合法的表里"（表头 + 分隔行 + 同深度的后续行），剩下的才是孤行。
    # 不能只看"上一行是不是表格行"：**孤行段里第 2 行往后，上一行恰好也是表格行**，于是会被
    # 当成"续行"而漏报——docs/03 修复前是 56~59 四行连续掉出引用块，旧判据只报第 1 行，
    # 照着报错修就会只修首行、再跑一次又红一行。**报告要能一次改完，否则每次运行只前进一行。**
    headers = {}
    for k in range(len(lines)):
        a0 = info(k)
        b0 = info(k + 1)
        if a0 is not None and b0 is not None and is_sep(b0[1]) and not is_sep(a0[1]):
            headers[k] = a0[0]

    in_table = set()
    for h, d in headers.items():
        in_table.add(h)
        in_table.add(h + 1)                       # 分隔行
        j = h + 2
        while True:
            t = info(j)
            if t is None or t[0] != d or is_sep(t[1]) or j in headers:
                break
            in_table.add(j)
            j += 1

    orphans = [k for k in range(len(lines)) if info(k) is not None and k not in in_table]
    runs = []
    for k in orphans:
        d = info(k)[0]
        if runs and runs[-1][1] == k - 1 and runs[-1][2] == d:
            runs[-1][1] = k                       # 同一段孤行的续行
        else:
            runs.append([k, k, d])

    for first, last, d in runs:
        span = '' if first == last else '（连续 %d 行，%d-%d）' % (last - first + 1, first + 1, last + 1)
        if is_sep(info(first)[1]):
            # 症状在这一行（分隔行），病因多半在上一行的**行末**：渲染器把「段落 + 表头」
            # 当成同一段，于是整块（含后面几行）一起降级为文本。不点出上一行，人会去改分隔行。
            hint = ''
            if first > 0 and lines[first - 1].strip():
                hint = '；病因多半在第 %d 行的行末（该行以正文开头、结尾却粘着表头），排查从那里开始' % first
            fails.append(('T1', first + 1,
                          '分隔行前面不是表头行 → 这张表没有起点%s%s' % (span, hint)))
        else:
            fails.append(('T1', first + 1,
                          '孤行（前一行不是同一引用层级 [%d] 内的表格行）→ 会被渲染成带竖线的普通文本%s'
                          % (d, span)))

    # ---- T2 列数 ----
    tables = []
    k = 0
    while k < len(lines):
        a = info(k)
        b = info(k + 1)
        if a is not None and b is not None and is_sep(b[1]) and not is_sep(a[1]):
            hc = cells(a[1])
            ncols = len(hc) if hc is not None else 0
            tables.append((k + 1, ncols))
            j = k + 2
            while True:
                t = info(j)
                if t is None or t[0] != a[0] or is_sep(t[1]):
                    break
                nx = info(j + 1)
                if nx is not None and is_sep(nx[1]):
                    break                 # 这是下一张表的表头，不是本表数据行
                tc = cells(t[1])
                if tc is not None and len(tc) != ncols:
                    fails.append(('T2', j + 1, '列数 %d ≠ 表头 %d' % (len(tc), ncols)))
                j += 1
            k = j
        else:
            k += 1

    return fails, tables, fence_ok


def label(p):
    """给人看的路径：在仓库内就相对仓库根，在仓库外就给绝对路径。

    直接 relpath 到 root 时，扫临时目录会打印 "../../../../var/folders/…" ——
    那串路径对读的人没有用，而失败信息里最该一眼看到的恰恰是"哪个文件"。
    """
    ap = os.path.abspath(p)
    try:
        r = os.path.relpath(ap, root)
    except ValueError:
        return ap
    return ap if r.startswith('..') else r


def judge(paths):
    fails = []
    tables = 0
    for p in md_files(paths):
        rel = label(p)
        f, t, _ = judge_file(p, rel)
        tables += len(t)
        for fid, ln, msg in f:
            fails.append((rel, ln, fid, msg))
    return fails, tables


# ---------------------------------------------------------------- 自证
# 样本写在临时目录，绝不碰真实 docs：自证能改到被测对象的话，它本身就是风险。
SAMPLE_OK = """# 标题

| 甲 | 乙 |
|---|---|
| 1 | 2 |
| 3 | 4 |

> 引用块里的表（深度 1，同深度续行合法）
>
> | 丙 | 丁 |
> |---|---|
> | 5 | 6 |

```sh
grep x f | awk '{print $2}' | sort
```

> ```sh
> grep y g | jq -r '.a | .b'
> ```

**段落之后空一行再接表格：**

| 戊 |
|---|
| 7 |
"""

# 注入 1：表头被粘在段落行末（docs/00 的真实形态）→ 分隔行前面没有表头，T1 必须红
SAMPLE_GLUED_HEADER = """**结论：三条路，需客户选一条：| 选项 | 交付内容 | 代价 |
|---|---|---|
| A | 甲 | 一 |
"""

# 注入 2：空行先把表截断，后面又是表格行（docs/04 与 docs/06 的真实形态）→ T1 必须红
SAMPLE_BLANK_CUT = """| 甲 | 乙 |
|---|---|
| 1 | 2 |

| 3 | 4 |
| 5 | 6 |
"""

# 注入 3：少一格（docs/05 Q15 的真实形态）→ T2 必须红
SAMPLE_COLCOUNT = """| 甲 | 乙 | 丙 |
|---|---|---|
| 1 | 2 |
| 3 | 4 | 5 |
"""

# 注入 4：引用块里丢了 "> " 前缀（docs/03 的真实形态）→ 深度变了，T1 必须红
SAMPLE_DEPTH_DROP = """> | 甲 | 乙 |
> |---|---|
> | 1 | 2 |
| 3 | 4 |
"""

# 注入 5：围栏没闭合 → T3 必须红（后半段全被当成代码）
SAMPLE_FENCE = """```sh
grep x f | sort
"""


def write_sample(d, name, text):
    p = os.path.join(d, name)
    io.open(p, 'w', encoding='utf-8').write(text)
    return p


def self_test():
    checks = []

    def check(ok, detail):
        checks.append((bool(ok), detail))

    tmp = tempfile.mkdtemp(prefix='tables-selftest-')
    try:
        # ---- 方向一：正样本必须绿，且"扫到了东西" ----
        okd = os.path.join(tmp, 'ok')
        os.makedirs(okd)
        write_sample(okd, 'ok.md', SAMPLE_OK)
        fails, tables = judge([okd])
        check(not fails, '正样本判绿（普通表 / 引用块内表 / shell 管道 / 引用块内围栏四类都不误伤）')
        # 只断言"没报错"是不够的：一个永远返回 0 条 fail 的检查器也满足它。
        # 必须同时断言它真的识别到了表 —— 空集永远无违规。
        check(tables == 3, '正样本识别到 %d 张表（期望 3：普通表 2 + 引用块内表 1）' % tables)

        # ---- 方向二：每个注入必须红在预期判据上 ----
        sub = os.path.join(tmp, 'inj')
        for name, text, want in (
            ('glued.md', SAMPLE_GLUED_HEADER, 'T1'),
            ('cut.md', SAMPLE_BLANK_CUT, 'T1'),
            ('cols.md', SAMPLE_COLCOUNT, 'T2'),
            ('depth.md', SAMPLE_DEPTH_DROP, 'T1'),
            ('fence.md', SAMPLE_FENCE, 'T3'),
        ):
            d = os.path.join(sub, name.replace('.md', ''))
            os.makedirs(d)
            write_sample(d, name, text)
            f, _ = judge([d])
            ids = sorted(set(x[2] for x in f))
            check(bool(f) and want in ids,
                  '注入[%s] 红在 %s（实得 %s，共 %d 条）'
                  % (name, want, ','.join(ids) if ids else '无', len(f)))

        ok = sum(1 for c, _ in checks if c)
        total = len(checks)
        for c, detail in checks:
            print(('  ok    ' if c else '  FAIL  ') + detail)
        print()
        if ok == total:
            print('== 自证通过：%d 项（1 正样本判绿且识别到 3 张表；5 个注入各自红在预期判据上）' % total)
            return 0
        print('== 自证失败：%d 项不符合预期' % (total - ok))
        return 1
    except Exception as e:                       # noqa: BLE001
        print('  自证异常: %r' % (e,))
        raise
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main():
    if '--self-test' in args:
        return self_test()

    list_mode = '--list' in args
    targets = [a for a in args if not a.startswith('-')]
    if not targets:
        targets = [os.path.join(root, 'docs')]
    if not all(os.path.exists(t) for t in targets):
        print('环境问题: 扫描目标不存在 %s' % [t for t in targets if not os.path.exists(t)], file=sys.stderr)
        return 2

    fails, tables = judge(targets)
    if list_mode:
        for p in md_files(targets):
            rel = label(p)
            _, t, _ = judge_file(p, rel)
            for ln, n in t:
                print('%-42s :%-5d 表头 %d 列' % (rel, ln, n))
        return 0

    for rel, ln, fid, msg in fails:
        print('  FAIL  %s:%d  [%s] %s' % (rel, ln, fid, msg))
    print()
    print('== 表格完整性：%d 个文件、%d 张表；孤行/表头缺失 T1=%d、列数不符 T2=%d、围栏未配对 T3=%d'
          % (len(md_files(targets)), tables,
             sum(1 for x in fails if x[2] == 'T1'),
             sum(1 for x in fails if x[2] == 'T2'),
             sum(1 for x in fails if x[2] == 'T3')))
    return 1 if fails else 0


sys.exit(main())
PY
