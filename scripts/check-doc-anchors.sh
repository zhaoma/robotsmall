#!/usr/bin/env bash
# =============================================================================
# 文档锚点门禁：脚本与文档里引用的 `docs/NN §x.y` 节号必须真实存在
#
# 用法：
#   ./scripts/check-doc-anchors.sh              # 扫 scripts/*.sh 与 docs/**/*.md 里的 docs/NN §x.y 引用
#   ./scripts/check-doc-anchors.sh --self-test  # fail-before-pass 自证（正样本绿 + 两个注入红）
#
# 退出码：见 `docs/05 §4.6`（定义只在那里，本文件不复述）。
#
# -----------------------------------------------------------------------------
# 为什么需要这条
# -----------------------------------------------------------------------------
# 本轮把"退出码含义"的定义收进 `docs/05 §4.6` 一个地方，四个脚本头部改成指针。
# 指针少写一遍，就多一处**会静默失效的引用**：文档改了节号（§4.6 → §4.7），
# 脚本还指着 §4.6，而它照样能跑、照样绿 —— 下一个人顺着指针翻过去，
# 看到的是别的内容，然后他有两种反应：按错误的节理解，或者认为文档坏了。
# 两种都比"缺一节"更贵。
#
# 这类漂移**没有任何运行时症状**，只能靠结构断言挡。所以这里不检查"文案对不对"，
# 只检查一件可机械判定的事：**被引用的节号在文档里存在**。
# =============================================================================
set -uo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$ROOT" || exit 2

exec python3 - "$@" <<'PY'
# -*- coding: utf-8 -*-
import io, os, re, sys, glob, tempfile, shutil

ROOT = os.getcwd()

# `docs/05 §4.6` / `docs/05-部署运维.md §4.6` / `docs/01 §6 F4` 都算引用；
# 节号支持 4 / 4.6 / 2.6.1 三级。
# 第 2 组是**文件名后缀**（含前导 `-`），它是"写全名时按全名核"的唯一依据 ——
# 不捕获它就只能拿两位编号去 glob，而本仓有两个 `02-*` 文件
# （`02-数据模型.md` / `02-数据模型-M1落地对照表.md`，后者排序在前），
# 于是 `docs/02-数据模型.md §7.3.4` 会被解析到对照表上、报"该文档里没有这个节号"。
# 那条报错本身是对的（引用的文件确实没被核到），错的是**核错了文件**。
REF = re.compile(r'docs/(\d{2})((?:-[^\s`\'"§]*?)?)\s*§\s*(\d+(?:\.\d+)*)')
# 另一种真实存在的写法：反引号包两位编号再跟节号，例如 `00` §5.4.3。
# 它以前完全没被扫到（39 条实测、0 条失效，所以接进来不会立刻制造一片假红）。
# **注意这里不能只按两位编号取"第一篇"文档**：本仓同时有 docs/00-项目交付方案.md
# 与 docs/architecture/00-architecture.md，后者才有 §5.4.3。曾经有人（包括我）
# 只看了前者就报"悬空锚"——那正是本脚本方向五/六已经防住的坑，报的人自己又踩了一遍。
BT = re.compile(r'`(\d{2})`\s*§\s*(\d+(?:\.\d+)*)')

def add_refs(p, lineno, line, out):
    for m in REF.finditer(line):
        out.append((p, lineno, m.group(1), m.group(2), m.group(3), m.group(0)))
    for m in BT.finditer(line):
        out.append((p, lineno, m.group(1), '', m.group(2), m.group(0)))

def scan_refs(root):
    """扫 scripts/*.sh **与** docs/**/*.md。

    为什么必须扫 docs：docs→docs 的显式引用（`docs/05 §4.6`）以前没有任何门禁 ——
    本脚本只扫 scripts/，于是一份文档可以引另一份文档里根本不存在的节号，
    而两个文件各自都过门禁。这正是 `06` 里"退出码出处"那两处写法分叉的来源。

    **只扫带 `docs/NN` 前缀的显式引用**。正文里大量裸 `§x.y`（实测 1730 处）语义上是
    "本节文档自己的某节"，但本仓有 247 处裸引用指向的是**别的文档**的节
    （如 `02-数据模型-M1落地对照表.md` 里的 `§6.2` 指 `02-数据模型.md`），
    用"裸引用 = 本文档"去判会制造 247 条假红。**不能判的东西就不要假装判了** ——
    它的处置是写全名，不是把猜测写进门禁。
    """
    out = []
    for p in sorted(glob.glob(os.path.join(root, 'scripts', '*.sh'))):
        for i, line in enumerate(io.open(p, encoding='utf-8'), 1):
            add_refs(os.path.basename(p), i, line, out)
    d = os.path.join(root, 'docs')
    for dp, _, names in os.walk(d):
        for n in sorted(names):
            if not n.endswith('.md'):
                continue
            p = os.path.join(dp, n)
            for i, line in enumerate(io.open(p, encoding='utf-8'), 1):
                add_refs(os.path.relpath(p, root), i, line, out)
    return out

def doc_candidates(prefix, suffix, root=ROOT):
    """返回候选文档路径列表。

    写全名（`docs/02-数据模型.md`）→ **只核那一个文件**，不参与 glob。
    只写编号（`docs/02`）→ 所有 `02-*.md` 都算候选，**任一命中即视为解析得到**。
        为什么"任一"：短名对读者本来就是模糊的，这条门禁判的是"这个引用悬不悬空"，
        而不是"作者心里想的是哪一篇"。要精确就写全名。
    """
    if suffix:
        p = os.path.join(root, 'docs', prefix + suffix)
        return [p] if os.path.isfile(p) else []
    # 必须按传入的 root 走，**不能用全局 ROOT**：`--self-test` 在临时目录里造样本，
    # 若这里读真实 docs/，注入"文档把 §4.6 改成 §4.7"会因为真实 docs/ 里仍有 §4.6
    # 而永远不红 —— 自证当场变成恒绿（本仓已出现过一次，就是这次改动的第一版）。
    d = os.path.join(root, 'docs')
    out = []
    for dirpath, _, names in os.walk(d):
        for n in names:
            if n.endswith('.md') and n.startswith(prefix + '-'):
                out.append(os.path.join(dirpath, n))
    return sorted(out)

HEADING = re.compile(r'^#{1,5}\s*(\d+(?:\.\d+)*)[.\s、]')

def headings(path):
    got = set()
    for line in io.open(path, encoding='utf-8'):
        m = HEADING.match(line)
        if m:
            got.add(m.group(1))
    return got

def judge(root):
    """返回 (失败列表, 引用总数)"""
    failures = []
    refs = scan_refs(root)
    cache = {}
    for script, line, prefix, suffix, num, raw in refs:
        cands = doc_candidates(prefix, suffix, root)
        if not cands:
            failures.append(('%s:%d' % (script, line),
                             '引用了 docs/%s%s 的 §%s，但 docs/ 下没有这个文件（%s）'
                             % (prefix, suffix, num, raw)))
            continue
        hit = False
        for path in cands:
            if path not in cache:
                cache[path] = headings(path)
            if num in cache[path]:
                hit = True
                break
        if not hit:
            failures.append(('%s:%d' % (script, line),
                             '引用了 %s §%s，但该文档里没有这个节号（%s）'
                             % (os.path.relpath(cands[0], root), num, raw)))
    return failures, len(refs)

def report(failures, total):
    for loc, msg in failures:
        print('%s ANCHOR_MISSING: %s' % (loc, msg))
    if failures:
        print('FAILED: %d 处（扫到 %d 条 docs/NN §x.y 引用）' % (len(failures), total))
        return 1
    print('OK: %d 条 docs/NN §x.y 引用全部解析得到' % total)
    return 0

# ---------------------------------------------------------------- 自证
SAMPLE_DOC = """# 示例文档

## 4. CI/CD

### 4.6 门禁脚本的退出码契约

正文。
"""

# 这两个样本字符串**故意从两段拼起来**：本脚本自己也落在扫描范围内，如果这里写成
# 完整的一行 `docs/05 §x`，自证样本就会被当成"真实引用"参与扫描 —— 那么 §9.99 那条
# 注入样本会让真实扫描永远红（第一次跑就是这样：报了自己 122 行）。这不是白名单，
# 是让"样本"与"被扫描的引用"在字面上分开。
SAMPLE_SH_OK = ("#!/usr/bin/env bash\n# 退出码：见 `docs/0" "5 §4.6`\n")

SAMPLE_SH_BAD = ("#!/usr/bin/env bash\n# 退出码：见 `docs/0" "5 §9.99`\n")

def write_sample(root, rel, text):
    p = os.path.join(root, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    io.open(p, 'w', encoding='utf-8').write(text)
    return p

def self_test():
    checks = []

    def check(ok, detail):
        checks.append((bool(ok), detail))

    tmp = tempfile.mkdtemp(prefix='docanchors-selftest-')
    try:
        # 方向一：正样本绿
        ok_dir = os.path.join(tmp, 'ok')
        write_sample(ok_dir, 'docs/05-示例.md', SAMPLE_DOC)
        write_sample(ok_dir, 'scripts/a.sh', SAMPLE_SH_OK)
        f1, t1 = judge(ok_dir)
        check(not f1 and t1 == 1,
              '正样本（script 指向 §4.6，文档有该节）判绿：%d 条引用、%d 处失败' % (t1, len(f1)))

        # 方向二：文档没有这一节 → 必须红（这是"文档改了节号"的真实形态）
        gap_dir = os.path.join(tmp, 'gap')
        write_sample(gap_dir, 'docs/05-示例.md', SAMPLE_DOC.replace('### 4.6', '### 4.7'))
        write_sample(gap_dir, 'scripts/a.sh', SAMPLE_SH_OK)
        f2, _ = judge(gap_dir)
        check(len(f2) == 1 and 'ANCHOR' not in f2[0][1] and '§4.6' in f2[0][1],
              '注入：文档把 §4.6 改成 §4.7 → 红并点名 §4.6：%s'
              % (f2[0][1][:60] if f2 else '未报红'))

        # 方向三：脚本指向不存在的节 → 必须红
        bad_dir = os.path.join(tmp, 'bad')
        write_sample(bad_dir, 'docs/05-示例.md', SAMPLE_DOC)
        write_sample(bad_dir, 'scripts/a.sh', SAMPLE_SH_BAD)
        f3, _ = judge(bad_dir)
        check(len(f3) == 1 and '§9.99' in f3[0][1],
              '注入：脚本引用 §9.99（文档没有）→ 红并点名 §9.99：%s'
              % (f3[0][1][:60] if f3 else '未报红'))

        # 方向四：docs 里没有这个编号的文件 → 必须红（防"引用了一个不存在文档的编号"）
        nodoc_dir = os.path.join(tmp, 'nodoc')
        write_sample(nodoc_dir, 'docs/06-示例.md', SAMPLE_DOC)
        write_sample(nodoc_dir, 'scripts/a.sh', SAMPLE_SH_OK)
        f4, _ = judge(nodoc_dir)
        check(len(f4) == 1 and '05' in f4[0][1],
              '注入：只存在 docs/06 却引用 docs/05 → 红：%s' % (f4[0][1][:60] if f4 else '未报红'))

        # 方向五/六：同一个编号下有两篇文档时，**写全名必须按全名核**。
        # 现实来源：本仓 `02-数据模型.md` 与 `02-数据模型-M1落地补充表/对照表` 并存，
        # 后者排序在前 —— 旧实现只取两位编号去 glob、拿第一个，于是
        # `docs/02-数据模型.md §7.3.4` 被解析到对照表上，报"该文档里没有这个节号"。
        # 那条红是**假红**：引用写得完全正确，核错了文件。
        two_dir = os.path.join(tmp, 'two')
        write_sample(two_dir, 'docs/02-A.md', SAMPLE_DOC)                       # 有 §4.6
        write_sample(two_dir, 'docs/02-B.md', '# 另一篇\n\n### 9.9 别处才有的节\n')
        write_sample(two_dir, 'scripts/a.sh',
                     '#!/usr/bin/env bash\n# 见 `docs/0' '2-A.md §4.6`\n')
        f5, _ = judge(two_dir)
        check(not f5, '同编号两篇文档时，写全名按全名核（§4.6 在 02-A 里）→ 绿：%s'
              % (f5[0][1][:60] if f5 else '绿'))

        # 方向六是方向五的反面，缺了它"写全名"可以退化成"glob 任一篇"还照样绿：
        # 引用 02-A 里**没有**、只有 02-B 有的节号 → 必须红。
        two_bad = os.path.join(tmp, 'twobad')
        write_sample(two_bad, 'docs/02-A.md', SAMPLE_DOC)
        write_sample(two_bad, 'docs/02-B.md', '# 另一篇\n\n### 9.9 别处才有的节\n')
        write_sample(two_bad, 'scripts/a.sh',
                     '#!/usr/bin/env bash\n# 见 `docs/0' '2-A.md §9.9`\n')
        f6, _ = judge(two_bad)
        check(len(f6) == 1 and '§9.9' in f6[0][1],
              '注入：写全名但该节只在同编号的另一篇里 → 必须红（证明没有退回 glob）：%s'
              % (f6[0][1][:60] if f6 else '未报红'))

        # 方向七：**docs 里的显式引用也要被扫到**。以前的门禁只扫 scripts/*.sh，
        # 文档 A 引文档 B 的悬空节号两边各自都绿。若少了这条注入，
        # "scan_refs 里删掉 docs 那一半"不会让任何自证变红。
        doc_dir = os.path.join(tmp, 'docref')
        write_sample(doc_dir, 'docs/05-示例.md', SAMPLE_DOC)
        write_sample(doc_dir, 'docs/06-调用方.md',
                     '# 调用方\n\n退出码见 `docs/0' '5 §9.99`。\n')
        f7, t7 = judge(doc_dir)
        check(len(f7) == 1 and '§9.99' in f7[0][1] and t7 == 1,
              '注入：docs 内引用不存在的 §9.99 → 必须红（证明 docs 也在扫描范围内）：%s'
              % (f7[0][1][:60] if f7 else '未报红'))

        # 方向八/九：反引号包编号的写法也要扫。样本仍从两段拼，避免本文件自己被扫到。
        # 少了方向九，"BT 正则写错/没接进 add_refs"不会让任何自证变红。
        bt_ok = os.path.join(tmp, 'bt')
        write_sample(bt_ok, 'docs/05-示例.md', SAMPLE_DOC)
        write_sample(bt_ok, 'docs/06-调用方.md', '# 调用方\n\n闸门见 `' '05` §4.6`。\n')
        f8, t8 = judge(bt_ok)
        check(not f8 and t8 == 1,
              '反引号式引用指向真实节（§4.6）→ 绿：%d 条引用、%d 处失败' % (t8, len(f8)))
        bt_bad = os.path.join(tmp, 'btbad')
        write_sample(bt_bad, 'docs/05-示例.md', SAMPLE_DOC)
        write_sample(bt_bad, 'docs/06-调用方.md', '# 调用方\n\n闸门见 `' '05` §9.99`。\n')
        f9, _ = judge(bt_bad)
        check(len(f9) == 1 and '§9.99' in f9[0][1],
              '注入：反引号式引用指向不存在的 §9.99 → 红并点名：%s'
              % (f9[0][1][:60] if f9 else '未报红'))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    for ok, detail in checks:
        print('  %s  %s' % ('ok  ' if ok else 'FAIL', detail))
    failed = [d for ok, d in checks if not ok]
    if failed:
        print('== 自证失败：%d 项不符合预期' % len(failed))
        return 1
    print('== 自证通过：%d 项（正样本绿 / 文档改节号红 / 脚本引用不存在节红 / 跨文档编号缺失红'
          ' / 同编号两篇时全名按全名核 / 全名指错篇必红 / docs 内引用也在扫描范围内'
          ' / 反引号式引用绿与红）' % len(checks))
    return 0

args = sys.argv[1:]
if '--self-test' in args:
    sys.exit(self_test())
for a in args:
    if a.startswith('-'):
        print('unknown arg: %s' % a, file=sys.stderr)
        sys.exit(3)

failures, total = judge(ROOT)
sys.exit(report(failures, total))
PY
