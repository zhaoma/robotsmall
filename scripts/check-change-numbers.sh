#!/usr/bin/env bash
# scripts/check-change-numbers.sh
#
# 硬门禁：docs 的变更表里，**同一编号空间内不得出现重复编号**。
# 语义来源：docs/01 的变更记录规则（取号前先声明 + 机械校验重复即红，两者同时立）。
#
# 为什么需要它：变更编号是"同一事实的身份证"。两个人同时取到同一号，或同一事实拿到两个号，
# 后果不是排版问题——引用方（"见 #83"）会指到两条不同的结论上，而**两条都读得通**，
# 于是没人发现。这比版本号引用更隐蔽：版本号至少还长得像版本号。
#
# ── 编号空间的取证结论（先查清再写，不要凭"应该是一套"假设）────────────────────────
# 实测（2026-11，全仓 8 个 md）：编号**不是全局一套**。
#   docs/01  一个空间：1..87，跨 20 个「每个版本一张表」的表块，编号连续
#   docs/03  两个空间：补丁清单 1..34、冲突索引 1..8（都从 1 开始）
#   docs/02  一个空间：1..2
#   其余文档无此类表
# 反例（证明全局判重会误杀）：docs/01 的 #34 = 「商品 CSV 导入导出 Won't」，
# docs/03 的 #34 = 「PM v1.17 两处收紧」——同一号码、两条不同事实，**两者都合法**。
# 若按全局判重，这条会天天红，而它给出的处置路径（改号）会破坏所有既存引用 → 门禁被关掉。
#
# 所以空间边界必须由**结构**推导，不写死文件清单：
#   · 空间 = 连续的 "> | N | ..." 引用块表格行
#   · 遇到 "> ### 标题" 这类**引用块内的标题行**就是硬分隔（新版块换新空间）
#   · 表块之间夹一行 "> **v1.5 变更（…）**" 这类加粗标签**不算分隔** ——
#     那些表块共享同一条编号序列（docs/01 就是这样从 1 编到 87）
#   · 非表格的普通行也不分隔：否则 docs/01 会被拆成 20 个空间，跨版本的重复号照样漏掉
# `--list-spaces` 把推导出来的空间导出来，供人审阅（和 check-doc-version-refs.sh 的
#  --list-exempt 同一思路：结构是事实，白名单是感觉）。
#
# ── 判据两条，顺序不可换 ───────────────────────────────────────────────────────
#   ① 扫到的编号行总数 ≥ --min（默认 85 = 已知分配下界）——先证明"我确实扫到了东西"
#   ② 同一空间内编号唯一
# 判据 1 必需的理由：**空集永远无重复**。一个正则写错、或文档把表格从 "> |" 改成 "|"，
# 脚本会扫到 0 条然后报"全部唯一"——绿的形态和真绿一模一样。
#
# 判据 1 的下界方向是**单向**的：变更行只增不减，所以正常编辑永远不会顶红；
# 只有"合法合并/删行"才会红一次。**不要**把下界写成"正好等于当前条数"——那样任何一次
# 删行都误红，红几次之后这条判据就会被改松（这和 check-doc-version-refs.sh 里
# "豁免推导错了只少扫一段"是同一类病）。
#
# 用法:
#   ./scripts/check-change-numbers.sh                # 扫 docs/**/*.md（递归）
#   ./scripts/check-change-numbers.sh --min 85       # 显式指定下界（下界应只增）
#   ./scripts/check-change-numbers.sh --list-spaces  # 先看编号空间是怎么划出来的（可审）
#   ./scripts/check-change-numbers.sh --self-test    # fail-before-pass 自证（3 项）
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
# 在 set -u 下直接让脚本退出（"xxx?: unbound variable"）。
DOCS=${DOCS_DIR:-${ROOT}/docs}
MIN=${MIN_CHANGE_ROWS:-85}

# 注意：${DOCS} / ${MIN} 都带花括号，理由同上。
exec python3 - "${DOCS}" "${MIN}" "$@" <<'PY'
import io
import os
import re
import sys
import collections
import tempfile
import shutil

docs = sys.argv[1]
min_rows = int(sys.argv[2])
args = sys.argv[3:]

ROW = re.compile(r'^>\s*\|\s*(\d+)\s*\|')
SPACE_BREAK = re.compile(r'^>\s*#{1,6}\s')     # 引用块内的标题行 = 新编号空间


def read(p):
    return io.open(p, encoding='utf-8', errors='replace').read().split('\n')


def md_files(root):
    out = []
    for dirpath, _, names in os.walk(root):
        for n in sorted(names):
            if n.endswith('.md'):
                out.append(os.path.join(dirpath, n))
    out.sort()
    return out


def scan_spaces(root):
    """返回编号空间列表：每项 {'file','anchor_line','rows':[(num,line),...]}。

    空间边界只由结构与引用块内标题决定，不按文件切、不按表块切、不写死文件名。
    """
    spaces = []
    for path in md_files(root):
        rel = os.path.relpath(path, root).replace(os.sep, '/')
        cur = None
        for i, line in enumerate(read(path), 1):
            if SPACE_BREAK.match(line):
                if cur:
                    spaces.append(cur)
                    cur = None
                continue
            m = ROW.match(line)
            if m:
                if cur is None:
                    cur = {'file': rel, 'anchor_line': i, 'rows': []}
                cur['rows'].append((int(m.group(1)), i))
        if cur:
            spaces.append(cur)
    return spaces


def judge(spaces, floor):
    """判据两条，顺序固定。返回 (失败列表, 扫到的总条数)。"""
    failures = []
    total = sum(len(s['rows']) for s in spaces)

    # 判据 1：先证明扫到了东西。失败信息必须同时给"扫到多少"和"下界多少"——
    # 只说"没扫到东西"，读者分不清是正则写错还是文档结构变了，而这两者的修法相反。
    if total < floor:
        failures.append((
            'SCAN_TOO_SMALL',
            '',
            '扫到 %d 条编号，低于下界 %d 条：判据本身没生效，不是"没有重复"。'
            '若文档确实精简过，用 --min 下调下界并说明原因；若是正则/结构问题，先修扫描。'
            % (total, floor)))

    # 判据 2：同一空间内唯一。跨空间同号是合法的（见文件头取证）。
    for s in spaces:
        counter = collections.Counter(n for n, _ in s['rows'])
        dups = sorted(n for n, c in counter.items() if c > 1)
        for n in dups:
            lines = [str(ln) for num, ln in s['rows'] if num == n]
            failures.append((
                'DUPLICATE_CHANGE_NUMBER',
                '%s:%s' % (s['file'], lines[0]),
                '编号 #%d 在同一编号空间内出现 %d 次（行 %s，空间起点 %s:%d）。'
                '取号前先声明、同一轮串行分配；已被引用的号不得改号或复用。'
                % (n, counter[n], '/'.join(lines), s['file'], s['anchor_line'])))
    return failures, total


def list_spaces(spaces, total):
    out = ['编号空间（由结构推导，供审阅）：']
    for idx, s in enumerate(spaces, 1):
        nums = [n for n, _ in s['rows']]
        dup = sorted(n for n, c in collections.Counter(nums).items() if c > 1)
        out.append('  #%d  %s:%d   %d 条   编号 %d..%d   重复=%s'
                   % (idx, s['file'], s['anchor_line'], len(nums), min(nums), max(nums),
                      dup or '无'))
    out.append('合计 %d 个空间 / %d 条编号（下界 %d）' % (len(spaces), total, min_rows))
    return out


# ---------------------------------------------------------------- 自证（3 项）
# 样本写在临时目录，绝不碰真实 docs：自证能改到被测对象的话，它本身就是风险。
SAMPLE_OK = """> **v1.1 变更**
> | # | 变更 |
> |---|---|---|
> | 1 | 甲 |
> | 2 | 乙 |
> **v1.2 变更**
> | # | 变更 |
> |---|---|---|
> | 3 | 丙 |

> ### 第二个空间（引用块内标题 = 硬分隔）
> | # | 冲突 |
> |---|---|---|
> | 1 | 丁 |
> | 2 | 戊 |
> | 3 | 己 |
"""

# 注入：同一空间内 #2 出现两次（跨表块——这正是"两个版本表各写一次 2"的真实形态，
# 也证明空间不被表块边界切开）
SAMPLE_DUP = """> **v1.1 变更**
> | # | 变更 |
> |---|---|---|
> | 1 | 甲 |
> | 2 | 乙 |
> **v1.2 变更**
> | # | 变更 |
> |---|---|---|
> | 2 | 丙（重号） |
> | 3 | 丁 |
"""

# 注入：把 "> |" 改成 "|"（表格形态破坏）→ 扫不到任何编号。
# 这一项证明"判据 1 确实在验扫描本身"，而不是只在纸面上写着。
SAMPLE_INVISIBLE = """| # | 变更 |
|---|---|
| 1 | 甲 |
| 2 | 乙 |
| 3 | 丙 |
"""


def write_sample(root, name, text):
    path = os.path.join(root, name)
    io.open(path, 'w', encoding='utf-8').write(text)
    return path


def self_test():
    checks = []

    def check(ok, detail):
        checks.append((bool(ok), detail))

    tmp = tempfile.mkdtemp(prefix='changenum-selftest-')
    try:
        # ---- 方向一：正样本必须绿 ----
        ok_dir = os.path.join(tmp, 'ok')
        os.makedirs(ok_dir)
        write_sample(ok_dir, 'ok.md', SAMPLE_OK)
        spaces = scan_spaces(ok_dir)
        fails, total = judge(spaces, floor=6)
        check(not fails and total == 6,
              '正样本（两个空间各 1..3，跨表块共享序列）判绿：%d 条、判据 %d 条全过'
              % (total, 2))

        # 空间划分本身也要断言：否则"只切出 1 个空间"会让跨空间同号变成误红，
        # 而"切出 6 个空间"会让真实重复漏掉 —— 两种都是绿/红的假象。
        check(len(spaces) == 2,
              '空间划分按结构推导：共 %d 个空间（期望 2：一个连续编号序列 + 一个引用块标题分隔）'
              % len(spaces))

        # ---- 方向二：注入重复号 → 必须红在判据 2 ----
        dup_dir = os.path.join(tmp, 'dup')
        os.makedirs(dup_dir)
        write_sample(dup_dir, 'dup.md', SAMPLE_DUP)
        fails2, total2 = judge(scan_spaces(dup_dir), floor=4)
        codes2 = [c for c, _, _ in fails2]
        check(codes2 == ['DUPLICATE_CHANGE_NUMBER'] and total2 == 4,
              '注入重复号（跨表块 #2 两次）→ 红在判据 2：%s'
              % (fails2[0][2] if fails2 else '未报红'))

        # ---- 方向三：表格形态破坏 → 必须红在判据 1（而不是"无重复"） ----
        inv_dir = os.path.join(tmp, 'invisible')
        os.makedirs(inv_dir)
        write_sample(inv_dir, 'flat.md', SAMPLE_INVISIBLE)
        fails3, total3 = judge(scan_spaces(inv_dir), floor=85)
        codes3 = [c for c, _, _ in fails3]
        check(codes3 == ['SCAN_TOO_SMALL'] and total3 == 0,
              '表格形态被破坏（"> |" 变 "|"）→ 红在判据 1：扫到 %d 条 < 下界 85，判据未生效'
              % total3)

        # 判据 1 的失败信息必须同时带"扫到多少"和"下界多少"，否则读者无法归因
        check(bool(fails3) and '扫到 0 条' in fails3[0][2] and '下界 85 条' in fails3[0][2],
              '判据 1 的失败信息同时打印扫到条数与下界：%s'
              % (fails3[0][2].split('：')[0] if fails3 else '未报红'))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    failed = [d for ok, d in checks if not ok]
    for ok, detail in checks:
        print('  %s  %s' % ('ok  ' if ok else 'FAIL', detail))

    if failed:
        print('== 自证失败：%d 项不符合预期' % len(failed))
        return 1

    print('== 自证通过：%d 项（正样本 2 项判绿 / 注入重复红在判据 2 / 形态破坏红在判据 1 且信息可归因）'
          % len(checks))
    return 0


if '--self-test' in args:
    # 刻意不 catch：崩掉就不打印结论行，让"崩了"与"拦住了"在输出层面可区分。
    sys.exit(self_test())

if not os.path.isdir(docs):
    print('找不到 docs 目录: %s' % docs, file=sys.stderr)
    sys.exit(2)

spaces = scan_spaces(docs)

if '--list-spaces' in args:
    failures, total = judge(spaces, min_rows)
    print('\n'.join(list_spaces(spaces, total)))
    if failures:
        print('\n（当前状态下判据未通过，详见直接运行本脚本的输出）')
    sys.exit(0)

failures, total = judge(spaces, min_rows)

if failures:
    for code, loc, msg in failures:
        print('%s %s: %s' % (loc or 'docs', code, msg) if loc else '%s: %s' % (code, msg))
    print('FAILED: %d 处' % len(failures))
    print("""
CHANGE NUMBER: 共 %d 处问题（扫到 %d 条编号 / %d 个空间）
  → 判据 1 失败：扫描本身没生效。先确认表格仍是 "> | N | ..." 形态（本门禁只认引用块表格）。
  → 判据 2 失败：同一编号空间内重号。取号前先在变更表里声明，同轮串行分配；
    已被引用（"见 #NN"）的号不得复用、不得改号——改号会让所有引用静默指错。
  → 编号空间是**按结构分段**的（引用块内标题为界），跨空间同号合法。
    用 --list-spaces 查看当前推导结果；若划分不符合预期，调整文档的标题层级，
    不要在脚本里加文件白名单——白名单是"感觉"，结构是"可审阅的事实"。
""" % (len(failures), total, len(spaces)))
    sys.exit(1)

print('OK: 变更编号无重复（%d 个编号空间 / %d 条编号，下界 %d）' % (len(spaces), total, min_rows))
sys.exit(0)
PY
