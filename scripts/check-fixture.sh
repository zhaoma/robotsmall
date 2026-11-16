#!/usr/bin/env bash
# scripts/check-fixture.sh
#
# AC-14a 验收 fixture 的**声明侧**校验：只读 `tests/fixtures/<ns>/*.json` 与 `images/`，
# 不连数据库、不依赖 .NET、不需要起站点。
#
# ── 为什么声明侧要单独有一道门 ─────────────────────────────────────────────────
# `fixture verify` 查的是**装载之后**的库（行数对账、逐格覆盖率、内嵌 alt 结构），它必须有库。
# 于是有一整类改坏方式它抓不到：**JSON 改坏了、装载失败了、库还是上一次的** ——
# verify 对着旧库照样全绿。而这道门在任何一次提交上都能跑，是最便宜的那一层。
#
# 更要紧的是**依赖方向**：这份 fixture 的样本身份（谁缺货、谁改过 slug、谁只有中文）
# 是下游 `verify-seo-matrix.sh`（S9/S10）、前端 E2E、AC-14a 扫描的**期望值来源**。
# 样本身份一旦漂移，下游不是"报错"，是**断言对着另一个东西变绿**：
# 把 `fx-oos` 的库存从 0 改成 5，S10 立刻找不到"在售但库存 0"的样本 → 脚本走 PRECONDITION，
# 而 PRECONDITION 在 CI 里常被当成"跳过"而不是失败。所以样本身份必须由机器钉住，不能靠读。
#
# ── 判据分两层 ───────────────────────────────────────────────────────────────
#   R* = 结构/边界：引用可解析、reset 删除边界完备、声明与目录一致
#   C* = 样本身份：每个样本带着它被造出来的那个理由（F1a/F1b/F2/F3/F3b/F5/F6/F-OOS/F-SLUG1/S9/S10）
# 每条判据在红的时候必须能说出**哪一项、哪个实体、期望什么**，否则它只会被加白名单。
#
# 用法:
#   ./scripts/check-fixture.sh                       # 默认校验 tests/fixtures/i18n-en-100
#   ./scripts/check-fixture.sh --dir <path>
#   ./scripts/check-fixture.sh --print-flags         # 打印 verify-seo-matrix.sh 需要的 flag（见下）
#   ./scripts/check-fixture.sh --quiet               # 只打失败项与总结（给 CI 用）
#
# 退出码（与 verify-seo-matrix.sh 同义，别用 `[ $? -le 1 ]` 把未验证当通过）:
#   0 = 全部通过（唯一通过态）
#   1 = 有判据失败
#   2 = 前置条件缺失（缺目录 / 缺 fixture.json / 缺 jq）→ **等价于不通过**
set -uo pipefail

DIR=""
PRINT_FLAGS=0
QUIET=0
SELF_TEST=0
while [ $# -gt 0 ]; do
  case "$1" in
    --dir)          DIR="$2"; shift 2 ;;
    --print-flags)  PRINT_FLAGS=1; shift ;;
    --quiet)        QUIET=1; shift ;;
    --self-test)    SELF_TEST=1; shift ;;
    -h|--help)      sed -n '2,46p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "未知参数: $1（见 --help）" >&2; exit 2 ;;
  esac
done

# ═══════════════════════════════════════════════════════════════════════════
# --self-test：**证明每条判据能红**
# 一条从没红过的判据等于没有判据 —— 而"它能红"这件事本身也是会腐坏的：
# 改判据时不小心把条件写宽，它照样打印 ok，只是永远不红了。
# 做法：每条判据改坏一处数据（就地改在一份临时副本上），断言①退出码为 1 ②输出里出现该判据编号。
# **改坏的是副本**：自证不许碰真数据，否则"跑一次自证"就等于"污染一次 fixture"。
# ═══════════════════════════════════════════════════════════════════════════
selftest() {
  local base="${DIR:-tests/fixtures/i18n-en-100}"
  # **不用 local**：`trap ... EXIT` 在函数返回之后才触发，那时 local 变量已经出作用域，
  # 在 `set -u` 下会变成 "tmp: unbound variable" —— 而它发生在收尾，
  # 会让一个本来成功的自证以非零码退出（症状：自证通过了，CI 却红）。
  ST_TMP=$(mktemp -d "${TMPDIR:-/tmp}/fx-selftest.XXXXXX") || exit 2
  local tmp="$ST_TMP"
  trap 'rm -rf "${ST_TMP:-}"' EXIT
  local pass=0 fail=0

  # mut <code> <说明> <文件名> <jq 过滤器>
  mut() {
    local code="$1" desc="$2" file="$3" filt="$4" d="$tmp/$1"
    rm -rf "$d"; cp -R "$base" "$d"
    if ! jq "$filt" "$d/$file" > "$d/$file.new" 2>/dev/null; then
      printf '  BROKEN %-5s 注入过滤器本身写错了（%s）\n' "$code" "$file"; fail=$((fail+1)); return
    fi
    mv "$d/$file.new" "$d/$file"
    expect_red "$code" "$desc" "$d"
  }
  # mutfile <code> <说明> <破坏动作>
  mutfile() {
    local code="$1" desc="$2" action="$3" d="$tmp/$1"
    rm -rf "$d"; cp -R "$base" "$d"
    eval "$action"
    expect_red "$code" "$desc" "$d"
  }
  expect_red() {
    local code="$1" desc="$2" d="$3" out rc
    # 用**非 quiet** 跑：判据编号出现在 `FAIL <code>` 行里，这是唯一能与判据一一对应的形态。
    # （quiet 模式只输出总结，编号与"失败"这个词分开在两行，grep 会漏 —— 自证自己会假绿。）
    out=$(bash "$0" --dir "$d" 2>&1); rc=$?
    if [ "$rc" = "1" ] && printf '%s' "$out" | grep -q "FAIL ${code} "; then
      printf '  red    %-5s %s\n' "$code" "$desc"; pass=$((pass+1))
    else
      printf '  NOTRED %-5s %s（rc=%s，期望 1；输出里没有 "FAIL %s"）\n' "$code" "$desc" "$rc" "$code"
      printf '%s\n' "$out" | sed 's/^/           | /' | head -6
      fail=$((fail+1))
    fi
  }

  echo "自证基线：$base"
  # 基线必须全绿 —— 否则"能红"是假的（一个本来就红的检查器当然每条都能红）
  local b_out b_rc
  b_out=$(bash "$0" --dir "$base" --quiet 2>&1); b_rc=$?
  if [ "$b_rc" != "0" ]; then
    echo "  NOTGREEN 基线本身就不是全绿（rc=$b_rc）—— 自证无意义，先修基线" >&2
    printf '%s\n' "$b_out" | head -12 | sed 's/^/           | /' >&2
    return 1
  fi
  echo "  green  基线 22 条全过"

  mut C4   "en 译文行空一格（seoTitle）" products.json \
      '.products |= map(if .key=="fx-kettle" then .translations |= map(if .locale=="en" then .seoTitle="" else . end) else . end)'
  mut C5   "en 文本混进中文" products.json \
      '.products |= map(if .key=="fx-mug" then .translations |= map(if .locale=="en" then .seoDescription="陶瓷杯" else . end) else . end)'
  mut C6   "给 zh-only 样本补 en 行" products.json \
      '.products |= map(if .key=="fx-tote" then .translations += [{"locale":"en","slug":"fx-tote","name":"x","shortDescription":"x","description":"x","seoTitle":"x","seoDescription":"x"}] else . end)'
  mut C7   "SKU 撞车" products.json \
      '.products |= map(if .key=="fx-kettle" then .variants[0].sku="FX-OOS-STD" else . end)'
  mut C8   "变体轴未登记为 isVariantAxis" attributes.json \
      '.attributes |= map(if .code=="color" then .isVariantAxis=false else . end)'
  mut C9   "两个变体同一组合" products.json \
      '.products |= map(if .key=="fx-tee" then .variants[1].axes=.variants[0].axes else . end)'
  mut C11  "缺货样本被改下架" products.json \
      '.products |= map(if .key=="fx-oos" then .status="Archived" else . end)'
  mut C12  "掩码样本库存被改成 >5" products.json \
      '.products |= map(if .key=="fx-mug" then .variants |= map(if .sku=="FX-MUG-350ML" then .onHand=5 else . end) else . end)'
  mut C16  "归档样本两个候选分类同深度" products.json \
      '.products |= map(if .key=="fx-arch-repl" then .categories=["fx-cat-home","fx-cat-apparel"] else . end)'
  mut C17  "历史行的 newSlug 与当前 slug 不一致" slug-history.json \
      '.slugHistory |= map(if .entityKey=="fx-kettle" then .newSlug="fx-kettle-old" else . end)'
  mut C2   "同语种 slug 撞车" products.json \
      '.products |= map(if .key=="fx-tee" then .translations |= map(if .locale=="en" then .slug="fx-mug" else . end) else . end)'
  mut R1   "静态页从 reset 白名单里漏掉一个" fixture.json \
      '.reset.staticPageSlugs.slugs -= ["guanyu"]'
  mut R2   "files[] 声明一个不存在的文件" fixture.json \
      '.files += ["nope.json"]'
  mutfile C13 "图片文件缺失" 'rm -f "$d/images/fx-mug-1.png"'
  mutfile C14 "内嵌图 alt 被清空" \
    'jq '"'"'.products |= map(if .key=="fx-tee" then .translations |= map(if .locale=="en" then .description="<p>no alt</p>" else . end) else . end)'"'"' "$d/products.json" > "$d/p2" && mv "$d/p2" "$d/products.json"'
  mutfile C15 "选项分布变了（总数不变）" \
    'jq '"'"'.attributes |= map(if .code=="material" then .options=.options[0:3] elif .code=="care" then .options += [{"code":"x1","translations":[{"locale":"zh-Hans","label":"甲"},{"locale":"en","label":"A"}]},{"code":"x2","translations":[{"locale":"zh-Hans","label":"乙"},{"locale":"en","label":"B"}]}] else . end)'"'"' "$d/attributes.json" > "$d/a2" && mv "$d/a2" "$d/attributes.json"'
  mutfile C18 "分类 parent 指向不存在的分类" \
    'jq '"'"'.categories |= map(if .key=="fx-arch-repl-cat" then .parent="fx-cat-ghost" else . end)'"'"' "$d/categories.json" > "$d/g2" && mv "$d/g2" "$d/categories.json"'

  echo "自证结果：$pass 条判据能红，$fail 条不能"
  [ "$fail" -eq 0 ] || return 1
  return 0
}

[ -n "$DIR" ] || DIR="tests/fixtures/i18n-en-100"

# `--print-flags` 是"给我一条能直接粘的命令"，前面那 22 行 ok 是噪音；失败时仍要把明细打出来。
[ "$PRINT_FLAGS" -eq 1 ] && QUIET=1

if [ "$SELF_TEST" -eq 1 ]; then
  command -v jq >/dev/null 2>&1 || { echo "PRECONDITION: 缺 jq" >&2; exit 2; }
  selftest || exit 1
  exit 0
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "PRECONDITION: 缺 jq（本脚本用 jq 解析 JSON，不用 python —— 后者在 CI 镜像里不保证有）" >&2
  exit 2
fi
if [ ! -d "$DIR" ]; then
  echo "PRECONDITION: 目录不存在: $DIR" >&2
  exit 2
fi
if [ ! -f "$DIR/fixture.json" ]; then
  echo "PRECONDITION: 缺 $DIR/fixture.json（它是命名空间与 reset 边界的唯一声明）" >&2
  exit 2
fi

# ── 输出与计数 ───────────────────────────────────────────────────────────────
FAILED=0
PASSED=0
WARNED=0
FAILS=""
CUR=""

begin() { CUR="$1"; }
ok()    { PASSED=$((PASSED+1)); [ "$QUIET" -eq 1 ] || printf '  ok   %-7s %s\n' "$CUR" "$1"; }
bad()   { FAILED=$((FAILED+1)); FAILS="$FAILS
  $CUR  $1"; printf '  FAIL %-7s %s\n' "$CUR" "$1"; }
warn()  { WARNED=$((WARNED+1)); [ "$QUIET" -eq 1 ] || printf '  warn %-7s %s\n' "$CUR" "$1"; }
hdr()   { [ "$QUIET" -eq 1 ] || printf '\n%s\n' "$1"; }

# 一条判据的收尾：无违规 → ok；有 → bad（详情逐条列出，最多 8 条，其余折叠）
verdict() {
  local name="$1" count="$2" details="${3:-}"
  if [ "$count" -eq 0 ]; then
    ok "$name"
  else
    bad "${name}（${count} 处）"
    printf '%s\n' "$details" | sed '/^$/d' | head -8 | sed 's/^/         · /'
    [ "$count" -gt 8 ] && echo "         · …还有 $((count-8)) 处（同类，判据相同）"
    return 1
  fi
}

# ── 声明 ─────────────────────────────────────────────────────────────────────
j() { jq -r "$1" "$2"; }
NS=$(j .namespace "$DIR/fixture.json")
DEFAULT_LOCALE=$(j '.locales[] | select(.isDefault) | .code' "$DIR/fixture.json")
PREFIX=$(j '.reset.slugPrefix' "$DIR/fixture.json")
SP_SLUG_LOCALE=$(j '.reset.staticPageSlugs.locale' "$DIR/fixture.json")

LOCALES=$(j '.locales[].code' "$DIR/fixture.json")
INTERNAL_CODES=$(j '.reset.internalCodes[]' "$DIR/fixture.json")
SP_SLUGS=$(j '.reset.staticPageSlugs.slugs[]' "$DIR/fixture.json")

missing_ref=0
for v in NS DEFAULT_LOCALE PREFIX SP_SLUG_LOCALE; do
  eval "val=\$$v"
  case "$val" in ""|null) echo "PRECONDITION: fixture.json 缺声明 $v" >&2; missing_ref=1 ;; esac
done
[ "$missing_ref" -eq 0 ] || exit 2

[ "$QUIET" -eq 1 ] || echo "fixture: $DIR  (namespace=$NS, default=$DEFAULT_LOCALE, reset 前缀=$PREFIX)"

# ── 外部命令的报错不许被吞 ───────────────────────────────────────────────────
# 起因是真实发生的一次：C5 里一个 jq 子查询写错了（语法错），jq 把错误打到 stderr、
# **退出码非 0 但进程替换不传递**，于是那条判据打印 `ok` —— 一个**没跑成功的判据报绿**。
# 这比判据写错严重得多：判据写错至少还是"错的结果"，而这里连"跑过"都不成立。
# 做法：把 stderr 收进文件，收尾时非空即判失败。检查器必须能证明自己的每条判据都真的跑过。
ERRLOG=$(mktemp "${TMPDIR:-/tmp}/check-fixture.XXXXXX")
trap 'rm -f "$ERRLOG"' EXIT
exec 2>"$ERRLOG"

# 各文件对象数组（用文件内的对象名，不猜文件名）
# 不吞 stderr：jq 报错必须冒到 ERRLOG 里让 E1 变红。**这里曾经写过 `2>/dev/null`，
# 结果是"文件读不进来"与"文件里确实没有重复"两种完全不同的状态打印同一个 ok。**
P=$(jq -c '.products      // []' "$DIR/products.json")
A=$(jq -c '.attributes    // []' "$DIR/attributes.json")
G=$(jq -c '.categories    // []' "$DIR/categories.json")
S=$(jq -c '.staticPages   // []' "$DIR/static-pages.json")
H=$(jq -c '.slugHistory   // []' "$DIR/slug-history.json")

# jq 预处理：把三张"带翻译的表"摊平成 (key, locale, slug) 三元组，后面多处复用。
# 展开成 TSV 而不是在每个判据里重写一遍 jq —— 一份口径，多处引用（否则判据之间会各自漂移）。
slug_rows() {   # → type <TAB> key <TAB> locale <TAB> slug
  {
    printf '%s' "$P" | jq -r '.[] | .key as $k | .translations[] | ["Product", $k, .locale, .slug] | @tsv'
    printf '%s' "$G" | jq -r '.[] | .key as $k | .translations[] | ["Category", $k, .locale, .slug] | @tsv'
    printf '%s' "$S" | jq -r '.[] | .key as $k | .translations[] | ["StaticPage", $k, .locale, .slug] | @tsv'
  }
}

# ═══════════════════════════════════════════════════════════════════════════
hdr "── R 结构 / reset 边界 ─────────────────────────────────────────────────"
# ── R1 reset 删除边界必须覆盖每一个实体 ──────────────────────────────────────
# 这是整份 fixture 最容易出洞的地方，而且洞**不会自己报错**：
#   三项并集（slug 前缀 / InternalCode / 静态页白名单，见 02 §7.3.3）；另有**第四项谓词**
#   `SlugHistory`（按 EntityType+EntityId 清依赖行，不计入三项并集——它没有自身标记可判）。
#   别把这里的注释当事实来源：口径的唯一出处是 02 §7.3.3。
#   静态页的 slug 是 `guanyu` / `about`（**刻意的**：要证明 per-locale slug），
#   而 StaticPage **没有 InternalCode 列** —— 于是它两个谓词都不命中。
#   后果链：reset 删不掉它 → 下一轮的 storefront 有"关于我们"却没有商品 →
#   更糟的是 verify 的行数对账会把它算成 orphan_scan 或（若谓词另写一套）静默放过 ——
#   **而"连跑两次计数不变"这条判据不会红**，因为 upsert 会把旧行覆盖成新值。
#   所以边界必须**声明完备**，并且由这道门保证"新增实体不会漏声明"。
R1=""
# 注意：macOS 自带的是 bash 3.2，**没有关联数组**（`declare -A` 直接报错）。
# 所以这里用 "key<TAB>internalCode" 的平面串 + awk 查表 —— 写成关联数组在 CI 的 linux 上能跑、
# 在开发者本机不能跑，是最容易漏的那种"环境相关"缺陷。
IC_MAP=$(printf '%s' "$P" | jq -r '.[] | "\(.key)\t\(.internalCode // "")"')
SP_SLUGS_SP=" $(printf '%s' "$SP_SLUGS" | tr '\n' ' ') "
# 静态页白名单是**实体级**判据（按默认语种 slug 定位一行 `StaticPage`，删它级联删掉所有译文行），
# 而前缀是**行级**（任意语种的 slug 都能命中）。两种粒度混在一起会导致：
# `/en/pages/about` 的 slug 不在白名单里于是被判"出界"，而它其实随实体一起被删 —— 假红。
# 所以先把"哪些静态页实体被白名单覆盖"算出来，再按实体判。
SP_OK_KEYS=""
while IFS=$'\t' read -r k zslug; do
  [ -n "$k" ] || continue
  case "$SP_SLUGS_SP" in *" $zslug "*) SP_OK_KEYS="$SP_OK_KEYS $k" ;; esac
done < <(printf '%s' "$S" | jq -r --arg l "$SP_SLUG_LOCALE" '.[] | .key as $k
  | [$k, ([.translations[] | select(.locale==$l) | .slug] | .[0] // "")] | @tsv')

while IFS=$'\t' read -r typ k loc slug; do
  [ -n "$k" ] || continue
  case "$typ" in
    StaticPage) case " $SP_OK_KEYS " in *" $k "*) continue ;; esac ;;   # 实体级白名单
    *)          case "$SP_SLUGS_SP" in *" $slug "*) continue ;; esac ;;  # （备用）行级白名单
  esac
  case "$slug" in "$PREFIX"*) continue ;; esac                          # slug 前缀（行级）
  ic=$(printf '%s\n' "$IC_MAP" | awk -F'\t' -v k="$k" '$1==k{print $2; exit}')
  if [ -n "$ic" ]; then
    ICS_SP=" $(printf '%s' "$INTERNAL_CODES" | tr '\n' ' ') "
    case "$ICS_SP" in *" $ic "*) continue ;; esac                       # InternalCode 白名单（实体级）
  fi
  R1="$R1$typ($k) ($loc) slug=$slug / internalCode=${ic:-无} 都不在 reset 边界内"$'\n'
done < <(slug_rows)
begin R1; verdict "每个实体的 slug 都落在 reset 谓词内（前缀 ∪ InternalCode ∪ 静态页白名单）" \
  "$(printf '%s' "$R1" | sed '/^$/d' | wc -l | tr -d ' ')" "$R1"

# R1b：声明的白名单项必须真的存在（反向：白名单里有、数据里没有 = 声明腐烂）
begin R1b; d=""; c=0
for s in $SP_SLUGS; do
  printf '%s' "$S" | jq -e --arg s "$s" --arg l "$SP_SLUG_LOCALE" \
    'any(.[]; any(.translations[]; .locale==$l and .slug==$s))' >/dev/null || {
    c=$((c+1)); d="$d reset.staticPageSlugs 里的 $s 在静态页数据中不存在（${SP_SLUG_LOCALE}）"$'\n'; }
done
verdict "reset 白名单项都真实存在（声明不腐烂）" "$c" "$d"

# ── R2 fixture.json 的 files[] 与实际目录一致 ────────────────────────────────
# 两个方向都要查：漏声明 → 有文件没人装载（样本静默缺失）；多声明 → load 直接报错。
begin R2; d=""; c=0
while IFS= read -r f; do
  [ -n "$f" ] || continue
  [ -f "$DIR/$f" ] || { c=$((c+1)); d="$d files[] 声明了 ${f}，但文件不存在"$'\n'; }
done < <(j '.files[]?' "$DIR/fixture.json")
for f in "$DIR"/*.json; do
  b=$(basename "$f")
  [ "$b" = "fixture.json" ] && continue
  jq -e --arg b "$b" '.files | index($b)' "$DIR/fixture.json" >/dev/null || {
    c=$((c+1)); d="$d 目录里有 ${b}，但 files[] 未声明（不会被装载，样本会静默缺失）"$'\n'; }
done
verdict "files[] 与目录内容双向一致" "$c" "$d"

# ── R3 声明里的语种必须覆盖默认语种，且没有重复声明 ──────────────────────────
begin R3; d=""; c=0
echo "$LOCALES" | grep -qx "$DEFAULT_LOCALE" || { c=$((c+1)); d="$d locales 里没有 isDefault 的那一条（${DEFAULT_LOCALE}）"$'\n'; }
dup=$(echo "$LOCALES" | sort | uniq -d)
[ -z "$dup" ] || { c=$((c+1)); d="$d 语种重复声明: $(echo "$dup" | tr '\n' ' ')"$'\n'; }
verdict "语种声明自洽（含默认语种、无重复）" "$c" "$d"

# ═══════════════════════════════════════════════════════════════════════════
hdr "── C 样本身份 ───────────────────────────────────────────────────────────"

# ── C1 默认语种翻译必须存在（02 §6：同事务写默认语言行）──────────────────────
begin C1; d=""; c=0
for kv in "Product:$P" "Category:$G" "StaticPage:$S"; do
  t=${kv%%:*}; jj=${kv#*:}
  while IFS= read -r k; do
    [ -n "$k" ] || continue
    printf '%s' "$jj" | jq -e --arg k "$k" --arg l "$DEFAULT_LOCALE" \
      'any(.[]; .key==$k and any(.translations[]; .locale==$l))' >/dev/null || {
      c=$((c+1)); d="$d $t($k) 缺 $DEFAULT_LOCALE 译文行（实体存在但默认语种无行 = 页面渲染无从回退）"$'\n'; }
  done < <(printf '%s' "$jj" | jq -r '.[].key')
done
verdict "三类实体都有默认语种译文行" "$c" "$d"

# ── C2 同类型内 (locale, slug) 唯一 ─────────────────────────────────────────
# 库里是唯一索引，重复的后果不是"两条数据"，是**装载时整个事务回滚**，
# 而报错文本只会告诉你撞了哪个约束 —— 所以要在声明侧先抓。
begin C2; d=""; c=0
for pair in "products:products" "categories:categories" "static-pages:staticPages"; do
  file="${pair%%:*}"; root="${pair#*:}"
  dup=$(jq -r --arg root "$root" '[.[$root][]
      | .translations[] | [.locale, .slug] | @tsv]
    | group_by(.) | map(select(length>1) | .[0]) | .[]' "$DIR/$file.json")
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    c=$((c+1)); d="$d $file: (locale,slug)=$line 重复"$'\n'
  done <<< "$dup"
done
verdict "同类型内 (locale, slug) 唯一" "$c" "$d"

# ── C3 所有翻译行的 locale 必须在声明集合内 ──────────────────────────────────
begin C3; d=""; c=0
while IFS=$'\t' read -r typ k loc slug; do
  [ -n "$k" ] || continue
  echo "$LOCALES" | grep -qx "$loc" || { c=$((c+1)); d="$d $typ($k) 的译文行 locale=$loc 未在 locales 中声明"$'\n'; }
done < <(slug_rows)
verdict "译文行 locale ∈ 声明语种" "$c" "$d"

# ── C4 en 行必须逐字段填满（F1a / F2）───────────────────────────────────────
# 这是 AC-14a「en 100% 已译」的**数据前提**。缺一格 → 覆盖率不足 100% → 验收红，
# 而红的成因是数据不是实现。所以缺口要在声明侧就报，别让它以"覆盖率 98%"的形态出现。
begin C4; d=""; c=0
while IFS=$'\t' read -r k field; do
  [ -n "$k" ] || continue
  c=$((c+1)); d="$d Product($k).en.$field 为空（AC-14a 覆盖率分母里的一格）"$'\n'
done < <(printf '%s' "$P" | jq -r '.[] | select(any(.translations[]; .locale=="en"))
  | .key as $k
  | .translations[] | select(.locale=="en")
  | ["name","shortDescription","description","seoTitle","seoDescription"][] as $f
  | select((.[$f] // "") == "")
  | "\($k)\t\($f)"')
while IFS=$'\t' read -r k field; do
  [ -n "$k" ] || continue
  c=$((c+1)); d="$d StaticPage($k).en.$field 为空"$'\n'
done < <(printf '%s' "$S" | jq -r '.[] | select(any(.translations[]; .locale=="en"))
  | .key as $k
  | .translations[] | select(.locale=="en")
  | ["title","body","seoTitle","seoDescription"][] as $f
  | select((.[$f] // "") == "")
  | "\($k)\t\($f)"')
verdict "en 译文行逐字段填满（商品 5 字段 / 静态页 4 字段）" "$c" "$d"

# ── C5 en 文本零中文，白名单精确到 (实体, 字段) ──────────────────────────────
# 中文型号豁免（F6）是**唯一**允许 en 文本里出现汉字的地方，且只限 `name` 一个字段。
# 白名单写成"整个实体"是本条最容易犯的错：一旦 `fx-arm` 整个实体豁免，
# 它的描述里混进中文就没人管，而那正是 AC-14a 要抓的东西。
# 注意 seoTitle 不在豁免内：F6 的样本里 seoTitle 是 `Robot Arm-3000`（无汉字），
# 这个细节的意义是——**豁免只给"业务上必须带型号"的那一格**，能少给就少给。
CJK='def hascjk: any(explode[]; (.>=13312 and .<=40959) or (.>=63744 and .<=64255) or (.>=12288 and .<=12351) or (.>=65280 and .<=65519) or (.>=11904 and .<=12031));'
WHITELIST_KEY="fx-arm"; WHITELIST_FIELD="name"
begin C5; d=""; c=0
while IFS=$'\t' read -r k field; do
  [ -n "$k" ] || continue
  [ "$k" = "$WHITELIST_KEY" ] && [ "$field" = "$WHITELIST_FIELD" ] && continue
  c=$((c+1)); d="$d Product($k).en.$field 含汉字/全角字符（AC-14a 的唯一红线；豁免仅限 ${WHITELIST_KEY}.${WHITELIST_FIELD}）"$'\n'
done < <(printf '%s' "$P" | jq -r "$CJK"'
  .[] | .key as $k | .translations[] | select(.locale=="en")
  | ["name","shortDescription","description","seoTitle","seoDescription"][] as $f
  | select((.[$f] // "") | hascjk) | "\($k)\t\($f)"')
while IFS=$'\t' read -r k field; do
  [ -n "$k" ] || continue
  c=$((c+1)); d="$d StaticPage($k).en.$field 含汉字/全角字符"$'\n'
done < <(printf '%s' "$S" | jq -r "$CJK"'
  .[] | .key as $k | .translations[] | select(.locale=="en")
  | ["title","body","seoTitle","seoDescription"][] as $f
  | select((.[$f] // "") | hascjk) | "\($k)\t\($f)"')
while IFS=$'\t' read -r k field; do
  [ -n "$k" ] || continue
  c=$((c+1)); d="$d Category($k).en.$field 含汉字/全角字符"$'\n'
done < <(printf '%s' "$G" | jq -r "$CJK"'
  .[] | .key as $k | .translations[] | select(.locale=="en")
  | ["name","description"][] as $f
  | select((.[$f] // "") | hascjk) | "\($k)\t\($f)"')
while IFS=$'\t' read -r k field; do
  [ -n "$k" ] || continue
  c=$((c+1)); d="$d Attribute($k).en.$field 含汉字/全角字符（筛选项没译 = 英文站筛选器全是中文）"$'\n'
done < <(printf '%s' "$A" | jq -r "$CJK"'
  .[] | .key as $k | .translations[] | select(.locale=="en")
  | select((.name // "") | hascjk) | "\($k)\tname"')
while IFS=$'\t' read -r k field; do
  [ -n "$k" ] || continue
  c=$((c+1)); d="$d Attribute($k).en.$field 含汉字/全角字符（筛选项没译 = 英文站筛选器全是中文）"$'\n'
done < <(printf '%s' "$A" | jq -r "$CJK"'
  .[] | .key as $k | .options[] | .code as $oc | .translations[] | select(.locale=="en")
  | select((.label // "") | hascjk) | "\($k)/\($oc)\tlabel"')
verdict "en 文本零汉字（豁免 = ${WHITELIST_KEY}.${WHITELIST_FIELD}，精确到字段）" "$c" "$d"

# ── C6 zh-only 样本必须真的没有 en 行（F3 / F3b）──────────────────────────────
# 这两个样本的**全部价值**在于"en 侧没有行"，所以判据是"没有"，而不是"翻译为空"。
# 两者的区别在实现上差一个分支：有行但空 → en 页 200 + 回退中文（14a 红）；
# 无行 → en 页 404（14d② 绿）。把它们混成"空字符串"这个样本就废了。
begin C6; d=""; c=0
if printf '%s' "$P" | jq -e 'any(.[]; .key=="fx-tote" and any(.translations[]; .locale=="en"))' >/dev/null; then
  c=$((c+1)); d="$d Product(fx-tote) 出现了 en 译文行 —— F3（页级 404）失去唯一样本，14d② 商品分支无人验"$'\n'
fi
if printf '%s' "$G" | jq -e 'any(.[]; .key=="fx-cat-zhonly" and any(.translations[]; .locale=="en"))' >/dev/null; then
  c=$((c+1)); d="$d Category(fx-cat-zhonly) 出现了 en 译文行 —— F3b 失去唯一样本"$'\n'
fi
verdict "zh-only 样本确实没有 en 行（fx-tote / fx-cat-zhonly）" "$c" "$d"

# ── C7 SKU 全局唯一 ─────────────────────────────────────────────────────────
begin C7; d=""; c=0
while IFS= read -r sku; do
  [ -n "$sku" ] || continue
  c=$((c+1)); d="$d SKU 重复: ${sku}（唯一索引会在装载事务里抛，整批回滚）"$'\n'
done < <(printf '%s' "$P" | jq -r '[.[] | .key as $k | .variants[] | {sku: .sku, key: $k}] | group_by(.sku) | map(select(length>1)) | .[] | .[0].sku')
verdict "SKU 全局唯一" "$c" "$d"

# ── C8 变体轴必须解析到已登记的 isVariantAxis 属性 ───────────────────────────
# 这条防的是"变体建了、轴没登记"：轴解析不出来 → AxisSignature 退化成空串 →
# 同商品多个变体签名相同 → 变体选择器认为"只有一个规格" → 页面正常渲染、加购拿错 SKU。
# 现场表现是"商品页看着好好的，下单拿错货"，不读数据根本查不出来。
begin C8; d=""; c=0
while IFS=$'\t' read -r pk acode ocode; do
  [ -n "$pk" ] || continue
  if ! printf '%s' "$A" | jq -e --arg c "$acode" 'any(.[]; .code==$c and .isVariantAxis==true)' >/dev/null; then
    c=$((c+1)); d="$d Product($pk) 的变体轴 $acode 未登记为 isVariantAxis=true"$'\n'; continue
  fi
  if ! printf '%s' "$A" | jq -e --arg c "$acode" --arg o "$ocode" 'any(.[]; .code==$c and any(.options[]; .code==$o))' >/dev/null; then
    c=$((c+1)); d="$d Product($pk) 的轴 $acode 引用了不存在的选项 $ocode"$'\n'
  fi
done < <(printf '%s' "$P" | jq -r '.[] | .key as $k | .variants[] | .axes[]? | "\($k)\t\(.code)\t\(.option)"')
verdict "变体轴与选项都可解析" "$c" "$d"

# ── C9 同商品内 AxisSignature 唯一（codes 升序以 | 连接，无轴 = ""）───────────
begin C9; d=""; c=0
while IFS=$'\t' read -r pk sig n; do
  [ -n "$pk" ] || continue
  c=$((c+1)); d="$d Product($pk) 有 $n 个变体签名相同（\"$sig\"）—— 选择器会少一格，加购拿到重复 SKU"$'\n'
done < <(printf '%s' "$P" | jq -r '.[] | .key as $k
  | [ .variants[] | ([.axes[]? | "\(.code)=\(.option)"] | sort | join("|")) ] | group_by(.) 
  | map(select(length>1) | "\($k)\t\(.[0])\t\(length)") | .[]')
verdict "同商品内变体签名唯一" "$c" "$d"

# ── C10 F1b：2 维矩阵样本必须是 3×2=6 ───────────────────────────────────────
begin C10; d=""; c=0
n=$(printf '%s' "$P" | jq -r '.[] | select(.key=="fx-tee") | .variants | length')
[ "$n" = "6" ] || { c=$((c+1)); d="$d Product(fx-tee) 变体数=${n}（F1b 要求颜色 3 × 尺码 2 = 6）"$'\n'; }
dims=$(printf '%s' "$P" | jq -r '.[] | select(.key=="fx-tee") | [.variants[].axes | length] | unique | @csv')
[ "$dims" = "2" ] || { c=$((c+1)); d="$d Product(fx-tee) 的变体轴数不是恒为 2（实际维度集合 ${dims}）"$'\n'; }
verdict "fx-tee 是 3×2 二维矩阵（F1b）" "$c" "$d"

# ── C11 F-OOS：在售 + 库存 0 + en 完整（S10 的唯一数据前提）──────────────────
# 三个条件缺一不可，且**每个条件的缺失都换一个不同的错误现场**：
#   不是 Active → S10 变成 S9（缺货 ≠ 下架）；onHand 不是 0 → 找不到 OutOfStock 样本；
#   en 不完整 → 同一个样本同时踩 AC-14a（一个样本只承担一个判据，混了就归因漂移）。
begin C11; d=""; c=0
st=$(printf '%s' "$P" | jq -r '.[] | select(.key=="fx-oos") | .status')
[ "$st" = "Active" ] || { c=$((c+1)); d="$d Product(fx-oos) status=${st}（S10 要求 Active）"$'\n'; }
avail=$(printf '%s' "$P" | jq -r '.[] | select(.key=="fx-oos") | .variants[0] | .onHand - .reserved - .sold')
[ "$avail" = "0" ] || { c=$((c+1)); d="$d Product(fx-oos) available=${avail}（要求 onHand-reserved-sold=0）"$'\n'; }
if ! printf '%s' "$P" | jq -e 'any(.[]; .key=="fx-oos" and any(.translations[]; .locale=="en"))' >/dev/null; then
  c=$((c+1)); d="$d Product(fx-oos) 缺 en 译文行（缺货样本必须 en 完整，否则它同时踩 14a）"$'\n'
fi
verdict "fx-oos = Active + available 0 + en 在场（F-OOS / S10）" "$c" "$d"

# ── C12 availableQuantity 掩码样本：≤5 才暴露真实值 ──────────────────────────
# 前端算 stepperMax 用 min(99, availableQuantity)，而 availableQuantity 在 >5 时是 null。
# 所以必须有一个**真实值 ≤5** 的变体，否则"显示真实数量"这条分支永远没人走过，
# 而 `Math.min(99, null) === 0` 这个坑会在生产上表现为"库存 3 但最多只能买 0 件"。
begin C12; d=""; c=0
oh=$(printf '%s' "$P" | jq -r '.[] | .variants[] | select(.sku=="FX-MUG-350ML") | .onHand')
[ -n "$oh" ] && [ "$oh" != "null" ] || { c=$((c+1)); d="$d 找不到 FX-MUG-350ML（availableQuantity 掩码样本的载体）"$'\n'; }
[ "$oh" = "3" ] || { c=$((c+1)); d="$d FX-MUG-350ML onHand=${oh}（要求 3：≤5 才能让 availableQuantity 暴露真实值）"$'\n'; }
verdict "FX-MUG-350ML onHand=3（≤5 掩码分支的唯一载体）" "$c" "$d"

# ── C13 图片文件存在 + 中英 alt 非空；未引用的图片给 warn ─────────────────────
begin C13; c13d=""; c13c=0
while IFS=$'\t' read -r k f zh_alt en_alt; do
  [ -n "$k" ] || continue
  [ -f "$DIR/images/$f" ] || { c13c=$((c13c+1)); c13d="$c13d $k 引用的 $f 不存在（装载时取图失败）"$'\n'; }
  [ -n "$zh_alt" ] || { c13c=$((c13c+1)); c13d="$c13d $k/$f 缺 $DEFAULT_LOCALE alt"$'\n'; }
  [ -n "$en_alt" ] || { c13c=$((c13c+1)); c13d="$c13d $k/$f 缺 en alt（02 §7.3.2：每张图都要英文 alt）"$'\n'; }
done < <(printf '%s' "$P" | jq -r '.[] | .key as $k | .images[]? | "\($k)\t\(.file)\t\(.altByLocale["'"$DEFAULT_LOCALE"'"] // "")\t\(.altByLocale["en"] // "")"')
# 引用来源有两处：`images[].file` 与富文本描述里内嵌的 `<img src=...>`。
# **只扫前者会把 `fx-tee-desc-1.png` 报成残留** —— 而它恰恰是 F5 的取证对象。
# 所以用"从整个 products.json 里抓 *.png 文件名"这个粗口径：宁可宽（多算引用）也不要窄
# （窄了就会把在用的图报成残留，而误报会让整条判据被关掉）。
refs=$(grep -oE '[A-Za-z0-9_.-]+\.png' "$DIR/products.json" | sort -u)
verdict "图片文件存在且中英 alt 非空" "$c13c" "$c13d"
for f in "$DIR"/images/*; do
  [ -f "$f" ] || continue
  b=$(basename "$f")
  printf '%s\n' "$refs" | grep -qxF "$b" || warn "images/$b 未被任何商品引用（不会坏事，但它是残留；reset 也不会删它）"
done

# ── C14 F5：富文本内嵌 img 必须有非空 alt，中英各查一遍 ──────────────────────
# 这条只能在声明侧查一次，因为**装载之后它就变成 Description 字符串的一部分**了，
# 而 sanitize 白名单若剥离内嵌图，结果是"脚本扫不到 alt 于是绿"——表现为绿、不是红，
# 所以必须由"数据里确实有这个 alt"这一侧钉住，再由前端/脚本 grep 输出侧兜住。
begin C14; d=""; c=0
while IFS=$'\t' read -r k loc html; do
  [ -n "$k" ] || continue
  n_img=$(printf '%s' "$html" | grep -o '<img' | wc -l | tr -d ' ')
  n_alt=$(printf '%s' "$html" | grep -o 'alt="[^"]\+"' | wc -l | tr -d ' ')
  [ "$n_img" -gt 0 ] || continue
  if [ "$n_alt" -lt "$n_img" ]; then
    c=$((c+1)); d="$d Product($k).$loc 有 $n_img 个 <img> 但只有 $n_alt 个非空 alt（F5 取证面）"$'\n'
  fi
done < <(printf '%s' "$P" | jq -r '.[] | .key as $k | .translations[] | "\($k)\t\(.locale)\t\(.description // "")"')
have=$(printf '%s' "$P" | jq -r '[.[] | .key as $k | .translations[] | select((.description // "") | test("<img")) | "\($k)/\(.locale)"] | join(",")')
for want in "fx-tee/$DEFAULT_LOCALE" "fx-tee/en"; do
  case ",$have," in *",$want,"*) ;; *) c=$((c+1)); d="$d 缺内嵌图样本: ${want}（F5 要求中英各一份）"$'\n' ;; esac
done
verdict "富文本内嵌 img 都带非空 alt，且 fx-tee 中英各一份（F5）" "$c" "$d"

# ── C15 属性/选项双语，且选项总数与 F4 的组成一致 ────────────────────────────
# 计数写成"5+3+2+3+2"而不是一个总数 15：总数相等但分布不同（比如 material 3 + care 5）
# 会让 F4 的取证面整体错位，而 15 这个总数**照样通过**。
begin C15; d=""; c=0
while IFS=$'\t' read -r k what zh en; do
  [ -n "$k" ] || continue
  [ -n "$zh" ] && [ -n "$en" ] || { c=$((c+1)); d="$d Attribute($k) 的 $what 缺中文或英文译文"$'\n'; }
done < <(printf '%s' "$A" | jq -r '.[] | .key as $k
  | (.translations[] | select(.locale=="'"$DEFAULT_LOCALE"'") | .name // "") as $zn
  | (.translations[] | select(.locale=="en") | .name // "") as $nen
  | "\($k)\tname\t\($zn)\t\($nen)",
    (.options[] | .code as $oc
      | (.translations[] | select(.locale=="'"$DEFAULT_LOCALE"'") | .label // "") as $zl
      | (.translations[] | select(.locale=="en") | .label // "") as $nl
      | "\($k)/\($oc)\tlabel\t\($zl)\t\($nl)")')
comp=$(printf '%s' "$A" | jq -r '[.[] | "\(.code):\(.options|length)"] | sort | join(" ")')
[ "$comp" = "capacity:2 care:3 color:3 material:5 size:2" ] || {
  c=$((c+1)); d="$d 选项分布=${comp}（F4 要求 capacity:2 care:3 color:3 material:5 size:2）"$'\n'; }
nf=$(printf '%s' "$A" | jq -r '[.[] | select(.isFilterable==true)] | length')
[ "$nf" -ge 2 ] || { c=$((c+1)); d="$d IsFilterable 属性只有 $nf 个（verify-seo-matrix.sh 的 f.* 链接前置条件要求 ≥2）"$'\n'; }
verdict "属性与选项双语齐全，选项分布 = F4 声明" "$c" "$d"

# ── C16 S9 前提：归档"有替代"的候选分类深度必须不同 ─────────────────────────
# "取最深"只有在**候选深度不同**时才有唯一期望值。若两个候选同深度，
# S9 的期望值就不可判定，而实现无论取哪个都能被论证成对的——
# 那就是**没有判据**，却长得像有判据。
depth() {   # key → 深度（根 = 0）
  local key="$1" dep=0 guard=0 parent
  while :; do
    parent=$(printf '%s' "$G" | jq -r --arg k "$key" '.[] | select(.key==$k) | .parent // "null"')
    [ "$parent" = "null" ] || [ -z "$parent" ] && break
    key="$parent"; dep=$((dep+1)); guard=$((guard+1))
    [ "$guard" -gt 32 ] && { echo "-1"; return; }
  done
  echo "$dep"
}
begin C16; d=""; c=0
replcats=$(printf '%s' "$P" | jq -r '.[] | select(.key=="fx-arch-repl") | .categories[]?' | tr '\n' ' ')
ncand=$(echo "$replcats" | wc -w | tr -d ' ')
[ "$ncand" -ge 2 ] || { c=$((c+1)); d="$d Product(fx-arch-repl) 只有 $ncand 个候选分类（S9 取最深需要 ≥2 个才有得比）"$'\n'; }
depths=""
for cc in $replcats; do depths="$depths $(depth "$cc")"; done
uniq_d=$(echo $depths | tr ' ' '\n' | sed '/^$/d' | sort -u | wc -l | tr -d ' ')
[ "$uniq_d" -ge 2 ] || { c=$((c+1)); d="$d Product(fx-arch-repl) 的候选分类深度相同（${depths}）—— \"取最深\"无唯一期望值"$'\n'; }
orphancats=$(printf '%s' "$P" | jq -r '.[] | select(.key=="fx-arch-orphan") | .categories | length')
[ "$orphancats" = "0" ] || { c=$((c+1)); d="$d Product(fx-arch-orphan) 有 $orphancats 个分类（410 样本必须无替代分类）"$'\n'; }
verdict "S9 前提成立（有替代者候选深度不同 / 无替代者分类为空）" "$c" "$d"

# ── C17 SlugHistory 可解析且 newSlug 等于当前 slug ───────────────────────────
# 这条防的是"301 对了但落地 404"：历史行的 newSlug 写错时，301 的目标是一个不存在的地址。
# 只断言"301 存在"的判据在这个错法下完全通过 —— 所以声明侧必须先把两个值对齐。
begin C17; d=""; c=0
while IFS=$'\t' read -r et etv ek loc old new; do
  [ -n "$ek" ] || continue
  cur=""
  case "$et" in
    Product)    cur=$(printf '%s' "$P" | jq -r --arg k "$ek" --arg l "$loc" '.[] | select(.key==$k) | .translations[] | select(.locale==$l) | .slug') ;;
    Category)   cur=$(printf '%s' "$G" | jq -r --arg k "$ek" --arg l "$loc" '.[] | select(.key==$k) | .translations[] | select(.locale==$l) | .slug') ;;
    StaticPage) cur=$(printf '%s' "$S" | jq -r --arg k "$ek" --arg l "$loc" '.[] | select(.key==$k) | .translations[] | select(.locale==$l) | .slug') ;;
    *) c=$((c+1)); d="$d SlugHistory($ek) 的 entityType=$et 不认识"$'\n'; continue ;;
  esac
  [ -n "$cur" ] || { c=$((c+1)); d="$d SlugHistory($ek) 指向的实体在该语种下没有译文行（${loc}）— 无法解析"$'\n'; continue; }
  [ "$cur" = "$new" ] || { c=$((c+1)); d="$d SlugHistory($ek).$loc newSlug=$new 但当前 slug=${cur}（301 会指向一个不存在的地址）"$'\n'; }
  [ "$old" != "$new" ] || { c=$((c+1)); d="$d SlugHistory($ek).$loc oldSlug == newSlug（这条历史行没有意义）"$'\n'; }
done < <(printf '%s' "$H" | jq -r '.[] | "\(.entityType)\t\(.entityTypeValue)\t\(.entityKey)\t\(.locale)\t\(.oldSlug)\t\(.newSlug)"')
# (entityType, locale, oldSlug) 唯一：库里是唯一索引
while IFS= read -r line; do
  [ -n "$line" ] || continue
  c=$((c+1)); d="$d SlugHistory 唯一键重复: $line"$'\n'
done < <(printf '%s' "$H" | jq -r '[.[] | "\(.entityType)\t\(.locale)\t\(.oldSlug)"] | group_by(.) | map(select(length>1) | .[0]) | .[]')
verdict "SlugHistory 可解析、newSlug 与当前 slug 一致、唯一键不重复" "$c" "$d"

# ── C18 分类树：parent 可解析、无环 ─────────────────────────────────────────
begin C18; d=""; c=0
while IFS= read -r key; do
  [ -n "$key" ] || continue
  printf '%s' "$G" | jq -e --arg k "$key" 'any(.[]; .key==$k)' >/dev/null || {
    c=$((c+1)); d="$d 分类 $key 的 parent 指向不存在的分类（面包屑与 301 目标都会断）"$'\n'; }
done < <(printf '%s' "$G" | jq -r '.[] | select(.parent != null) | .parent')
while IFS= read -r key; do
  [ -n "$key" ] || continue
  dep=$(depth "$key")
  [ "$dep" = "-1" ] && { c=$((c+1)); d="$d 分类 $key 的 parent 链成环（深度计算超过 32 层）"$'\n'; }
done < <(printf '%s' "$G" | jq -r '.[].key')
verdict "分类树 parent 可解析且无环" "$c" "$d"

# ═══════════════════════════════════════════════════════════════════════════
# ═══════════════════════════════════════════════════════════════════════════
# ── 收尾：外部命令报错即失败（见顶部 ERRLOG 的说明）──────────────────────────
if [ -s "$ERRLOG" ]; then
  FAILED=$((FAILED+1))
  begin E1
  bad "有外部命令报错被判据吞掉了（下面的内容说明某条判据其实没跑成功）"
  printf '         经 stderr 捕获:\n'
  head -6 "$ERRLOG" | sed 's/^/           | /'
  FAILS="$FAILS
  E1  外部命令报错（见上）"
fi

[ "$PRINT_FLAGS" -eq 1 ] || { hdr "── 总结 ─────────────────────────────────────────────────────────────────"; }
if [ "$PRINT_FLAGS" -eq 0 ]; then
  printf '  %d passed, %d failed, %d warnings  (%s)\n' "$PASSED" "$FAILED" "$WARNED" "$NS"
  if [ "$FAILED" -gt 0 ]; then
    echo "  ── 失败明细 ──"
    printf '%s\n' "$FAILS"
    echo "  修数据，不要把判据加白名单：这些身份是下游断言的期望值来源。"
  fi
fi

# ── --print-flags：把样本身份翻译成 verify-seo-matrix.sh 的命令行 ─────────────
# 存在的理由：那几个 flag 的形态**极易传错，且传错后的报错会指向错误的地方**。
# `--moved-from` 要的是**路径**（不带 base）、且默认 `--locale zh`；
# 而本 fixture 的 SlugHistory 行**全在 en**（逐语种有变更才有行）。
# 于是"照文档抄、不传 --locale"的结果是 `/zh/products/fx-kettle-v1` → 404 →
# 脚本报"SlugHistory 未实现或未登记" → **看起来像实现的缺陷**，实际是 flag 传错。
# 这类"归因漂移"比断言失败难查一个数量级，所以让脚本把正确命令行直接吐出来。
if [ "$PRINT_FLAGS" -eq 1 ]; then
  [ "$FAILED" -eq 0 ] || { echo "# 前置：check-fixture 有 $FAILED 处失败，先修数据再取 flag" >&2; printf '%s\n' "$FAILS" >&2; exit 1; }
  histloc=$(printf '%s' "$H" | jq -r '.[0].locale // "zh"')
  moved_from=$(printf '%s' "$H" | jq -r '[.[] | select(.entityType=="Product")] | .[0] | .oldSlug // empty')
  moved_to=$(printf '%s' "$H" | jq -r '[.[] | select(.entityType=="Product")] | .[0] | .newSlug // empty')
  arch_gone=$(printf '%s' "$P" | jq -r '[.[] | select(.status=="Archived" and (.categories|length)==0)] | .[0].key // empty')
  arch_gone_from=$(printf '%s' "$H" | jq -r --arg k "$arch_gone" '.[] | select(.entityKey==$k) | .oldSlug // empty')
  arch_moved=$(printf '%s' "$P" | jq -r '[.[] | select(.status=="Archived" and (.categories|length)>0)] | .[0].key // empty')
  oos=$(printf '%s' "$P" | jq -r '[.[] | select(.status=="Active") | select(any(.variants[]; (.onHand - .reserved - .sold) == 0))] | .[0].key // empty')
  base=$(j .publicSiteUrl "$DIR/fixture.json")

  cat <<EOF
# 由 scripts/check-fixture.sh --print-flags 生成（数据来自 ${DIR}，勿手改）
# 站点: $base    语种: ${histloc}（**必须显式传，默认是 zh；本 fixture 的 SlugHistory 行全在 ${histloc}**）
./scripts/verify-seo-matrix.sh $base --locale $histloc \\
    --archived-moved     /$histloc/products/$arch_moved \\
    --archived-gone      /$histloc/products/$arch_gone \\
    --archived-gone-from /$histloc/products/$arch_gone_from \\
    --moved-from         /$histloc/products/$moved_from \\
    --moved-to           /$histloc/products/$moved_to \\
    --out-of-stock       /$histloc/products/$oos
EOF
  exit 0
fi

[ "$FAILED" -eq 0 ] || exit 1
exit 0
