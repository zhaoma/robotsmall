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
# 退出码: 0 干净 / 1 有引用 / 2 环境问题
set -uo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
DOCS=${DOCS_DIR:-$ROOT/docs}

exec python3 - "$DOCS" "$@" <<'PY'
import io, os, re, sys

docs = sys.argv[1]
args = sys.argv[2:]
# 只拦"需求版本" v1.x；放过三段式发行版本 v1.4.2 / v1.10.3（那是 Git tag / 镜像 tag，
# 与需求版本是两套编号。不排除会把发布流水线整条打红——误伤的门禁一定会被关掉）。
# (?!\.\d) 排除 v1.4.2 的 v1.4；(?<![0-9A-Za-z.]) 排除 xv1.4 / 1.v1.4
PAT = re.compile(r'(?<![0-9A-Za-z.])v1\.\d+(?!\.\d)')
if not os.path.isdir(docs):
    print('找不到 docs 目录: %s' % docs, file=sys.stderr); sys.exit(2)

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
                if re.search(r'变更记录|附录\s*B|作废|台账|追溯', title):
                    cur_start = i
            continue
    if cur_start is not None:
        rs.append((cur_start, len(lines), '追溯区（变更记录 / 附录 B / 作废台账）'))

    return rs

def in_ranges(i, rs):
    return any(a <= i <= b for a, b, _ in rs)

if '--list-exempt' in args:
    for f in sorted(os.listdir(docs)):
        if not f.endswith('.md'):
            continue
        rs = ranges_for_file(read(os.path.join(docs, f)))
        if not rs:
            print('文件: docs/%s   （无豁免，全文视为正文）' % f)
            continue
        print('文件: docs/%s' % f)
        for a, b, why in rs:
            print('  豁免 %d:%d  %s' % (a, b, why))
    print('正则: %s' % PAT.pattern)
    sys.exit(0)

files = []
for dirpath, _, names in os.walk(docs):
    for n in sorted(names):
        if n.endswith('.md'):
            files.append(os.path.join(dirpath, n))
files.sort()

total, report = 0, []
for f in files:
    lines = read(f)
    rs = ranges_for_file(lines)
    for i, l in enumerate(lines, 1):
        if in_ranges(i, rs):
            continue
        for m in PAT.finditer(l):
            total += 1
            s = max(0, m.start() - 40); e = min(len(l), m.end() + 40)
            ctx = ('…' if s else '') + l[s:e].strip() + ('…' if e < len(l) else '')
            report.append('%s:%d 引用了 %s\n     上下文：%s' % (f.replace(docs + os.sep, 'docs/'), i, m.group(0), ctx))
            break   # 一行只报一次，避免同一行刷屏

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
