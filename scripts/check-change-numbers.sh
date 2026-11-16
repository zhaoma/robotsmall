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
# ── 判据三条，顺序不可换 ───────────────────────────────────────────────────────
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
# ── 判据 3：每个编号空间内必须 **1..N 连续**（没有空洞）──────────────────────────────
# 判据 1 是全仓总量，它只保证"整体没崩"：docs/03 少扫 30 条（38→8）总量仍 ≥ 下界，
# 于是静默通过。而"每个文件各自取自己的上界"若写成常量表，就是一张会漂移、且必须
# 每轮手工维护的数字表 —— 那正是本轮在清的那类东西。
# 连续性是同一件事的**零维护**实现：某空间少扫一段 / 删掉一行 / 编号跳号，
# 立刻出现空洞 → 红。它不需要知道任何文件的"应有条数"，因为上界就是该空间自己的条数。
# 唯一代价：真的删/合并一条变更行时也会红一次。这是**有意的**——编号是永久记录，
# 该条作废请在描述里标"已作废"并保留编号，而不是把号抽掉（抽掉会让所有 "见 #NN" 静默指错）。
#
# 判据 3 比的是**集合**（编号是否 1..N），不是**行序**。行序错位单独记 WARN
# （NUMBER_OUT_OF_ORDER），不阻断。这条边界是踩出来的：第一版逐位比较行序，于是
# docs/01 **曾**把 "#92 排在 #90 前面"（编号一条不缺）判成"不连续、缺（无缺失但起始不是 1）"
# （该处行序已由维护者移正；以下是历史取证，不是现状）——
# 原因自相矛盾，而 --list-spaces 同一时刻打印"编号 1..92、重复=无"。门禁和它的审阅通道
# 说法不一，读的人只会认为门禁坏了。**一个说不清原因的红，最后一定会被关掉。**
#
# 用法:
#   ./scripts/check-change-numbers.sh                # 扫 docs/**/*.md（递归）
#   ./scripts/check-change-numbers.sh --min 85       # 显式指定下界（下界应只增）
#   ./scripts/check-change-numbers.sh --list-spaces  # 先看编号空间是怎么划出来的（可审）
#   ./scripts/check-change-numbers.sh --self-test    # fail-before-pass 自证（项数以末行结论为准）
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


def judge(spaces, floor, warns=None):
    """判据三条，顺序固定。返回 (失败列表, 扫到的总条数)。不阻断项追加进 warns（若给）。"""
    failures = []
    if warns is None:
        warns = []
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

    # 判据 3：每个空间内的编号**集合**必须是 1..N。先于判据 2 报告没有意义的多余错误会被抑制：
    # 有重号时集合必然也不等于 1..N，这里跳过（一个病因只报一个码，否则读者要去猜哪个是真因）。
    #
    # 为什么是"集合"不是"逐位相等"（第一版就是逐位比，然后错了）：
    # 逐位相等把**行序**也算进了判据，于是文档里只要有一处顺序错位（#92 插在 #90 前面），
    # 哪怕编号一条不缺也报红，原因写成"缺（无缺失但起始不是 1）"—— 这句话自相矛盾
    # （集合明明完整），而 `--list-spaces` 同一时刻打印"编号 1..92、重复=无"。
    # 同一件事两个通道给出相反的解释，读的人只会认定门禁坏了，然后把它关掉。
    # 所以拆成两件事：
    #   集合 ≠ 1..N       → FAIL（缺号/多号 = 真的丢了一条变更记录，不可放过）
    #   集合 = 1..N 但乱序 → WARN（表不再是时间序 = 抄写痕迹，值得提醒，但不丢记录）
    for sp in spaces:
        nums = [n for n, _ in sp['rows']]
        if len(set(nums)) != len(nums):
            continue
        need = list(range(1, len(nums) + 1))
        if sorted(nums) != need:
            missing = sorted(set(need) - set(nums))
            extra = sorted(set(nums) - set(need))
            detail = []
            if missing:
                detail.append('缺 %s' % ', '.join('#%d' % n for n in missing[:10]))
            if extra:
                detail.append('多出 %s' % ', '.join('#%d' % n for n in extra[:10]))
            failures.append((
                'NUMBER_GAP',
                '%s:%d' % (sp['file'], sp['anchor_line']),
                '编号空间起点 %s:%d 的编号集合不是 1..%d：%d 条编号，%s。'
                '少扫一段、删行或跳号都会长成这样。编号是永久记录：该条作废就在描述里标"已作废"'
                '并保留编号，不要抽号（抽掉会让所有"见 #NN"静默指错）。'
                % (sp['file'], sp['anchor_line'], len(nums), len(nums),
                   '；'.join(detail) or '（集合与条数不符）')))
        else:
            for k in range(len(nums) - 1):
                if nums[k] > nums[k + 1]:
                    warns.append((
                        'NUMBER_OUT_OF_ORDER',
                        '%s:%d' % (sp['file'], sp['rows'][k][1]),
                        '变更表行序错位：#%d 排在 #%d 前面（空间起点 %s:%d）。'
                        '编号本身完整、不丢记录，所以只提醒不阻断；但变更表是时间序，'
                        '错位多半是"插行插错位置"，下次取号时容易看错末尾。'
                        % (nums[k], nums[k + 1], sp['file'], sp['anchor_line'])))
                    break

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
        order = '单调' if nums == sorted(nums) else '有错位'
        out.append('  #%d  %s:%d   %d 条   编号 %d..%d   重复=%s   行序=%s'
                   % (idx, s['file'], s['anchor_line'], len(nums), min(nums), max(nums),
                      dup or '无', order))
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


# 注入：编号 1,2,4 —— 缺 #3。模拟"某文件少扫一段"或"删掉一行变更记录"：
# 总量看着还很正常（这里 3 条），只有连续性这一个信号能发现。
SAMPLE_GAP = """> | # | 变更 |
> |---|---|---|
> | 1 | 甲 |
> | 2 | 乙 |
> | 4 | 丁（#3 被抽掉了） |
"""


# 注入：集合完整但**行序错位**（#3 写在 #2 前面）。docs/01 **曾是这个形态**
# （真实事故：`#92` 排在 `#90` 前面，编号一条不缺；该处行序已移正，样本形态仍取自它）。
# 注意：`SAMPLE_ORDER` 的内容一个字都不要跟着 docs/01 改 —— 它钉的是
# "编号齐了不得判红"，与 docs/01 现在长什么样无关。
# 第一版判据把行序也算进去，于是这种文档被判"编号不连续、缺（无缺失但起始不是 1）"。
# 这条样本钉的是：**编号齐了就不得判红**，错位只能出 WARN。
SAMPLE_ORDER = """> | # | 变更 |
> |---|---|---|
> | 1 | 甲 |
> | 3 | 丙（先写了 #3） |
> | 2 | 乙 |
"""


# 注入：集合里**多出一个号**（1,2,3,5）。判据要能说清是"多 #5"而不是含糊的"不连续"——
# 缺号和多号的修法相反：缺号去查漏扫/删行，多号去查上界跨空间了。
SAMPLE_EXTRA = """> | # | 变更 |
> |---|---|---|
> | 1 | 甲 |
> | 2 | 乙 |
> | 3 | 丙 |
> | 5 | 戊（多了 #5） |
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

        # ---- 方向四：编号出现空洞（少扫一段 / 删行）→ 必须红在判据 3 ----
        # 这是判据 1 抓不到的那一类：总量仍然很大，只有"某个空间不连续"这一个信号。
        gap_dir = os.path.join(tmp, 'gap')
        os.makedirs(gap_dir)
        write_sample(gap_dir, 'gap.md', SAMPLE_GAP)
        fails4, total4 = judge(scan_spaces(gap_dir), floor=3)
        codes4 = [c for c, _, _ in fails4]
        check(codes4 == ['NUMBER_GAP'] and total4 == 3,
              '注入编号空洞（1,2,4，缺 #3）→ 红在判据 3：%s'
              % (fails4[0][2].split('。')[0] if fails4 else '未报红'))

        # ---- 方向五：集合完整但行序错位 → 不得判红，只能出 WARN ----
        # 这条是回归项：没有它，"按集合比"与"按行序比"两种实现在自证里分辨不出来。
        order_dir = os.path.join(tmp, 'order')
        os.makedirs(order_dir)
        write_sample(order_dir, 'order.md', SAMPLE_ORDER)
        warns5 = []
        fails5, total5 = judge(scan_spaces(order_dir), floor=3, warns=warns5)
        codes5w = [c for c, _, _ in warns5]
        check(not fails5 and codes5w == ['NUMBER_OUT_OF_ORDER'],
              '注入行序错位（1,3,2，编号一条不缺）→ 判绿 + 1 条 WARN（实得 failures=%s warns=%s）：%s'
              % (len(fails5), codes5w,
                 warns5[0][2].split('。')[0] if warns5 else '未出 WARN'))

        # ---- 方向六：多了号 → 红在判据 3，且原因必须点名"多 #5" ----
        extra_dir = os.path.join(tmp, 'extra')
        os.makedirs(extra_dir)
        write_sample(extra_dir, 'extra.md', SAMPLE_EXTRA)
        fails6, total6 = judge(scan_spaces(extra_dir), floor=4)
        codes6 = [c for c, _, _ in fails6]
        msg6 = fails6[0][2] if fails6 else ''
        check(codes6 == ['NUMBER_GAP'] and '多出 #5' in msg6 and '缺 #4' in msg6,
              '注入多号（1,2,3,5，4 条）→ 红在判据 3 且点名"缺 #4；多出 #5"：%s'
              % (msg6.split('。')[0] if msg6 else '未报红'))

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

    print('== 自证通过：%d 项（正样本 2 项判绿 / 注入重复红在判据 2 / 形态破坏红在判据 1 / '
          '编号空洞红在判据 3 / 行序错位只出 WARN / 多号红在判据 3 且点名）'
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
    warns = []
    failures, total = judge(spaces, min_rows, warns)
    print('\n'.join(list_spaces(spaces, total)))
    for code, loc, msg in warns:
        print('WARN %s: %s' % (loc or 'docs', msg))
    if failures:
        print('\n（当前状态下判据未通过，详见直接运行本脚本的输出）')
    sys.exit(0)

warns = []
failures, total = judge(spaces, min_rows, warns)

for code, loc, msg in warns:
    print('WARN %s: %s' % (loc or 'docs', msg))

if failures:
    for code, loc, msg in failures:
        print('%s %s: %s' % (loc or 'docs', code, msg) if loc else '%s: %s' % (code, msg))
    print('FAILED: %d 处' % len(failures))
    print("""
CHANGE NUMBER: 共 %d 处问题（扫到 %d 条编号 / %d 个空间）
  → 判据 1 失败：扫描本身没生效。先确认表格仍是 "> | N | ..." 形态（本门禁只认引用块表格）。
  → 判据 2 失败：同一编号空间内重号。取号前先在变更表里声明，同轮串行分配；
    已被引用（"见 #NN"）的号不得复用、不得改号——改号会让所有引用静默指错。
  → 判据 3 失败（NUMBER_GAP）：某空间编号集合不是 1..N，**缺号或多号**。
    缺号 = 少扫一段/删了行；多号 = 上界超了该空间条数（通常是插行插到了别的空间）。
    作废一条变更请在描述里标"已作废"并保留编号，不要抽号（抽掉会让"见 #NN"静默指错）。
  → 编号空间是**按结构分段**的（引用块内标题为界），跨空间同号合法。
    用 --list-spaces 查看当前推导结果；若划分不符合预期，调整文档的标题层级，
    不要在脚本里加文件白名单——白名单是"感觉"，结构是"可审阅的事实"。
""" % (len(failures), total, len(spaces)))
    sys.exit(1)

print('OK: 变更编号无重复且各空间 1..N 完整（%d 个编号空间 / %d 条编号，下界 %d%s）'
      % (len(spaces), total, min_rows,
         '；WARN %d 处（行序错位，不阻断）' % len(warns) if warns else ''))
sys.exit(0)
PY
