#!/usr/bin/env bash
# =============================================================================
# check-migration-checks.sh —— 配置里声明的 CHECK，必须在迁移里真的被创建
#
# 为什么需要它：`dotnet ef migrations has-pending-model-changes`（check.sh 第 8 步）
# 比较的是 **模型 ↔ 快照**，而快照本身就是上一次 `migrations add` 的产物。
# 所以在"配置里加了 HasCheckConstraint、但没重新生成迁移"这种状态下，快照和模型
# 是同时包含它的 —— 第 8 步恒绿，而**从迁移建出来的新库里根本没有这条约束**。
#
# 这是"自证判据"的又一例：判据的对象是它自己的产物。同 `%an`、`rc`、`mtime`。
#
# 判据只做一件事：`HasCheckConstraint("CK_X")` 声明出的每个名字，
# 必须出现在某个**非 Designer、非 Snapshot** 的迁移文件里。
#
# ---- C3：名字对得上，不等于语义对得上 -------------------------------------
# C1 只比名字。本仓真实发生过一次"名字两边都有、表达式不同"：
#   配置 `CategoryConfiguration.cs`  → `"Depth" >= 1 AND "Depth" <= 5`
#   迁移 `M1_Catalog.cs`（Up）       → `"Depth" >= 0 AND "Depth" <= 5`
# 当时 C1 绿、第 8 步 `has-pending-model-changes` 也绿，只有人拿 `docs/02` 正文
# 对一遍才看得出。所以 C3 比**表达式字面量**。
#
# C3 有两个必须写下来的坑，都是"看起来能跑、其实判错"的那种：
#   ① **只看 `Up()`，不看 `Down()`**。`Down()` 里写的是"回到旧定义"，与配置天然不同
#      （`M1_CategoryDepthLowerBound.Down` 就写着 `>= 0`）。不分 Up/Down 去取"最后一次
#      出现"，会把每条**被后续迁移修过**的约束都判成不一致 —— 假红，而假红的下场是
#      有人去改期望值。
#   ② **按文件名顺序回放，`Drop` 清空、`Add`/`CheckConstraint` 覆盖**。同一名字会在
#      多个迁移里出现（建表一次、后来重建表再改一次），只有最后一个 `Up()` 的结果有效。
#
# 为什么排除 Designer：Designer 里也有一份完整模型（内嵌的 TargetModel），
# 它会把 CK 名写进去。不排除的话，这条判据会被 Designer 满足 —— 而 Designer
# 是 `add` 自动生成的，等于又绕回自证。
#
# 为什么"迁移里有、配置里没有"不判红：历史迁移会永久保留当年建过的约束，
# 后来从配置里删掉是正常演进（EF 会另生成一条 Drop 迁移）。只有反方向是缺陷。
#
# 退出码（见 docs/05 §4.6）：0 全过 / 1 有判据失败 / 2 前置缺失（= 不通过）
# =============================================================================
set -uo pipefail

CONF_DIR="${CK_CONF_DIR:-src/OpenStore.Infrastructure/Persistence/Configurations}"
MIG_DIR="${CK_MIG_DIR:-src/OpenStore.Infrastructure/Persistence/Migrations}"

usage() {
  cat <<'EOF'
用法：check-migration-checks.sh [--self-test] [--list]

  无参数        检查本仓
  --self-test   用合成样本证明判据既能绿也能红（fail-before-pass）
  --list        打印判据清单

环境变量（供自证使用）：CK_CONF_DIR / CK_MIG_DIR
EOF
}

print_list() {
  cat <<'EOF'
判据清单（check-migration-checks）：
  [C1] 配置里 HasCheckConstraint 声明的每个名字，必须出现在某个迁移文件里
       失败 rc=1；配置目录不存在或无任何声明 rc=2（前置缺失 ≠ 通过）
  [C2] 明细：配置声明 N 个 / 迁移创建 M 个（M 可多于 N，见文件头说明）
  [C3] 名字对得上之后，**表达式**也必须逐字一致（只看 Up()，按文件名顺序回放到最终态）
       失败 rc=1；无法比对（迁移侧没有可解析的 Up() 表达式）rc=2
EOF
}

# 从配置目录抽 CK 名。
#
# **不能只匹配 `HasCheckConstraint("CK_...` 这一个形状。** 曾经就是那样，代价是
# **4 条里只看见 1 条**（20:58 实测：本式命中 1，真实 4）。同一条约束在本仓有三种
# 合法 EF 写法，后两种一律返回空：
#   ① 单行字面量   t.HasCheckConstraint("CK_X", "…")             ← 旧式唯一看得见的
#   ② 多行调用     table.HasCheckConstraint(\n    "CK_Y",        ← 名字在下一行
#   ③ 常量间接     HasCheckConstraint(DepthCheckName, "…")        ← 名字在同文件的 const 里
#
# 而 **"抽不到"与"真的没有"在输出上完全一样**：判据绿着，三条约束从未被它看过一眼。
# 这与本脚本要抓的病同形（"配置声明了、库里没有"）——只不过这次缺的是**判据自己的视野**。
#
# 所以反过来做两步：先认出"这个文件里有 HasCheckConstraint"，再取该文件里所有
# **带引号的** `CK_` 字面量。② ③ 因此自动覆盖（③ 的名字就在
# `public const string DepthCheckName = "CK_Category_Depth"` 里）。
#
# 为什么限定"带引号"而不是"取所有 CK_ 串"：约束名必须是一个字符串才能进 DB，
# 所以这就是它的**语义条件**，不是格式凑合。副产品是注释里裸写的 `CK_X` 不算声明。
# （已知边界：注释里写成 `// use "CK_X"` 这种带引号的会误收。宁可多收——多收会让
#  它去迁移里找，找不到就红，是响的；少收则是静默绿。）
collect_conf() {
  local f
  find "$1" -name '*.cs' -type f 2>/dev/null | while IFS= read -r f; do
    grep -q 'HasCheckConstraint' "$f" 2>/dev/null || continue
    grep -oE '"CK_[A-Za-z0-9_]+"' "$f" 2>/dev/null | tr -d '"'
  done | LC_ALL=C sort -u
}

# 从迁移目录抽 CK 名：排除 *.Designer.cs 与 StoreDbContextModelSnapshot.cs
collect_mig() {
  local f
  for f in "$1"/*.cs; do
    [ -e "$f" ] || continue
    case "$f" in
      *Designer.cs) continue ;;
      *StoreDbContextModelSnapshot.cs) continue ;;
    esac
    grep -hoE 'CK_[A-Za-z0-9_]+' "$f" 2>/dev/null
  done | LC_ALL=C sort -u
}

# C3：配置里的表达式与迁移里**最终生效**的表达式必须逐字一致。
# 退出码 0=一致 / 1=不一致 / 2=无法比对（前置缺失 ≠ 通过）。
#
# 为什么用 python 而不是继续 bash：要同时做三件 bash 做不可靠的事 ——
# ① 按引号配对切参数（表达式里含逗号，`IN (0, 1)`，裸 `[^,]*` 会切错）；
# ② 括号配对找 `Up()` 体的边界；③ 反转义 `\"`。用 grep/sed 拼出来会在
# 下一种合法写法上静默返回空 —— 那正是 C1 刚栽过的那个坑。
check_exprs() {
  local conf_dir="$1" mig_dir="$2"
  command -v python3 >/dev/null 2>&1 || {
    printf 'PRECONDITION: C3 需要 python3（按引号切参数、找 Up() 体边界）\n'
    return 2
  }
  python3 - "$conf_dir" "$mig_dir" <<'PY'
import os, re, sys, glob

conf_dir, mig_dir = sys.argv[1], sys.argv[2]
STR = r'"((?:[^"\\]|\\.)*)"'
IDENT = r'([A-Za-z_][A-Za-z0-9_]*)'

def strip_comments(s):
    return "\n".join(re.sub(r'(?<!:)//.*$', '', ln) for ln in s.splitlines())

def unesc(lit):
    return lit.replace('\\"', '"').replace("\\'", "'")

def norm(e):
    return re.sub(r'\s+', ' ', e).strip()

# 一个 C# 字符串"值"不止单段字面量，还可以是若干段用 + 拼起来（可跨行）。
# 只认单段会让 CK_PAV_ExactlyOne（配置里 4 行 "…" + "…" + "…"）整条
# HasCheckConstraint 匹配失败 —— 名字进不了 conf，C3 比对数从 9 掉到 8，
# 而 DETAIL 照旧报"8 条一致"，读的人以为 9 条都看过。这正是本脚本要治的
# "DETAIL 的数不度量它声称度量的东西"，只不过这次缺的是判据自己的视野。
CONCAT = STR + r'(?:\s*\+\s*' + STR + r')*'

def read_concat(src, pos):
    """从 src[pos]（应是一个引号）读一段字符串拼接表达式，返回 (值, 结束下标)。
    失败返回 (None, pos)。值仍是源码形态（含 \\" 转义），由调用方 unesc。"""
    m = re.match(CONCAT, src[pos:], re.S)
    if not m:
        return None, pos
    return ''.join(re.findall(STR, m.group(0), re.S)), pos + m.end()

def skip_ws(src, pos):
    while pos < len(src) and src[pos].isspace():
        pos += 1
    return pos

# ---- 配置侧：name -> 表达式。名字可能是字面量、const，表达式可能是 + 拼接。
conf = {}
for f in sorted(glob.glob(os.path.join(conf_dir, '**', '*.cs'), recursive=True)):
    src = strip_comments(open(f, encoding='utf-8').read())
    consts = dict(re.findall(r'const\s+string\s+' + IDENT + r'\s*=\s*' + STR + r'\s*;', src))
    for m in re.finditer(r'HasCheckConstraint\s*\(', src):
        p = skip_ws(src, m.end())
        name = None
        if p < len(src) and src[p] == '"':     # 参数1：字面量名
            name, p = read_concat(src, p)
        else:                                  # 参数1：const 标识符名
            im = re.match(IDENT, src[p:])
            if im:
                name = consts.get(im.group(1))
                p = skip_ws(src, p + im.end())
        if p >= len(src) or src[p] != ',':
            continue
        p = skip_ws(src, p + 1)
        if p >= len(src) or src[p] != '"':    # 参数2：表达式（可拼接）
            continue
        expr, _ = read_concat(src, p)
        if name and expr is not None:
            conf[unesc(name)] = norm(unesc(expr))

# ---- 迁移侧：只回放 Up() 体，按文件名顺序（时间戳前缀 = 迁移顺序）
def up_body(src):
    m = re.search(r'protected\s+override\s+void\s+Up\s*\(', src)
    if not m:
        return None
    i = src.find('{', m.end())
    if i < 0:
        return None
    d = 0
    for k in range(i, len(src)):
        if src[k] == '{':
            d += 1
        elif src[k] == '}':
            d -= 1
            if d == 0:
                return src[i:k + 1]
    return src[i:]

ops = []   # (name, expr 或 None=删除)
for f in sorted(glob.glob(os.path.join(mig_dir, '*.cs'))):
    b = os.path.basename(f)
    # 排除 Designer 与 Snapshot 的理由见文件头：它们内嵌完整 TargetModel，
    # 不排就等于让 `add` 的产物自己满足判据。
    if b.endswith('Designer.cs') or b == 'StoreDbContextModelSnapshot.cs':
        continue
    src = strip_comments(open(f, encoding='utf-8').read())
    body = up_body(src)
    if body is None:
        continue
    for m in re.finditer(r'\b(DropCheckConstraint|AddCheckConstraint|CheckConstraint)\s*\((.*?)\)\s*;', body, re.S):
        fn, args = m.group(1), m.group(2)
        # 参数仍按"命名参数优先、位置参数兜底"解析，但表达式值统一走 read_concat，
        # 与配置侧同构 —— 否则配置能读拼接、迁移读不了，比对数又会对不上。
        named = {}
        for key in ('name', 'sql', 'table'):
            km = re.search(r'\b' + key + r'\s*:\s*', args, re.S)
            if not km:
                continue
            q = skip_ws(args, km.end())
            if q < len(args) and args[q] == '"':
                val, _ = read_concat(args, q)
                if val is not None:
                    named[key] = val
        # 位置参数：按顺序取所有拼接串
        pos_vals = []
        q = 0
        while q < len(args):
            q = skip_ws(args, q)
            if q < len(args) and args[q] == '"':
                val, q = read_concat(args, q)
                if val is not None:
                    pos_vals.append(val)
            else:
                q += 1
        if fn == 'DropCheckConstraint':
            name = unesc(named['name']) if 'name' in named else (unesc(pos_vals[0]) if pos_vals else None)
            if name:
                ops.append((name, None))
            continue
        if 'name' in named and 'sql' in named:
            ops.append((unesc(named['name']), norm(unesc(named['sql']))))
        elif len(pos_vals) >= 2:
            ops.append((unesc(pos_vals[0]), norm(unesc(pos_vals[1]))))

final = {}
for name, expr in ops:
    if expr is None:
        final.pop(name, None)
    else:
        final[name] = expr

bad, cmpd = [], 0
for name in sorted(conf):
    if name not in final:
        continue          # 名字缺失是 C1 的判据，这里不重复报
    cmpd += 1
    if final[name] != norm(conf[name]):
        bad.append((name, norm(conf[name]), final[name]))

if conf and cmpd == 0:
    print('PRECONDITION: C3 无法比对 —— 迁移侧没有任何可解析的 Up() 表达式')
    sys.exit(2)

if bad:
    print('FAIL: 以下 CHECK 的名字对得上，表达式与配置不一致：')
    for n, c, m in bad:
        print('  - %s' % n)
        print('      配置（HasCheckConstraint）：%s' % c)
        print('      迁移（Up() 回放后的最终态）：%s' % m)
    print('      后果：名字对、C1 绿、第 8 步也绿，而库里那条约束的语义是旧的。')
    print('      修法：重新生成迁移，别手工改 Up() 里的 SQL —— 改完配置再 add 才会连快照一起对。')
    print('DETAIL: C3 比对 %d 个 / 不一致 %d 个' % (cmpd, len(bad)))
    sys.exit(1)

print('结论：C3 表达式 %d 条与配置逐字一致' % cmpd)
print('DETAIL: C3 比对 %d 个 / 不一致 0 个' % cmpd)
sys.exit(0)
PY
}

# 参数：配置目录 迁移目录。输出：0 通过 / 1 缺 / 2 前置缺失
check_dirs() {
  local conf_dir="$1" mig_dir="$2"
  local conf mig missing n_conf n_mig

  if [ ! -d "$conf_dir" ]; then
    printf 'PRECONDITION: 配置目录不存在：%s\n' "$conf_dir"
    return 2
  fi
  if [ ! -d "$mig_dir" ]; then
    printf 'PRECONDITION: 迁移目录不存在：%s\n' "$mig_dir"
    return 2
  fi

  conf=$(collect_conf "$conf_dir")
  mig=$(collect_mig "$mig_dir")

  if [ -z "$conf" ]; then
    # 合法状态（M0 早期一条 CHECK 都没有），但它让本判据无法证明任何事。
    # 按 docs/05 §4.6 的口径：**前置缺失 ≠ 通过**。
    printf 'PRECONDITION: 配置里没有任何 HasCheckConstraint 声明，本判据无从证明\n'
    return 2
  fi

  n_conf=$(printf '%s\n' "$conf" | grep -c .)
  n_mig=$(printf '%s\n' "$mig" | grep -c . || true)

  missing=$(comm -23 <(printf '%s\n' "$conf") <(printf '%s\n' "$mig" | grep . || true))

  if [ -n "$missing" ]; then
    printf 'FAIL: 以下 CHECK 在配置里声明了，但没有任何迁移创建它：\n'
    printf '%s\n' "$missing" | sed 's/^/  - /'
    printf '      后果：从迁移建出的新库没有这条约束，而第 8 步 has-pending-model-changes 仍为绿\n'
    printf '            （快照与模型同时包含它 —— 判据的对象是它自己的产物）。\n'
    printf '      修法：dotnet ef migrations add <Name> 重新生成；若是刚提交的迁移，\n'
    printf '            删掉该迁移重生成，别手工往 Up() 里贴。\n'
    printf 'DETAIL: 配置 %s 个 / 迁移 %s 个 / 缺 %s 个\n' "$n_conf" "${n_mig:-0}" "$(printf '%s\n' "$missing" | grep -c .)"
    return 1
  fi

  printf '结论：配置 %s 个 CHECK 全部在迁移里创建\n' "$n_conf"
  printf 'DETAIL: 配置 %s 个 / 迁移 %s 个（迁移多出的部分是历史约束，正常）\n' "$n_conf" "${n_mig:-0}"

  # 名字全对上之后才比表达式：C1 红时表达式没有比较的意义（缺名字比错表达式更靠前）。
  check_exprs "$conf_dir" "$mig_dir"
  return $?
}

self_test() {
  local tmp failures=0
  tmp=$(mktemp -d) || { echo "FAIL: mktemp 失败"; return 1; }
  trap 'rm -rf "$tmp"' RETURN

  # 迁移样本必须带 `Up()` 体 —— C3 只看 `Up()`（`Down()` 写的是"回到旧定义"，
  # 与配置天然不同，见文件头坑①）。不带 `Up()` 的样本会让 C3 走 rc=2，
  # 于是这些样本测的就变成"C3 的前置条件"，而不是它们各自想测的那条。
  # $1=目标文件 $2=约束名 $3=表达式（写 C# 源码里的样子，含 `\"` 转义）
  mk_mig() {
    cat > "$1" <<CS
using Microsoft.EntityFrameworkCore.Migrations;
public partial class M : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "T",
            columns: table => new { },
            constraints: table =>
            {
                table.CheckConstraint("$2", "$3");
            });
    }
}
CS
  }

  # 样本 1：配置有、迁移有、表达式一致 → 必须绿
  mkdir -p "$tmp/s1/conf" "$tmp/s1/mig"
  printf 'b.HasCheckConstraint("CK_A_Bit", "\\"Bit\\" IN (0,1)");\n' > "$tmp/s1/conf/XConfiguration.cs"
  mk_mig "$tmp/s1/mig/M1.cs" "CK_A_Bit" '\"Bit\" IN (0,1)'
  if CK_CONF_DIR="$tmp/s1/conf" CK_MIG_DIR="$tmp/s1/mig" bash "$0" >/dev/null 2>&1; then
    echo "  [ok] 样本1 配置与迁移一致 → 绿"
  else
    echo "  [FAIL] 样本1 配置与迁移一致，却判红了"; failures=$((failures+1))
  fi

  # 样本 2：配置有、迁移没有 → 必须红（这是主判据）
  mkdir -p "$tmp/s2/conf" "$tmp/s2/mig"
  printf 'b.HasCheckConstraint("CK_B_Bit", "\\"Bit\\" IN (0,1)");\n' > "$tmp/s2/conf/XConfiguration.cs"
  printf 'table.CheckConstraint("CK_A_Bit", "\\"Bit\\" IN (0,1)");\n' > "$tmp/s2/mig/M1.cs"
  set +e
  CK_CONF_DIR="$tmp/s2/conf" CK_MIG_DIR="$tmp/s2/mig" bash "$0" >/dev/null 2>&1
  local rc=$?
  set -e
  if [ "$rc" -eq 1 ]; then
    echo "  [ok] 样本2 配置有、迁移无 → rc=1"
  else
    echo "  [FAIL] 样本2 期望 rc=1，实际 rc=$rc"; failures=$((failures+1))
  fi

  # 样本 3：CK 只出现在 Designer 里 → 必须红（证明"排除 Designer"真的生效）
  mkdir -p "$tmp/s3/conf" "$tmp/s3/mig"
  printf 'b.HasCheckConstraint("CK_C_Bit", "\\"Bit\\" IN (0,1)");\n' > "$tmp/s3/conf/XConfiguration.cs"
  printf 'table.CheckConstraint("CK_C_Bit", "\\"Bit\\" IN (0,1)");\n' > "$tmp/s3/mig/M1.Designer.cs"
  set +e
  CK_CONF_DIR="$tmp/s3/conf" CK_MIG_DIR="$tmp/s3/mig" bash "$0" >/dev/null 2>&1
  rc=$?
  set -e
  if [ "$rc" -eq 1 ]; then
    echo "  [ok] 样本3 CK 只在 Designer 里 → rc=1（排除规则生效）"
  else
    echo "  [FAIL] 样本3 期望 rc=1（Designer 不算创建），实际 rc=$rc"; failures=$((failures+1))
  fi

  # 样本 4：配置目录为空 → rc=2（前置缺失 ≠ 通过）
  mkdir -p "$tmp/s4/conf" "$tmp/s4/mig"
  printf 'table.CheckConstraint("CK_A_Bit", "x");\n' > "$tmp/s4/mig/M1.cs"
  set +e
  CK_CONF_DIR="$tmp/s4/conf" CK_MIG_DIR="$tmp/s4/mig" bash "$0" >/dev/null 2>&1
  rc=$?
  set -e
  if [ "$rc" -eq 2 ]; then
    echo "  [ok] 样本4 配置无声明 → rc=2（不装作通过）"
  else
    echo "  [FAIL] 样本4 期望 rc=2，实际 rc=$rc"; failures=$((failures+1))
  fi

  # 样本 5：迁移目录不存在 → rc=2
  mkdir -p "$tmp/s5/conf"
  printf 'b.HasCheckConstraint("CK_A_Bit", "x");\n' > "$tmp/s5/conf/XConfiguration.cs"
  set +e
  CK_CONF_DIR="$tmp/s5/conf" CK_MIG_DIR="$tmp/s5/mig-nope" bash "$0" >/dev/null 2>&1
  rc=$?
  set -e
  if [ "$rc" -eq 2 ]; then
    echo "  [ok] 样本5 迁移目录不存在 → rc=2"
  else
    echo "  [FAIL] 样本5 期望 rc=2，实际 rc=$rc"; failures=$((failures+1))
  fi

  # 样本 6：抽取式本身的三形态（**这一条是 20:58 那次漏报的直接产物**）
  #
  # 原来的 5 个样本全是 `printf 'b.HasCheckConstraint("CK_X", …)'` —— **单行字面量**。
  # 五个样本都在测"判据对不对"，没有一个在测"抽取看不看得见"。而实测本仓 4 条约束里
  # 3 条不是这个形状，判据绿着说"配置 1 个 CHECK 全部在迁移里创建"。
  # 所以这里直接测 `collect_conf` 的输出集合，不绕经退出码 —— 退出码分不出"抽到 1 个且一致"
  # 与"抽到 4 个且一致"。
  mkdir -p "$tmp/s6/conf"
  cat > "$tmp/s6/conf/AConfiguration.cs" <<'CS'
public const string DepthCheckName = "CK_Six_Const";
void Cfg() {
    builder.ToTable("A", t => t.HasCheckConstraint("CK_Six_Inline", "\"X\" >= 0"));
    builder.ToTable("B", t => table.HasCheckConstraint(
        "CK_Six_Multiline",
        "\"Y\" >= 0"));
    builder.ToTable("C", t => t.HasCheckConstraint(DepthCheckName, "\"Z\" >= 0"));
    // 注释里裸写的 CK_Six_Bare 不是声明（没有引号）
}
CS
  local got6 want6
  got6=$(printf '%s\n' "$(CK_SIX=1; collect_conf "$tmp/s6/conf")" | grep . | LC_ALL=C sort | tr '\n' ' ')
  want6="CK_Six_Const CK_Six_Inline CK_Six_Multiline "
  if [ "$got6" = "$want6" ]; then
    echo "  [ok] 样本6 抽取覆盖三种写法（单行字面量 / 多行调用 / const 间接），注释里的裸串不算"
  else
    echo "  [FAIL] 样本6 抽取式漏了写法：期望 [$want6] 实得 [$got6]"
    failures=$((failures+1))
  fi

  # 样本 7：名字对得上、**表达式不同** → 必须红（这是 C3 存在的唯一理由）
  #
  # 形状取自本仓真实事故：配置 `>= 1`、`M1_Catalog` 的 Up 里 `>= 0`。
  # C1 在这一格是绿的（名字两边都有），第 8 步也是绿的 —— 只有 C3 能发现。
  mkdir -p "$tmp/s7/conf" "$tmp/s7/mig"
  printf 'b.HasCheckConstraint("CK_D_Depth", "\\"Depth\\" >= 1 AND \\"Depth\\" <= 5");\n' > "$tmp/s7/conf/XConfiguration.cs"
  mk_mig "$tmp/s7/mig/M1.cs" "CK_D_Depth" '\"Depth\" >= 0 AND \"Depth\" <= 5'
  set +e
  local out7
  out7=$(CK_CONF_DIR="$tmp/s7/conf" CK_MIG_DIR="$tmp/s7/mig" bash "$0" 2>&1)
  rc=$?
  set -e
  if [ "$rc" -eq 1 ] && printf '%s\n' "$out7" | grep -q '表达式与配置不一致'; then
    echo "  [ok] 样本7 名字对、表达式不同 → rc=1 且红在「表达式与配置不一致」"
  else
    echo "  [FAIL] 样本7 期望 rc=1 且红在表达式，实际 rc=$rc"; failures=$((failures+1))
  fi

  # 样本 8：Up() 一致、**Down() 不同** → 必须绿
  #
  # 这一条防的是 C3 最容易写错的那一版：不分 Up/Down 取"最后一次出现"。
  # 真实文件 `M1_CategoryDepthLowerBound.cs` 的 Down 就是往回写成 `>= 0`，
  # 取最后一次会把每条被后续迁移修过的约束全判成不一致 —— 假红，
  # 而假红的下场是有人去改期望值，改完这条判据就废了。
  mkdir -p "$tmp/s8/conf" "$tmp/s8/mig"
  printf 'b.HasCheckConstraint("CK_E_Depth", "\\"Depth\\" >= 1");\n' > "$tmp/s8/conf/XConfiguration.cs"
  cat > "$tmp/s8/mig/M1.cs" <<'CS'
using Microsoft.EntityFrameworkCore.Migrations;
public partial class M : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(name: "CK_E_Depth", table: "T", sql: "\"Depth\" >= 1");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(name: "CK_E_Depth", table: "T", sql: "\"Depth\" >= 0");
    }
}
CS
  set +e
  CK_CONF_DIR="$tmp/s8/conf" CK_MIG_DIR="$tmp/s8/mig" bash "$0" >/dev/null 2>&1
  rc=$?
  set -e
  if [ "$rc" -eq 0 ]; then
    echo "  [ok] 样本8 Up() 一致、Down() 不同 → 绿（只回放 Up，不取最后一次出现）"
  else
    echo "  [FAIL] 样本8 期望 rc=0（Down 不算最终态），实际 rc=$rc"; failures=$((failures+1))
  fi

  # 样本 9：表达式是 4 行 "…" + "…" 拼接（本仓 CK_PAV_ExactlyOne 的真实形状）
  # 且 const 间接名字 → 必须绿，且比对数含这一条。
  # 这一条是 data-engineer 报"DETAIL 说比 8 条、配置其实 9 条"的直接产物：
  # 旧正则只认单个字符串字面量，拼接那一整条 HasCheckConstraint 匹配失败、名字进不了 conf。
  mkdir -p "$tmp/s9/conf" "$tmp/s9/mig"
  cat > "$tmp/s9/conf/AConfiguration.cs" <<'CS'
public const string ExactlyOneName = "CK_F_Concat";
void Cfg() {
    t.HasCheckConstraint(
        ExactlyOneName,
        "(CASE WHEN \"A\" IS NOT NULL THEN 1 ELSE 0 END"
        + " + CASE WHEN \"B\" IS NOT NULL THEN 1 ELSE 0 END) = 1");
}
CS
  mk_mig "$tmp/s9/mig/M1.cs" "CK_F_Concat" \
    '(CASE WHEN \"A\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"B\" IS NOT NULL THEN 1 ELSE 0 END) = 1'
  set +e
  local out9
  out9=$(CK_CONF_DIR="$tmp/s9/conf" CK_MIG_DIR="$tmp/s9/mig" bash "$0" 2>&1)
  rc=$?
  set -e
  if [ "$rc" -eq 0 ] && printf '%s\n' "$out9" | grep -q 'C3 比对 1 个 / 不一致 0 个'; then
    echo "  [ok] 样本9 拼接表达式 + const 名 → 绿，且比对数含这一条（不是静默跳过）"
  else
    echo "  [FAIL] 样本9 期望 rc=0 且比对 1 条，实际 rc=$rc 输出=[$out9]"; failures=$((failures+1))
  fi

  # 样本 10：拼接表达式里某一段不同 → 必须红在「表达式与配置不一致」。
  # 没有这一条，样本9 可能被"根本没比、直接放行"满足 —— 那是恒绿，不是判据。
  mkdir -p "$tmp/s10/conf" "$tmp/s10/mig"
  cat > "$tmp/s10/conf/AConfiguration.cs" <<'CS'
public const string ExactlyOneName = "CK_G_Concat";
void Cfg() {
    t.HasCheckConstraint(
        ExactlyOneName,
        "(CASE WHEN \"A\" IS NOT NULL THEN 1 ELSE 0 END"
        + " + CASE WHEN \"B\" IS NOT NULL THEN 1 ELSE 0 END) = 1");
}
CS
  mk_mig "$tmp/s10/mig/M1.cs" "CK_G_Concat" \
    '(CASE WHEN \"A\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"B\" IS NOT NULL THEN 1 ELSE 0 END) = 2'
  set +e
  local out10
  out10=$(CK_CONF_DIR="$tmp/s10/conf" CK_MIG_DIR="$tmp/s10/mig" bash "$0" 2>&1)
  rc=$?
  set -e
  if [ "$rc" -eq 1 ] && printf '%s\n' "$out10" | grep -q '表达式与配置不一致'; then
    echo "  [ok] 样本10 拼接表达式某段不同 → rc=1 且红在「表达式与配置不一致」"
  else
    echo "  [FAIL] 样本10 期望 rc=1 且红在表达式，实际 rc=$rc"; failures=$((failures+1))
  fi

  echo
  if [ "$failures" -eq 0 ]; then
    echo "自证通过：10/10"
    return 0
  fi
  echo "自证失败：$failures/10 条样本未达预期"
  return 1
}

case "${1:-}" in
  --list) print_list; exit 0 ;;
  --self-test) self_test; exit $? ;;
  "") check_dirs "$CONF_DIR" "$MIG_DIR"; exit $? ;;
  -h|--help) usage; exit 0 ;;
  *) printf '未知参数：%s\n' "$1" >&2; usage >&2; exit 2 ;;
esac
