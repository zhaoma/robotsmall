#!/usr/bin/env bash
# scripts/check-seo-tc-coverage.sh
#
# 「需求侧的 S 档，有没有被 docs/06 的某条 **TC 用例行** 认领」——文档到文档的覆盖率门禁。
# 定义点：docs/01 §6.1（S1~S11 的语义唯一出处）、docs/06 §3.6（TC 用例表）。
#
# 为什么需要一条**独立**的门禁（而不是加进 verify-seo-matrix.sh）：
#   · `verify-seo-matrix.sh` 担保的是"脚本对每个 S 有没有断言"；
#   · 它担保不了"有没有一条**测试用例**认领这个 S"——缺引用这个缺陷住在 06 里，
#     而探测器必须住在被防对象所在的那一侧（判据量的集合与要防的东西所在集合要一致）。
#   · 反过来把 TC 编号印进运维脚本的 `--list` 是错的：运维脚本里出现产品用例编号，
#     谁都不敢改它；而且 06 改编号后脚本不跟就变成第二真源，**且没有运行时症状**。
#
# 键集从 docs/01 §6.1 的表格里**解析**，不写死 1..11 —— 写死的话需求加 S12 时门禁仍然全绿，
# 而它守的东西已经不全了。（"清单写死"是同一条洞，在 `check.sh --list` 上已经踩过一次。）
#
# 用法:
#   ./scripts/check-seo-tc-coverage.sh
#   ./scripts/check-seo-tc-coverage.sh --doc01 docs/01-需求基线-OpenStore.md --doc06 docs/06-测试验收.md
#   ./scripts/check-seo-tc-coverage.sh --list      # 打印每个 S 被哪几条 TC 引用
#   ./scripts/check-seo-tc-coverage.sh --self-test
#
# 退出码：定义只在 `docs/05 §4.6`，本文件不复述数字含义。
#   本脚本落到四个码上的情形：0 全覆盖 / 1 有 S 没被任何 TC 行引用（或 TC 里写了不存在的 S 号）
#   / 2 前置缺失（docs/01 或 docs/06 找不到、§6.1 解析不出键集）/ 3 用法错误。
set -uo pipefail

DOC01="docs/01-需求基线-OpenStore.md"
DOC06="docs/06-测试验收.md"
SELF_TEST=0
LIST=0

while [ $# -gt 0 ]; do
  case "$1" in
    --self-test) SELF_TEST=1; shift ;;
    --list)      LIST=1; shift ;;
    --doc01)     DOC01="$2"; shift 2 ;;
    --doc06)     DOC06="$2"; shift 2 ;;
    -h|--help)   sed -n '2,30p' "$0"; exit 0 ;;
    -*)          echo "未知参数: $1" >&2; exit 3 ;;
    *)           echo "多余的位置参数: $1（本脚本不接受站点地址）" >&2; exit 3 ;;
  esac
done

command -v python3 >/dev/null 2>&1 || { echo "PRECONDITION: 需要 python3 解析表格" >&2; exit 2; }

# 核心判定。用 python 而不是 awk：要按表格行切字段、还要做"整词匹配 S7 不等于 S70"，
# awk 里写这些只能靠正则拼接，可读性差到没人愿意维护。
check(){
python3 - "$DOC01" "$DOC06" "$LIST" <<'PY'
import re, sys

doc01, doc06, want_list = sys.argv[1], sys.argv[2], sys.argv[3] == "1"

def die(msg, code):
    sys.stderr.write("PRECONDITION: %s\n" % msg)
    sys.exit(code)

try:
    d01 = open(doc01, encoding="utf-8").read()
except OSError:
    die("读不到 %s" % doc01, 2)
try:
    d06 = open(doc06, encoding="utf-8").read()
except OSError:
    die("读不到 %s" % doc06, 2)

# ---- 键集：docs/01 §6.1 的表格行 `| **S1** | ...`
if "### 6.1" not in d01:
    die("%s 里找不到 §6.1（S 档语义的唯一出处）；节号变了解析器要跟着改，不能当成'全覆盖'" % doc01, 2)
sec = d01.split("### 6.1", 1)[1]
m = re.search(r"\n#{1,3}\s", sec)
if m:
    sec = sec[:m.start()]
keys = re.findall(r"^\|\s*\*\*(S\d+)\*\*\s*\|", sec, re.M)
if not keys:
    # 解析不到键集 ≠ 全覆盖。这一条不写，"表格格式一改"就会静默变成永远绿。
    die("%s §6.1 里一个 S 档都没解析到（表格行格式是 `| **S1** | ...`）" % doc01, 2)

# ---- TC 用例行：docs/06 表格里以 TC- 开头的行（正文里提到 Sn 不算"被用例认领"）
rows = [l for l in d06.split("\n") if re.match(r"^\|\s*\**TC-", l)]

def srefs(text):
    # 整词：S7 不能匹配 S70 / S7a。
    # **还要先剥掉"别的编号空间里的 S"**：docs/06 里真实存在 `INV-S01/S03/S07`（不变量编号），
    # 这些 S01/S03/S07 与 SEO 的 S1~S11 不是一个编号空间。第一版没剥，一跑就误伤三条
    # （`INV-S01` 的前一个字符是 `-`，不算字母数字，于是被当成 SEO 档）——判据自己先过一遍
    # 真实文档，是"不误伤"这一条唯一可靠的验法。
    t = re.sub(r"\b[A-Z]{2,}-S\d+(?:[/~]S\d+)*", " ", text)
    return set(re.findall(r"(?<![A-Za-z0-9])(S\d+)(?![0-9A-Za-z])", t))

covered = {}
for k in keys:
    hit = []
    for r in rows:
        if k in srefs(r):
            cells = r.split("|")
            hit.append(cells[1].strip().strip("*") if len(cells) > 1 else "?")
    covered[k] = hit

bad = 0
missing = [k for k in keys if not covered[k]]
for k in keys:
    if covered[k]:
        if want_list:
            print("  ok    %-4s ← %s" % (k, ", ".join(covered[k])))
    else:
        print("  FAIL  %s 没有被 docs/06 的任何一条 TC 用例行引用（正文里提到它不算："
              "提到不等于有用例认领）" % k)
        bad += 1

# ---- 反向：TC 行里写了不存在的 S 号 = 笔误。方向不同（有效性 vs 覆盖性），
#      但两种错都会让"这条用例在验什么"变成读不出来的东西，所以一起判。
stray = {}
for r in rows:
    cells = r.split("|")
    for s in srefs(r) - set(keys):
        stray.setdefault(s, set()).add(cells[1].strip().strip("*") if len(cells) > 1 else "?")
for s in sorted(stray):
    print("  FAIL  docs/06 的 TC 行引用了不存在的 %s（出现在 %s）：需求 §6.1 里没有这个编号，"
          "写错了就是笔误，写对了就是需求侧还没登记" % (s, ", ".join(sorted(stray[s]))))
    bad += 1

if not want_list:
    if bad == 0:
        print("  ok    %d 个 S 档全部被至少一条 TC 用例行引用；TC 行里也没有不存在的 S 号" % len(keys))
    else:
        print("== %d 项不通过（键集来自 %s §6.1，共 %d 个 S 档）" % (bad, doc01, len(keys)))
sys.exit(1 if bad else 0)
PY
}

# ---------------------------------------------------------------- 自证
# 每条判据都要证明自己会红、且不会乱红（docs/06 §3.7 PG1/PG2）。这里的"故意做错的实现"
# 就是一对手搭的小样本文档：正样本必须绿，三种坏法各自红在预期的那条断言上。
self_test(){
  local tmp; tmp=$(mktemp -d)
  local sfail=0 out rc

  # 正样本：3 个 S 档，每个都被一条 TC 行引用
  cat > "$tmp/01ok.md" <<'EOF'
### 6.1 AC-11 的语义断言
| # | 断言语义 | 期望 |
|---|---|---|
| **S1** | 私有页 noindex | 通过 |
| **S2** | 私有页不在 Disallow | 通过 |
| **S3** | 筛选页 canonical 归并 | 通过 |

### 6.2 下一节
| TC-XX-01 | S1 用例 | 期望 |
| TC-XX-02 | S2/S3 用例 | 期望 |
| TC-XX-04 | US-XX-01, INV-S01/S03/S07 | 期望 |
EOF
  # 坏法 ①：S3 只在正文里被提到，没有任何 TC 行引用它（= 今天 docs/06 上 S5 的真实形态）
  sed 's/| TC-XX-02 | S2\/S3 用例 | 期望 |/| TC-XX-02 | S2 用例 | 期望 |\n\n正文里顺手提一句 S3 不算被认领。/' \
      "$tmp/01ok.md" > "$tmp/01miss.md"
  # 坏法 ②：TC 行引用了不存在的 S9（笔误）
  sed 's/| TC-XX-02 | S2\/S3 用例 | 期望 |/| TC-XX-02 | S2\/S3 用例 | 期望 |\n| TC-XX-03 | S9 用例 | 期望 |/' \
      "$tmp/01ok.md" > "$tmp/01stray.md"
  # 坏法 ③：§6.1 的表格格式变了，解析不出键集 —— 必须 rc=2，不能静默判"全覆盖"
  printf '### 6.1 AC-11 的语义断言\n\n这一节改成了散文，没有表格。\n' > "$tmp/01nokeys.md"

  # ① 正样本必须绿
  #    注意正样本里故意放了一行 `INV-S01/S03/S07`（不变量编号，不是 SEO 档）：
  #    它必须**不被**当成 S 档 —— 第一版就在这里误伤了三条真文档，是"不误伤"的回归样本。
  out=$(check_pair "$tmp/01ok.md" "$tmp/01ok.md" 2>&1); rc=$?
  # check_pair 不存在时这里会 127，自证自己就会炸 —— 显式判一次，别让它冒充"红在预期那条"
  [ "$rc" -eq 0 ] && echo "  ok    正样本（3 个 S 全被 TC 行引用）+ 误伤回归样本（INV-S01/S03/S07 不算 S 档）→ rc=0" || {
    echo "  WRONG 正样本应绿却 rc=${rc}；输出：$(printf '%s' "$out" | head -3)"; sfail=$((sfail+1)); }

  # ② 只在正文提到 = 必须红，且点名 S3
  out=$(check_pair "$tmp/01miss.md" "$tmp/01miss.md" 2>&1); rc=$?
  if [ "$rc" -eq 1 ] && grep -q 'FAIL  S3 ' <<< "$out"; then
    echo "  ok    只在正文提到 S 不算认领 → 红在 S3：$(printf '%s' "$out" | grep 'FAIL' | head -1)"
  else
    echo "  WRONG 缺引用应 rc=1 且点名 S3，实际 rc=${rc}；输出：$(printf '%s' "$out" | head -3)"; sfail=$((sfail+1))
  fi

  # ③ 引用了不存在的 S 号 = 笔误，必须红
  out=$(check_pair "$tmp/01stray.md" "$tmp/01stray.md" 2>&1); rc=$?
  if [ "$rc" -eq 1 ] && grep -q '不存在的 S9' <<< "$out"; then
    echo "  ok    TC 行写了不存在的 S9 → 红并点出笔误"
  else
    echo "  WRONG 笔误应 rc=1，实际 rc=${rc}；输出：$(printf '%s' "$out" | head -3)"; sfail=$((sfail+1))
  fi

  # ④ 解析不出键集必须 rc=2（未验证），不能是 0
  out=$(check_pair "$tmp/01nokeys.md" "$tmp/01nokeys.md" 2>&1); rc=$?
  if [ "$rc" -eq 2 ] && grep -q '^PRECONDITION' <<< "$out"; then
    echo "  ok    §6.1 解析不出键集 → rc=2 且打印 PRECONDITION（不冒充全覆盖）"
  else
    echo "  WRONG 解析失败应 rc=2，实际 rc=${rc}；输出：$(printf '%s' "$out" | head -3)"; sfail=$((sfail+1))
  fi

  # ⑤ 文件不存在必须 rc=2
  out=$(check_pair "$tmp/does-not-exist.md" "$tmp/01ok.md" 2>&1); rc=$?
  if [ "$rc" -eq 2 ]; then echo "  ok    文档不存在 → rc=2（未验证，不是通过）"
  else echo "  WRONG 文档不存在应 rc=2，实际 rc=${rc}"; sfail=$((sfail+1)); fi

  # ⑥ 用法错误 rc=3：不认识参数
  bash "$0" --definitely-not-an-arg >/dev/null 2>&1; rc=$?
  if [ "$rc" -eq 3 ]; then echo "  ok    不认识参数 → rc=3（与 1/2 区分开）"
  else echo "  WRONG 不认识参数应 rc=3，实际 rc=${rc}"; sfail=$((sfail+1)); fi

  rm -rf "$tmp"
  if [ "$sfail" -eq 0 ]; then echo "== 自证通过：6 项（正样本绿 / 只正文提到红 / 笔误红 / 解析失败 2 / 缺文件 2 / 用法 3）"; return 0; fi
  echo "== 自证失败：$sfail 项不符合预期"; return 1
}

# 自证专用：拿一对指定的文件跑 check()，不改全局 DOC01/DOC06
check_pair(){
  DOC01="$1" DOC06="$2" LIST=0 check
}

if [ "$SELF_TEST" = "1" ]; then self_test; exit $?; fi
check; exit $?
