#!/usr/bin/env bash
# =============================================================================
# 文档锚点门禁：脚本里引用的 `docs/NN §x.y` 节号必须真实存在
#
# 用法：
#   ./scripts/check-doc-anchors.sh              # 扫 scripts/*.sh 里的 docs/NN §x.y 引用
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
REF = re.compile(r'docs/(\d{2})(?:-[^\s`\'"§]*?)?\s*§\s*(\d+(?:\.\d+)*)')

def md_files():
    out = []
    for dirpath, _, names in os.walk(os.path.join(ROOT, 'docs')):
        for n in names:
            if n.endswith('.md'):
                out.append(os.path.join(dirpath, n))
    return sorted(out)

def doc_for(prefix):
    """docs/05 → docs/05-部署运维.md（按两位编号前缀找，不写死文件名）"""
    hits = [p for p in md_files() if os.path.basename(p).startswith(prefix + '-')]
    return hits[0] if hits else None

HEADING = re.compile(r'^#{1,5}\s*(\d+(?:\.\d+)*)[.\s、]')

def headings(path):
    got = set()
    for line in io.open(path, encoding='utf-8'):
        m = HEADING.match(line)
        if m:
            got.add(m.group(1))
    return got

def scan_scripts(root):
    out = []
    for p in sorted(glob.glob(os.path.join(root, 'scripts', '*.sh'))):
        for i, line in enumerate(io.open(p, encoding='utf-8'), 1):
            for m in REF.finditer(line):
                out.append((os.path.basename(p), i, m.group(1), m.group(2), m.group(0)))
    return out

def judge(root):
    """返回 (失败列表, 引用总数)"""
    failures = []
    refs = scan_scripts(root)
    cache = {}
    for script, line, prefix, num, raw in refs:
        path = doc_for(prefix) if root == ROOT else _doc_for(root, prefix)
        if path is None:
            failures.append(('%s:%d' % (script, line),
                             '引用了 docs/%s 的 §%s，但 docs/ 下没有以 %s- 开头的文件（%s）'
                             % (prefix, num, prefix, raw)))
            continue
        if path not in cache:
            cache[path] = headings(path)
        if num not in cache[path]:
            failures.append(('%s:%d' % (script, line),
                             '引用了 %s §%s，但该文档里没有这个节号（%s）'
                             % (os.path.relpath(path, root), num, raw)))
    return failures, len(refs)

def _doc_for(root, prefix):
    d = os.path.join(root, 'docs')
    if not os.path.isdir(d):
        return None
    for n in sorted(os.listdir(d)):
        if n.endswith('.md') and n.startswith(prefix + '-'):
            return os.path.join(d, n)
    return None

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
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    for ok, detail in checks:
        print('  %s  %s' % ('ok  ' if ok else 'FAIL', detail))
    failed = [d for ok, d in checks if not ok]
    if failed:
        print('== 自证失败：%d 项不符合预期' % len(failed))
        return 1
    print('== 自证通过：%d 项（正样本绿 / 文档改节号红 / 脚本引用不存在节红 / 跨文档编号缺失红）'
          % len(checks))
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
