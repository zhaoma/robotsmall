#!/usr/bin/env bash
# scripts/check-doc-version-refs.sh
#
# 硬门禁：docs 正文里不得出现 `v1.x` 版本号引用。语义唯一来源：docs/01 的「结构规则 3｜正文禁用版本号」。
#
# 为什么需要它：版本号是"需要人工失效的缓存键"。正文里写一句"与 v1.x 对齐"，等于复制了一份会过期的
# 状态——与"结论写两处"是同一个病。AC 编号语义稳定、W 编号永不复用，版本号永远在动。
# 本脚本拦 `v1.\d+` 的全部形态，**包括 `v1.10 #47` 这种"看着像引用变更条目"的写法**（不给 # 开例外）。
#
# 豁免按【结构章节】而不是【文件】或【行号硬编码】——否则要么 01 自己的正文违规被整文件漏掉，
# 要么其他文档的作废台账被整片误杀（实测误杀 47 处）。每个文件各自豁免：
#   1) 文件头：第 1 行到第一个二级标题之前 —— 那里的版本号是主语，不是引用
#   2) 二级标题命中「变更记录 / 附录 B / 作废 / 台账 / 追溯」的整段 —— 那是追溯区，
#      版本号是被描述的对象
# 其余全部视为正文。`--list-exempt` 可导出当前每个文件的豁免区间，供审阅而不是靠感觉。
#
# 用法:
#   ./scripts/check-doc-version-refs.sh                 # 扫 docs/**/*.md
#   ./scripts/check-doc-version-refs.sh --list-exempt   # 先看豁免区间（可审，不靠感觉）
#   ./scripts/check-doc-version-refs.sh --self-test     # 双向自证（正样本必绿 + 注入样本必红在行）
# 退出码: 0 干净 / 1 有引用 / 2 环境问题
#
# 为什么必须有 --self-test：本脚本最容易"假绿"——它的豁免不是跳过某几行，而是**按每个文件的
# 结构推导豁免区间**。推导错了不会报错，只会少扫一段，于是判绿。所以自证要两个方向都证：
#   方向一（正样本必绿）：只有"永远红"的门禁才通不过这一条；永远红的门禁结局只有一个——被关掉
#     （上一轮误杀 47 处就是前兆：处置路径不可执行时，人只剩"关掉它"这一个动作）。
#   方向二（注入样本必红在那一行）：只证"退出码非 0"不够。红了但不告诉你哪个文件哪一行，
#     人的第一反应是整个文件重写一遍去猜。
#
# 自证结论行契约（全组统一，脚本最后一行必是二者之一，且与退出码对应）：
#   == 自证通过：<N> 项（<明细>）        → 退出码 0
#   == 自证失败：<N> 项不符合预期         → 退出码 1
# 为什么要有结论行：只看退出码时，"拦住问题了"和"检查器自己崩了"是同一个值（非 0）。
# 判绿必须两个信号同时看——崩掉的脚本不会有结论行。因此本脚本自证过程中若抛异常，
# 刻意不打印结论行，让堆栈原样暴露（宁可看起来像崩了，也不假装"自证失败"）。
set -uo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
DOCS=${DOCS_DIR:-$ROOT/docs}

# 注意：${DOCS} 这类变量引用一律带花括号 —— 变量后面紧跟中文全角标点时，
# 全角标点的首字节会被并进变量名，在 set -u 下直接让脚本退出（"xxx?: unbound variable"）。
exec python3 - "${DOCS}" "$@" <<'PY'
import io
import os
import re
import shutil
import sys
import tempfile

docs = sys.argv[1]
args = sys.argv[2:]
# 只拦"需求版本" v1.x；放过三段式发行版本 v1.4.2 / v1.10.3（那是 Git tag / 镜像 tag，
# 与需求版本是两套编号。不排除会把发布流水线整条打红——误伤的门禁一定会被关掉）。
# 判法：先抓"v1."开头的最长点分段，再要求命中串**恰好一个点**才是需求版本。
#
# 为什么不用正则负向断言 `v1\.\d+(?!\.\d)`：\d+ 是贪婪但可回溯，遇到 v1.10.3 时
# \d+ 先取 "10" 被 (?!\.\d) 否掉，回溯取 "1" 反而通过，于是 **v1.10.3 被判成 v1.1**。
# 这个 bug 是 --self-test 的"三段式发行版本不得误伤"那条正样本抓出来的（v1.4.2 侥幸不触发，
# 因为单字符没有可回溯的退路）——典型的一旦误杀发布流水线、人就关掉门禁的那类问题。
PAT = re.compile(r'(?<![0-9A-Za-z.])v1\.\d+(?:\.\d+)*')


def is_requirement_version(text):
    """恰好两段（v1.10）才是需求版本；三段及以上（v1.10.3）是发行 tag，放过。"""
    return text.count('.') == 1


TRACE_TITLE = re.compile(r'变更记录|附录\s*B|作废|台账|追溯')


def read(p):
    return io.open(p, encoding='utf-8', errors='replace').read().split('\n')


def ranges_for_file(lines):
    """豁免区间靠结构标记推导，不硬编码行号（文档会一直变长）。

    规则原文是「版本号只允许出现在**本文件**头、变更记录表与附录 B」——
    「本文件」意味着**每个文件各自豁免**，不是只有 01 豁免。
    早先只给 01 豁免，结果 02/03/architecture 的作废台账（它们同样是"被描述的对象"）
    被整片判红，一共 47 处；而唯一的合法处置"往 01 的豁免区间里放"根本放不进去
    （那些行不在 01 里）。门禁给出的处置路径不可执行时，人只会关掉门禁。

    区间来源（每层都是结构，不是白名单）：
      1) 文件头：第 1 行到第一个二级标题之前（标题行/当前版本行里版本号是主语）
      2) 变更记录 / 附录 B / 作废台账 / 追溯区：命中标题的二级章节整体
    其余全部视为正文。
    """
    rs = []
    first_h2 = None
    for i, l in enumerate(lines, 1):
        if re.match(r'^## ', l):
            first_h2 = i
            break
    if first_h2 and first_h2 > 1:
        rs.append((1, first_h2 - 1, '文件头（版本号是主语）'))

    cur_start = None
    for i, l in enumerate(lines, 1):
        m = re.match(r'^(#+) +(.*)$', l)
        if m:
            level, title = len(m.group(1)), m.group(2)
            if level <= 2:
                if cur_start is not None:
                    rs.append((cur_start, i - 1, '追溯区（变更记录 / 附录 B / 作废台账）'))
                    cur_start = None
                if TRACE_TITLE.search(title):
                    cur_start = i
            continue
    if cur_start is not None:
        rs.append((cur_start, len(lines), '追溯区（变更记录 / 附录 B / 作废台账）'))

    return rs


def in_ranges(i, rs):
    return any(a <= i <= b for a, b, _ in rs)


def md_files(root):
    files = []
    for dirpath, _, names in os.walk(root):
        for n in sorted(names):
            if n.endswith('.md'):
                files.append(os.path.join(dirpath, n))
    files.sort()
    return files


def scan(root):
    """扫描 root 下所有 .md，返回 (违规处数, 报告行列表)。正文/自证共用同一份逻辑。"""
    total, report = 0, []
    for f in md_files(root):
        lines = read(f)
        rs = ranges_for_file(lines)
        for i, l in enumerate(lines, 1):
            if in_ranges(i, rs):
                continue
            for m in PAT.finditer(l):
                if not is_requirement_version(m.group(0)):
                    continue
                total += 1
                s = max(0, m.start() - 40)
                e = min(len(l), m.end() + 40)
                ctx = ('…' if s else '') + l[s:e].strip() + ('…' if e < len(l) else '')
                rel = f.replace(root + os.sep, 'docs/')
                report.append('%s:%d 引用了 %s\n     上下文：%s' % (rel, i, m.group(0), ctx))
                break   # 一行只报一次，避免同一行刷屏
    return total, report


def list_exempt(root):
    """导出每个文件的豁免区间（可审阅的事实，不是白名单）。返回输出行列表。

    必须走 md_files() 递归，不能 os.listdir() 只列顶层 —— 这是本脚本最难发现的一个 bug：
    scan() 已经是递归的（所以门禁判定正确），但**审阅通道只列顶层**，于是
    `--list-exempt` 会报告"docs/architecture/00-architecture.md 没有被豁免"，
    而事实相反。**门禁没错、报告在说谎**，比两边都错更危险：做"豁免是否过宽"审计的人
    会得到错误答案，并可能去改一个本来正确的机制。
    更糟的是它曾经"自证通过"——因为自证的绿样本是平铺的（没有子目录），
    递归写不写都能过。所以自证里补了一个嵌套样本，把这一维变成必红项。
    """
    out = []
    for p in md_files(root):
        rel = os.path.relpath(p, root).replace(os.sep, '/')
        rs = ranges_for_file(read(p))
        if not rs:
            out.append('文件: docs/%s   （无豁免，全文视为正文）' % rel)
            continue
        out.append('文件: docs/%s' % rel)
        for a, b, why in rs:
            out.append('  豁免 %d:%d  %s' % (a, b, why))
    out.append('正则: %s   （仅两段式算需求版本：命中串点数 != 1 的放过，那是发行 tag）' % PAT.pattern)
    return out


# ---------------------------------------------------------------- 双向自证
# 样本一律写在临时目录，绝不碰真实 docs：自证要是能改到被测对象，它本身就是风险。
SAMPLE_OK = """# 标题
> 版本：v1.5（需求基线）

## 1. 正文小节
（无版本号）

## 变更记录
| 版本 | 说明 |
| v1.5 | 初稿 |
"""

# 附录 B / 作废台账：历史误杀 47 处的那一类，必须整段豁免
SAMPLE_OK_APPENDIX = """# 附录样本
> 版本：v1.5

## 1. 正文小节
（无版本号）

## 附录 B 作废台账
| v1.3 | 已作废 |
| v1.10 #47 | 结论已合并到 AC-11 |
"""

# 三段式发行版本（Git tag / 镜像 tag）不得误伤，否则会把发布流水线整条打红
SAMPLE_OK_SEMVER = """# 发行版样本
> 版本：v1.5

## 1. 正文小节
镜像 tag 为 v1.4.2，Git tag 为 v1.10.3。
"""

# 注入样本：违规**必须在正文中间**（第 8 行），且文件头/变更记录的合法版本号都在
SAMPLE_BAD = """# 标题
> 版本：v1.5（需求基线）

## 1. 正文小节
（无版本号）

## 2. 正文小节
（本节结论依据需求 v1.9 定稿并锁定。）

## 变更记录
| 版本 | 说明 |
| v1.5 | 初稿 |
"""
BAD_LINE = 8


def write_sample(root, name, text):
    path = os.path.join(root, name)
    io.open(path, 'w', encoding='utf-8').write(text)
    return path


def self_test():
    checks = []          # (是否通过, 明细)

    def check(ok, detail):
        checks.append((bool(ok), detail))

    tmp = tempfile.mkdtemp(prefix='docverrefs-selftest-')
    try:
        # ---- 方向一：正样本必须判绿 ----
        ok_dir = os.path.join(tmp, 'green')
        os.makedirs(ok_dir)
        write_sample(ok_dir, 'ok.md', SAMPLE_OK)

        # 嵌套一层子目录的绿样本。**这一份不是为了"更全"，是为了让"递归没写"变成必红**：
        # 早先只有平铺样本时，md_files() 递归写不写都能过，于是 self_test 在"递归"这一维
        # 给的是假保证（scan 递归了、list_exempt 没递归，自证 8 项全绿）。
        nested_dir = os.path.join(ok_dir, 'architecture')
        os.makedirs(nested_dir)
        write_sample(nested_dir, 'nested.md', SAMPLE_OK)

        total, report = scan(ok_dir)
        check(total == 0 and not report,
              '正样本（版本号仅在文件头 + 变更记录）判绿：%d 处违规' % total)

        # 绿的原因必须真的是"结构推导出的豁免"，不是"正则漏了"——
        # 这一条专治"豁免推导错了，少扫一段，于是静默判绿"这种假绿。
        rs = ranges_for_file(read(os.path.join(ok_dir, 'ok.md')))
        has_head = any(why.startswith('文件头') and a <= 2 <= b for a, b, why in rs)
        has_trace = any(why.startswith('追溯区') and a <= 9 <= b for a, b, why in rs)
        body_scanned = not in_ranges(5, rs)
        check(has_head and has_trace and body_scanned,
              '正样本的豁免区间由结构推导：文件头 %s / 追溯区 %s / 正文第 5 行仍被扫 → %s'
              % ([r for r in rs if r[2].startswith('文件头')],
                 [r for r in rs if r[2].startswith('追溯区')],
                 '是' if body_scanned else '否'))

        # ---- 方向一（续）：附录 B / 作废台账 整段豁免 ----
        ap_dir = os.path.join(tmp, 'green-appendix')
        os.makedirs(ap_dir)
        write_sample(ap_dir, 'appendix.md', SAMPLE_OK_APPENDIX)
        total_ap, _ = scan(ap_dir)
        check(total_ap == 0, '附录 B / 作废台账段内版本号被整段豁免：%d 处违规' % total_ap)

        # ---- 方向一（续）：三段式发行版本不误杀 ----
        sv_dir = os.path.join(tmp, 'green-semver')
        os.makedirs(sv_dir)
        write_sample(sv_dir, 'semver.md', SAMPLE_OK_SEMVER)
        total_sv, _ = scan(sv_dir)
        check(total_sv == 0, '三段式发行版本 v1.4.2 / v1.10.3 不误杀：%d 处违规' % total_sv)

        # ---- 方向二：注入样本必须红 ----
        bad_dir = os.path.join(tmp, 'red')
        os.makedirs(bad_dir)
        write_sample(bad_dir, 'bad.md', SAMPLE_BAD)
        total_bad, report_bad = scan(bad_dir)
        check(total_bad == 1, '注入样本（正文中间 v1.9）判红：%d 处违规' % total_bad)

        # 红必须红在该行：给文件 + 行号，而不是只给退出码
        located = bool(report_bad) and re.search(r'bad\.md:%d\b' % BAD_LINE, report_bad[0]) is not None
        check(located,
              '注入样本定位到 bad.md:%d（行号与文件都在）：%s'
              % (BAD_LINE, report_bad[0].split('\n')[0] if report_bad else '无报告'))

        # 上下文要带出违规原文，人能一眼看懂该改哪句
        contextual = bool(report_bad) and 'v1.9' in report_bad[0] and '定稿并锁定' in report_bad[0]
        check(contextual, '注入样本的上下文含违规原文与所在句：%s'
              % (report_bad[0].split('上下文：')[-1] if report_bad else '无报告'))

        # ---- 豁免必须可审阅：--list-exempt 走同一份结构推导 ----
        listed = list_exempt(ok_dir)
        check(any('豁免' in l and '文件头' in l for l in listed),
              '--list-exempt 可导出正样本的豁免区间：%s'
              % (next((l.strip() for l in listed if '文件头' in l), '未导出')))

        # 递归必须贯穿到审阅通道。判据是"输出的路径里出现子目录名"，不是"没有报错"。
        check(any('architecture/nested.md' in l for l in listed),
              '--list-exempt 递归到子目录（architecture/nested.md 出现在输出里）：%s'
              % next((l.strip() for l in listed if 'nested.md' in l), '未出现'))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    failed = [d for ok, d in checks if not ok]
    for ok, detail in checks:
        print('  %s  %s' % ('ok  ' if ok else 'FAIL', detail))

    if failed:
        print('== 自证失败：%d 项不符合预期' % len(failed))
        return 1

    print('== 自证通过：%d 项（正样本 4 项判绿、注入样本 3 项红在 %d 行、豁免导出与递归 2 项可审）'
          % (len(checks), BAD_LINE))
    return 0


if '--self-test' in args:
    # 刻意不 catch 异常：崩掉的脚本不打印结论行，调度方据此能区分"拦住问题了"与"检查器自己崩了"。
    sys.exit(self_test())

if not os.path.isdir(docs):
    print('找不到 docs 目录: %s' % docs, file=sys.stderr)
    sys.exit(2)

if '--list-exempt' in args:
    print('\n'.join(list_exempt(docs)))
    sys.exit(0)

total, report = scan(docs)

if total:
    print('\n'.join(report))
    print("""
DOC VERSION REF: 共 %d 处
  → 改为只引用 AC 编号或章节号（见 01 §6 AC-11 / 见 01 §6.1 S7），不要写版本；
    版本号会过期，AC 编号不会。要引用变更条目时用 W 编号（见 W20），不要写 "vX #47"——
    读者还得先知道 vX 是哪一版，缓存键又回来了。
  → 若整段确实是被描述的对象（台账 / 作废清单 / 历史对照），把它写成
    「变更记录 / 附录 B / 作废台账」这样的二级标题章节，脚本会整段豁免；
    而不是在本脚本里加白名单——白名单是"感觉"，章节结构是"可审阅的事实"。
    用 --list-exempt 可以看到当前每个文件被豁免了哪些区间。
""" % total)
    sys.exit(1)

print('OK: docs 正文无版本号引用（豁免区间见 --list-exempt）')
sys.exit(0)
PY
