#!/usr/bin/env bash
# =============================================================================
# 裁决落地核查：台账说"落在 §X.Y"，§X.Y 里就得真的提过这件事
#
# 用法：
#   ./scripts/check-ledger-landing.sh              # 默认核 docs/03 的修订记录台账
#   ./scripts/check-ledger-landing.sh --strict     # 目标级未命中（任一落点没提）也判红
#   ./scripts/check-ledger-landing.sh --quiet      # 折叠"命中行原文"与明细，只打汇总与红项
#   ./scripts/check-ledger-landing.sh --list       # 只列判据，不执行
#   ./scripts/check-ledger-landing.sh --self-test  # fail-before-pass 自证（12 个样本）
#
# 退出码：见 `docs/05 §4.6`（0 全绿 / 1 有判据失败 / 2 未验证（前置缺失或空集）/ 3 用法错误）
#
# -----------------------------------------------------------------------------
# 这个脚本能证明什么、不能证明什么（先读这段，否则它的绿一定会被用错）
# -----------------------------------------------------------------------------
# 能证明：台账某行声明"落在 §2.5"，而 §2.5 那一段的行范围内**出现过**该行的代码标识符。
# 不能证明：那句话说的是台账那个意思。
#
# 本轮真实发生过的三类事故，**它一条都挡不住**，别拿它的绿去支撑它们：
#   ① 语义相反：台账写"只比对不回写"，正文两处还写着回写。标识符 `CanSellConsistencyJob`
#      在 §2.7 出现过 —— 本判据判绿。语义核只能靠人读。
#   ② 号过期：正文写 `M2`，实际该是 `M3`。`M2` 是合法标识符，出现过就绿。号的真源是
#      `docs/00` 的里程碑表，得用"从真源取号再比对"的脚本，不是这个。
#   ③ 跨文件互斥：`details.policy` 在 `03` 撤了、`06` 还写三字段、`openapi` 两字段。
#      单文件内部怎么核都发现不了"三份文档互斥"，得三向对照。
#
# 它挡的是**第四类**，也是本轮真实存在过的：落点声明了、那一节里根本没提这件事
# （多节落点只落了一处 / 改了节号 / 加了台账行忘了写正文）。
# 这一类判据很窄，但它是机械的、零成本的、且**人肉做不完**（52 行 × 平均 2.8 个落点 = 144 个目标）。
#
# -----------------------------------------------------------------------------
# 四条别退化的纪律（每条都对应一次现实的坑）
# -----------------------------------------------------------------------------
# 1) 空集不许绿。台账 0 行、或可核行数为 0（全抽不出标识符）→ rc=2，「查了 0 条」单独成句。
#    "核了 0 条"与"核了 45 条全过"不许是同一句话。
# 2) 抽不出关键词的行**必须计数并列原文**。`**一次性安装令牌**` 这类中文加粗短语抽不出
#    标识符 —— 这个数字是信息（它告诉你这套台账里有多少条天生不可机器核），
#    静默跳过会让"不可核"看起来像"已核过"。
# 3) **不静默降级成全文件 grep。** 落点声明的正是"在哪一节"，降级成全文件搜会让
#    "在别处提过"与"在这一节说过"混为一谈 —— 那正是本判据唯一要区分的东西。
#    节号解析不到时按"节不存在"判红（L2），不是缩回全文搜。
# 4) **命中只印原文，不下结论。** `不是一天` 与 `是一天` 都含 `一天`；穷举否定形式
#    是不可能的，所以每个命中都把它所在的那行原文印出来，由人判断肯定/否定。
#    脚本替人下"已落地"的结论，就是本判据最容易变成假绿的地方。
#
# -----------------------------------------------------------------------------
# 为什么关键词只取反引号
# -----------------------------------------------------------------------------
# 加粗短语（`**四档状态码定稿**`）是散文不是标识符。实测它在本文件里只命中台账自己那一行，
# 拿它当关键词等于**天生为绿**（永远能在台账里找到自己）。所以只取反引号标识符，
# 抽不出的行进"不可核"并逐行列出。这条是我先按"反引号+加粗"跑了一版、量出 46 个假未命中
# 之后改的 —— 判据是先量过的，不是先写死的。
# =============================================================================
set -uo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$ROOT" || exit 2

# `--list` / `--help` 在**取临时文件之前**处理：它们只读、不该依赖 python3，
# 也不该因为一个 mktemp 失败就报"用法错误"。判据清单本身就是一个要能被引用的东西。
for a in "$@"; do
  case "$a" in
    --list)
      echo "== check-ledger-landing 判据清单（与实跑同一份，改判据必须两处同时改） =="
      echo "  [L1] 台账可解析：表头在、列数=5、数据行 > 0           失败 rc=2（核不了，不是核过了）"
      echo "  [L2] 落点声明的节号在文档标题里存在                   失败 rc=1（引用漂移）"
      echo "  [L3] 落点节内出现过该行的代码标识符                   失败 rc=1（--strict 降到目标级）"
      echo "  [L4] 不可核的行必须计数并列原文；可核行数为 0 判红     失败 rc=2（空集绿）"
      exit 0 ;;
    -h|--help) sed -n '2,18p' "$0"; exit 0 ;;
  esac
done

if ! command -v python3 >/dev/null 2>&1; then
  printf 'PRECONDITION: 缺 python3（本脚本用 python3 解析 markdown 表格与标题树）→ 未验证，不是通过\n' >&2
  exit 2
fi

# 模板必须以 X 结尾：BSD mktemp 不认 `x.XXXXXX.py`，会当成字面名 —— 第一次能建，
# 第二次就 "File exists"，而症状是"脚本静默没有输出"（自证里就是这么撞出来的）。
PYCORE=$(mktemp "${TMPDIR:-/tmp}/ledger-landing.XXXXXX") || exit 2
FIXTURE_DIR=""
cleanup() { rm -f "$PYCORE"; [ -n "$FIXTURE_DIR" ] && rm -rf "$FIXTURE_DIR"; }
trap cleanup EXIT INT TERM

cat > "$PYCORE" <<'PY'
# -*- coding: utf-8 -*-
import io, os, re, sys, datetime

DOC = os.environ.get('LL_DOC', 'docs/03-后端设计.md')
API = os.environ.get('LL_API', 'openapi/openstore.yaml')
QUIET = os.environ.get('LL_QUIET') == '1'
STRICT = os.environ.get('LL_STRICT') == '1'
BASE = os.environ.get('LL_BASE', '?')
BASE_SUB = os.environ.get('LL_BASE_SUB', '')

def die(msg):
    print(msg)
    sys.exit(2)

def read(p):
    if not os.path.exists(p):
        die('PRECONDITION: 文件不存在：%s → 未验证，不是通过' % p)
    return io.open(p, encoding='utf-8').read()

def meta(p):
    t = datetime.datetime.fromtimestamp(os.path.getmtime(p)).strftime('%Y-%m-%d %H:%M:%S')
    n = io.open(p, encoding='utf-8').read().count('\n') + 1
    return '%s  行数=%s  mtime=%s' % (p, n, t)

doc = read(DOC)
lines = doc.split('\n')

# ---- 标题树：节号 → 行范围 -------------------------------------------------
HEAD = re.compile(r'^(#{1,5})\s+(\d+(?:\.\d+)*)[.、\s]')
heads = []
for i, ln in enumerate(lines):
    m = HEAD.match(ln)
    if m:
        heads.append((m.group(2), len(m.group(1)), i))

def section_range(num):
    for k, (n, lv, i) in enumerate(heads):
        if n == num:
            end = len(lines)
            for n2, lv2, i2 in heads[k + 1:]:
                if lv2 <= lv:
                    end = i2
                    break
            return i, end
    return None

# ---- 台账：只取从 #1 开始的那一段连续行 -------------------------------------
led_header_ln = None
for i, ln in enumerate(lines):
    if re.match(r'^\s*>?\s*\|\s*#\s*\|', ln) and '落点' in ln:
        led_header_ln = i
        break
if led_header_ln is None:
    die('GATE FAILED: ledger_parse (L1)\n'
        '  文档里找不到修订记录表头（期望形如 `> | # | 来源 | 变更 | 本文落点 | 实质改动 |`）。\n'
        '  表头被改名/挪进代码块都会走到这里 —— 这是"核不了"，不是"核过了"。')

def cells(ln):
    b = re.sub(r'^[^|]*\|', '', ln).strip()      # 去掉前导 `> ` 与第一个 |
    if b.endswith('|'):
        b = b[:-1]
    return [c.strip() for c in b.replace('\\|', '\x00').split('|')]

rows = []
for i in range(led_header_ln + 1, len(lines)):
    m = re.match(r'^\s*>?\s*\|\s*(\d+)\s*\|', lines[i])
    if not m:
        if rows:
            break
        continue                                  # 跳过分隔行 `|---|`
    n = int(m.group(1))
    if rows and n <= rows[-1][0]:
        break                                     # 编号回头 = 下一张表，停
    rows.append((n, i, lines[i]))

if not rows:
    die('GATE FAILED: ledger_parse (L1)\n'
        '  查了 0 条台账行。\n'
        '  表头在、数据行 0 条 —— 可能真是空表，也可能表被整段删了。两种都不该判绿：\n'
        '  空集绿是这类门禁最常见的死法。')

bad_cols = [(n, len(cells(ln))) for n, i, ln in rows if len(cells(ln)) != 5]
if bad_cols:
    print('GATE FAILED: ledger_parse (L1)')
    print('  台账列数不是 5（# | 来源 | 变更 | 本文落点 | 实质改动）：')
    for n, c in bad_cols[:10]:
        print('    #%d 实测 %d 列' % (n, c))
    sys.exit(1)

# ---- 关键词与落点 -----------------------------------------------------------
KW = re.compile(r'`([^`]+)`')
SEC = re.compile(r'§\s*(\d+(?:\.\d+)*)')
SPLIT = re.compile(r'\s+/\s+|、|，|,')
CJK = re.compile(r'[\u4e00-\u9fff]')

def norm(s):
    """比对前把空白全去掉：文档里写 `count = 0`、台账里写 `count=0`，是同一件事。
    这条是实测撞出来的 —— 加空格差异让 #15 假红了一次，而假红和假绿一样会让人不再看输出。"""
    return re.sub(r'\s+', '', s)

def keywords(chg):
    """只留"标识符样"的 token；带空格又带中文的是散文短语（如 `分类页首屏 SQL ≤ 8`），
    它在正文里通常换了说法，拿它当关键词只会产生假红。这类行降级成"不可核"。"""
    ids, phrases = [], []
    for span in KW.findall(chg):
        for t in SPLIT.split(span):
            t = t.replace('**', '').strip()
            if not (2 <= len(t) <= 40) or t in ids or t in phrases:
                continue
            (phrases if (' ' in t and CJK.search(t)) else ids).append(t)
    return ids, phrases

api_text = read(API) if os.path.exists(API) else ''

print('== 裁决落地核查：台账 → 正文落点 ==')
print('基线：commit=%s %s' % (BASE, BASE_SUB))
print('      %s' % meta(DOC))
if os.path.exists(API):
    print('      %s' % meta(API))
print()

l2_fail, l3_fail, target_miss, unkeyworded, shown_hits = [], [], [], [], []
checked = targets = hits = 0

for n, i, ln in rows:
    c = cells(ln)
    chg, where = c[2], c[3]
    kws, phrases = keywords(chg)
    secs = SEC.findall(where)
    wants_api = 'openapi' in where
    if not kws:
        note = '  ← 只有散文短语，抽不出标识符：' + ' / '.join(phrases) if phrases else ''
        unkeyworded.append((n, chg + note))
        continue
    if not secs and not wants_api:
        unkeyworded.append((n, chg + '  ← 落点列也抽不出 §节号'))
        continue
    checked += 1
    row_hit = False
    for s in secs:
        targets += 1
        r = section_range(s)
        if r is None:
            l2_fail.append((n, s))
            continue
        body = norm('\n'.join(lines[r[0]:r[1]]))
        hit_k = [k for k in kws if norm(k) in body]
        if hit_k:
            hits += 1
            row_hit = True
            # 只印原文，不下结论：命中的那一行原样给出，肯定/否定由人判断（纪律 4）
            for j in range(r[0], r[1]):
                if norm(hit_k[0]) in norm(lines[j]):
                    shown_hits.append((n, '§' + s, hit_k[0], lines[j].strip()[:110]))
                    break
        else:
            elsewhere = []
            for k in kws:
                cnt = norm(doc).count(norm(k))
                if cnt:
                    first = next(j for j, l in enumerate(lines) if norm(k) in norm(l))
                    elsewhere.append('%s（全文 %d 次，首个 L%d）' % (k, cnt, first + 1))
            target_miss.append((n, '§' + s, kws[:3], elsewhere[:2]))
    if wants_api:
        targets += 1
        if any(norm(k) in norm(api_text) for k in kws):
            hits += 1
            row_hit = True
        else:
            target_miss.append((n, 'openapi', kws[:3], []))
    if not row_hit:
        l3_fail.append((n, where, kws[:3]))

print('口径：台账 %d 行｜抽得出标识符 %d 行｜落点目标 %d 个（命中 %d / 未命中 %d）'
      % (len(rows), len(rows) - len(unkeyworded), targets, hits, targets - hits))
print('      --strict 下"单个落点没命中"也判红；默认只在整行零命中时判红')
print()

if shown_hits:
    if QUIET:
        print('  命中原文 %d 行（--quiet 已折叠；去掉 --quiet 看原文，别只看计数）' % len(shown_hits))
    else:
        print('  命中原文（只印这一行，不下结论 —— 肯定/否定由人判断）：')
        for n, s, k, txt in shown_hits:
            print('    #%-3d %-8s [%s] %s' % (n, s, k, txt))
    print()

if unkeyworded:
    print('  [不可核 %d 行] 变更列没有反引号标识符 —— 判据看不进去，这不等于"已核过"' % len(unkeyworded))
    for n, chg in unkeyworded:
        print('    #%-3d 变更列：%s' % (n, chg.strip()[:70]))
    print()

if target_miss:
    if QUIET and not STRICT:
        print('  目标级未命中 %d 个（--quiet 已折叠；去掉 --quiet 或加 --strict 查看明细）'
              % len(target_miss))
    else:
        print('  目标级未命中 %d 个%s：'
              % (len(target_miss), '（--strict：判红）' if STRICT else '（明细，默认不判红）'))
        for n, s, kws, elsewhere in target_miss:
            hint = ('｜' + '；'.join(elsewhere)) if elsewhere else '｜全文一次都没出现'
            print('    #%-3d %-8s 找不到 %s%s' % (n, s, ' / '.join(kws), hint))
    print()

if l2_fail:
    print('GATE FAILED: ledger_section_missing (L2)')
    print('  落点声明了一个文档里不存在的节 —— 这是引用漂移，不是内容缺失：')
    for n, s in l2_fail:
        print('    #%d 落点声明 §%s，但标题树里没有这一节（节号是不是改过？）' % (n, s))
    print()

if l3_fail:
    print('GATE FAILED: ledger_not_landed (L3)')
    print('  台账说落在这些节里，但这些节里一个标识符都找不到：')
    for n, where, kws in l3_fail:
        print('    #%-3d 落点=%s  找不到 %s' % (n, where.strip()[:48], ' / '.join(kws)))
    print()

if checked == 0:
    print('GATE FAILED: ledger_coverage (L4)')
    print('  查了 0 条可核台账行（%d 行全抽不出反引号标识符）。' % len(rows))
    print('  判据在这个文档上等于没运行 —— 这时它的绿不能当"台账已核查"用。')
    sys.exit(2)

# --strict：每个落点都要命中。默认档不判红不是"宽容"，是因为首次接入时 26/131 个落点
# 是历史存量，一次全红只会让人把脚本关掉（本轮 ⑤-a/⑤-b 的裁决就是这个道理）。
# 但红必须**有 GATE FAILED 行**：没有规则名的失败，日志里就是一段普通输出。
if STRICT and target_miss:
    print('GATE FAILED: ledger_target_miss (L3 --strict)')
    print('  --strict 下每个落点都要命中；上面 %d 条未命中任一即红。' % len(target_miss))
    print()

if l2_fail or l3_fail or (STRICT and target_miss):
    sys.exit(1)

print('结论：L1 ok｜L2 ok｜L3 ok｜L4 ok（%d 行可核 / %d 行不可核，不可核的已逐行列出）'
      % (checked, len(unkeyworded)))
print('      本判据只证明「落点那一节里提过这个名字」，不证明「说的是那个意思」。')
print('      语义相反 / 号过期 / 跨文件互斥 —— 这三类它一条都挡不住，别拿它的绿去支撑它们。')
sys.exit(0)
PY

# ---------------------------------------------------------------------------
# 自证：先造正样本（必须绿），再逐条注入（必须红在指定判据上）
# 自证只用临时目录里的合成文档，不读也不改仓库里的真文档。
# ---------------------------------------------------------------------------
self_test() {
  local total=0 pass=0 fail=0
  FIXTURE_DIR=$(mktemp -d "${TMPDIR:-/tmp}/ledger-selftest.XXXXXX") || exit 2
  local D="$FIXTURE_DIR"

  # 正样本：2 行台账，落点都在、标识符都在
  cat > "$D/ok.md" <<'EOF'
# 样例文档

> ### 修订记录
> | # | 来源 | 变更 | 本文落点 | 实质改动 |
> |---|---|---|---|---|
> | 1 | 测试 | 新增 `AlphaThing` | §2.1 | 说明 |
> | 2 | 测试 | 删除 `BetaThing` | §2.2、openapi | 说明 |

## 1. 一节

别的节，这里也有 `AlphaThing`。

## 2.1 聚合 A

这里有 `AlphaThing` 的说明。

## 2.2 聚合 B

这里有 `BetaThing` 的说明。
EOF
  printf 'BetaThing: true\n' > "$D/ok.yaml"

  # 注入 A：把标识符从 §2.1 删掉，但 §1 里仍保留（要判红，且不许靠全文 grep 放过）
  sed 's/这里有 `AlphaThing` 的说明。/这里什么都没写。/' "$D/ok.md" > "$D/miss.md"

  # 注入 B：落点写成一个不存在的节
  sed 's/| §2.1 |/| §9.9 |/' "$D/ok.md" > "$D/nosec.md"

  # 注入 C：表头被改名（核不了）
  sed 's/| # | 来源 | 变更 | 本文落点 | 实质改动 |/| 编号 | 来源 | 变更 | 落点 | 改动 |/' \
      "$D/ok.md" > "$D/noheader.md"

  # 注入 D：表头在、数据行 0 条（空集绿防护）
  awk 'BEGIN{drop=0} /^\| 1 \|/{drop=1} /^\| 2 \|/{next} /^> \| # \||^> \|---/{print;next} /^\| 1 \|/{next} {print}' \
      "$D/ok.md" | grep -v '^> | [0-9] |' > "$D/emptytable.md"

  # 注入 E：一行抽不出反引号标识符（不判红，但必须列出来）
  sed 's/新增 `AlphaThing`/新增 **甲东西**/' "$D/ok.md" > "$D/unkey.md"

  # 注入 F：全部行都抽不出标识符（可核行数 0 → 空集绿，判红）
  sed -e 's/新增 `AlphaThing`/新增 **甲东西**/' -e 's/删除 `BetaThing`/删除 **乙东西**/' \
      "$D/ok.md" > "$D/allunkey.md"

  # 注入 G：标识符只出现在该节的否定句里（判绿，但原文必须印出来）
  sed 's/这里有 `AlphaThing` 的说明。/这里**不**采用 `AlphaThing`。/' "$D/ok.md" > "$D/negation.md"

  # 注入 H：空白差异不算未命中（台账写 `count=0`、正文写 `count = 0` 是同一件事）
  sed -e 's/新增 `AlphaThing`/新增 `Count=0` 过滤/' \
      -e 's/这里有 `AlphaThing` 的说明。/这里有 `Count = 0` 的过滤。/' \
      "$D/ok.md" > "$D/space.md"

  # 注入 I：部分落点未命中 —— 默认档只登记，--strict 下判红
  # 这一组守住的是本脚本的核心取舍（默认登记 / --strict 判红）。哪天有人"顺手"把默认档
  # 调严，全组第二天就会开始忽略这条门禁 —— 本轮 ⑤-a/⑤-b 的裁决就是这个道理。
  cat > "$D/partial.md" <<'EOF'
# 样例文档

> ### 修订记录
> | # | 来源 | 变更 | 本文落点 | 实质改动 |
> |---|---|---|---|---|
> | 1 | 测试 | 新增 `AlphaThing` | §2.1 | 说明 |
> | 2 | 测试 | 删除 `BetaThing` | §2.2、§2.3 | 说明 |

## 2.1 聚合 A

这里有 `AlphaThing` 的说明。

## 2.2 聚合 B

这里有 `BetaThing` 的说明。

## 2.3 聚合 C

这一节什么都没提。
EOF

  run_case() {  # $1 名称 $2 期望rc $3 期望文案 $4 doc $5 api $6 额外环境（如 LL_STRICT=1）
    total=$((total + 1))
    local out rc
    out=$(env LL_DOC="$4" LL_API="$5" LL_QUIET=0 LL_STRICT=0 \
          LL_BASE=selftest LL_BASE_SUB=合成样本 ${6:-} python3 "$PYCORE" 2>&1)
    rc=$?
    if [ "$rc" = "$2" ] && printf '%s' "$out" | grep -qF "$3"; then
      printf '  ok    %s → rc=%s 且含「%s」\n' "$1" "$rc" "$3"
      pass=$((pass + 1))
    else
      printf '  WRONG %s → 期望 rc=%s 且含「%s」，实得 rc=%s\n' "$1" "$2" "$3" "$rc"
      printf '%s\n' "$out" | sed 's/^/        | /' | head -24
      fail=$((fail + 1))
    fi
  }

  echo "== check-ledger-landing 自证（合成文档，不读真文档） =="
  run_case "正样本：落点都对得上 → 全绿"            0 "结论：L1 ok"            "$D/ok.md"       "$D/ok.yaml"
  run_case "注入：落点那节没提（别节仍提）→ L3 红"   1 "ledger_not_landed"      "$D/miss.md"     "$D/ok.yaml"
  run_case "注入：落点节号不存在 → L2 红（引用漂移）" 1 "ledger_section_missing" "$D/nosec.md"    "$D/ok.yaml"
  run_case "注入：台账表头被改名 → L1 判 rc=2"       2 "ledger_parse"           "$D/noheader.md" "$D/ok.yaml"
  run_case "注入：表头在但 0 条数据行 → rc=2"        2 "查了 0 条台账行"        "$D/emptytable.md" "$D/ok.yaml"
  run_case "注入：某行抽不出标识符 → 不判红但必须列出" 0 "[不可核 1 行]"          "$D/unkey.md"    "$D/ok.yaml"
  run_case "注入：全部行抽不出标识符 → 空集绿判 rc=2"  2 "ledger_coverage"        "$D/allunkey.md" "$D/ok.yaml"
  run_case "注入：标识符只在否定句里 → 绿，但印原文"   0 "这里**不**采用"          "$D/negation.md" "$D/ok.yaml"
  run_case "注入：空白差异不算未命中（count=0 vs count = 0）" 0 "结论：L1 ok"       "$D/space.md"    "$D/ok.yaml"
  run_case "部分落点未命中：默认档只登记，不判红"     0 "目标级未命中 1 个"        "$D/partial.md"  "$D/ok.yaml"
  run_case "同一份在 --strict 下判红（施压档，必须带规则名）" 1 "GATE FAILED: ledger_target_miss" "$D/partial.md" "$D/ok.yaml" "LL_STRICT=1"

  # 判据清单与实跑必须是同一份：--list 里声明的 L 编号集合 == 正样本结论行里出现的集合。
  # 这两处漂一次，清单就成了"文档写着五条、实际跑四条"。
  total=$((total + 1))
  local listed actual
  listed=$(bash "$0" --list | grep -oE '\[L[0-9]+\]' | tr -d '[]' | LC_ALL=C sort -u | tr '\n' ' ')
  actual=$(LL_DOC="$D/ok.md" LL_API="$D/ok.yaml" LL_QUIET=0 LL_STRICT=0 LL_BASE=s LL_BASE_SUB=s \
           python3 "$PYCORE" 2>&1 | grep -oE 'L[0-9]+ ok' | awk '{print $1}' | LC_ALL=C sort -u | tr '\n' ' ')
  if [ -n "$listed" ] && [ "$listed" = "$actual" ]; then
    printf '  ok    --list 的判据编号与实跑一致 → %s\n' "$listed"
    pass=$((pass + 1))
  else
    printf '  WRONG --list 与实跑不一致 → 声明=[%s] 实跑=[%s]\n' "$listed" "$actual"
    fail=$((fail + 1))
  fi

  echo
  if [ "$fail" -ne 0 ]; then
    echo "== 自证失败：$fail/$total 个样本没有红在预期断言上 =="
    echo "  判定器本身不可信 —— 此时它给出的 PASS 不能当作「台账已核查」的证据。"
    echo "GATE FAILED: ledger_landing_self_test"
    return 1
  fi
  echo "== 自证通过：$total 个样本，正样本绿、每种坏情况各自红在预期判据上 =="
  return 0
}

# ---------------------------------------------------------------------------
# 参数
# ---------------------------------------------------------------------------
DOC="docs/03-后端设计.md"
API="openapi/openstore.yaml"
export LL_QUIET=0 LL_STRICT=0

while [ $# -gt 0 ]; do
  case "$1" in
    --doc)       DOC="${2:-}"; shift 2 ;;
    --doc=*)     DOC="${1#--doc=}"; shift ;;
    --openapi)   API="${2:-}"; shift 2 ;;
    --openapi=*) API="${1#--openapi=}"; shift ;;
    --quiet)     LL_QUIET=1; shift ;;
    --strict)    LL_STRICT=1; shift ;;
    --self-test) self_test; exit $? ;;
    *) printf '用法错误：不认识参数 %s（见 --help）\n' "$1" >&2; exit 3 ;;
  esac
done

export LL_DOC="$DOC" LL_API="$API"
export LL_BASE="$(git rev-parse --short HEAD 2>/dev/null || echo '?')"
export LL_BASE_SUB="$(git log -1 --pretty=%s 2>/dev/null | cut -c1-70 || true)"
exec python3 "$PYCORE"
