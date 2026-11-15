#!/usr/bin/env bash
# scripts/check-doc-version-refs.sh
#
# 硬门禁：docs 正文里不得出现 `v1.x` 版本号引用。语义唯一来源：docs/01 的「结构规则 3｜正文禁用版本号」。
#
# 为什么需要它：版本号是"需要人工失效的缓存键"。正文里写一句"与 v1.x 对齐"，等于复制了一份会过期的
# 状态——与"结论写两处"是同一个病。AC 编号语义稳定、W 编号永不复用，版本号永远在动。
# 本脚本拦 `v1.\d+` 的全部形态，**包括 `v1.10 #47` 这种"看着像引用变更条目"的写法**（不给 # 开例外）。
#
# 豁免按【行区间】而不是【文件】——否则 01 自己的正文违规会被整文件漏掉：
#   1) 01 文件头（首条 `> **vX 变更` 之前）—— 那里的版本号是主语，不是引用
#   2) 01 变更记录区（首条 `> **vX 变更` 起，到 `> **本文的写法约定` 之前）
#   3) 01 附录 B（`## 附录 B` 之后）—— 那里的版本号是被描述的对象
# 其余所有 .md（00/02/03/04/05/06 与 architecture/）**全文视为正文**。
#
# 用法:
#   ./scripts/check-doc-version-refs.sh                 # 扫 docs/**/*.md
#   ./scripts/check-doc-version-refs.sh --list-exempt   # 先看豁免区间（可审，不靠感觉）
# 退出码: 0 干净 / 1 有引用 / 2 环境问题
set -uo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
DOCS=${DOCS_DIR:-$ROOT/docs}
REQ="$DOCS/01-需求基线-OpenStore.md"

exec python3 - "$DOCS" "$REQ" "$@" <<'PY'
import io, os, re, sys

docs, req_path = sys.argv[1], sys.argv[2]
args = sys.argv[3:]
# 只拦"需求版本" v1.x；放过三段式发行版本 v1.4.2 / v1.10.3（那是 Git tag / 镜像 tag，
# 与需求版本是两套编号。不排除会把发布流水线整条打红——误伤的门禁一定会被关掉）。
# (?!\.\d) 排除 v1.4.2 的 v1.4；(?<![0-9A-Za-z.]) 排除 xv1.4 / 1.v1.4
PAT = re.compile(r'(?<![0-9A-Za-z.])v1\.\d+(?!\.\d)')
if not os.path.isdir(docs):
    print('找不到 docs 目录: %s' % docs, file=sys.stderr); sys.exit(2)

def read(p):
    return io.open(p, encoding='utf-8', errors='replace').read().split('\n')

def ranges_for_req(lines):
    """豁免区间靠结构标记推导，不硬编码行号（01 会一直变长）。"""
    hdr_end = chg_start = chg_end = None
    app_start = None
    for i, l in enumerate(lines, 1):
        if hdr_end is None and re.match(r'^> \*\*v1\.\d+ 变更', l):
            hdr_end, chg_start = i - 1, i
        if chg_start and chg_end is None and l.startswith('> **本文的写法约定'):
            chg_end = i - 1
        if app_start is None and l.startswith('## 附录 B'):
            app_start = i
    rs = []
    if hdr_end:  rs.append((1, hdr_end, '文件头（版本号是主语）'))
    if chg_end:  rs.append((chg_start, chg_end, '变更记录区（追溯区）'))
    if app_start: rs.append((app_start, len(lines), '附录 B（被描述对象）'))
    return rs

def in_ranges(i, rs):
    return any(a <= i <= b for a, b, _ in rs)

if '--list-exempt' in args:
    if os.path.isfile(req_path):
        rs = ranges_for_req(read(req_path))
        print('文件: %s' % req_path)
        for a, b, why in rs:
            print('  豁免 %d:%d  %s' % (a, b, why))
    print('正则: %s' % PAT.pattern)
    print('其余文件全文视为正文（含 00/02/03/04/05/06 与 architecture/）')
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
    rs = ranges_for_req(lines) if os.path.abspath(f) == os.path.abspath(req_path) else []
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
  → 若某行确实是被描述的对象（台账 / 作废清单 / 历史对照），把它放进 01 的豁免区间，
    而不是在本脚本里加白名单；白名单是"感觉"，行区间是"结构"。
""" % total)
    sys.exit(1)

print('OK: docs 正文无版本号引用（豁免区间见 --list-exempt）')
sys.exit(0)
PY
