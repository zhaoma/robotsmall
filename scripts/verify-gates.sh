#!/usr/bin/env bash
# =============================================================================
# OpenStore 门禁自证：证明 check.sh 里每道门禁真的会拦住违规
#
# 用法：
#   ./scripts/verify-gates.sh              # 跑全部注入
#   ./scripts/verify-gates.sh --only=V3    # 只跑一条
#   ./scripts/verify-gates.sh --list
#
# 退出码：0 全部注入都被拦住 / 1 至少一条门禁是摆设 / 2 用法或环境错误
#
# -----------------------------------------------------------------------------
# 为什么不用户手工维护的 .patch 文件
# -----------------------------------------------------------------------------
# 原设计是 `git apply scripts/patches/V1.patch`。它有一个致命退化：
# **static patch 与代码位置强耦合。** 有人改了 Money.cs 的空白/顺序，V1.patch
# 就 apply 失败；而 apply 失败看起来像"环境问题"，最自然的处置是注释掉这条注入。
# 于是门禁自证静默缩小，而 CI 依旧全绿 —— 这正是我们要防的东西，只是换了个层级。
#
# 所以这里改成「脚本化变更 + 锚点前置断言」：
#   - 目标文件靠 glob 发现，不写死路径（文件会移动）
#   - 变更靠模式匹配替换，不依赖行号（行号会漂）
#   - **锚点找不到 → 立即大声失败**，绝不跳过
# 锚点断言把"补丁失效"从静默降级变成响亮失败，这是整个脚本最重要的一行。
#
# -----------------------------------------------------------------------------
# 断言规则（缺一不可）
# -----------------------------------------------------------------------------
# 每条注入必须同时满足两条，才算"红对了地方"：
#   ① 门禁退出码非 0
#   ② 输出包含预期片段（错误码/规则名）
# 只断 ①，注入可能红在编译错误上；只断 ②，可能红在另一条无关断言上。
# 这是 devops 在 verify-seo-matrix.sh 上踩过的坑：三个注入全红在同一条断言上，
# 看起来 3/3 通过，实际只证明了 1 件事。
# =============================================================================
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.." || exit 2
ROOT="$PWD"

if [ -z "${DOTNET_ROOT:-}" ] && [ -d "$HOME/.dotnet" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
fi
case ":$PATH:" in
  *":$HOME/.dotnet/tools:"*) ;;
  *) PATH="$HOME/.dotnet/tools:$PATH"; export PATH ;;
esac

ONLY=""
case "${1:-}" in
  --only=*) ONLY="${1#--only=}" ;;
  --list)
    grep -oE 'V[0-9]+ (layering|bounded_contexts|authorization|storefront_dto|templates|themes|migrations|domain_tests)' "$0" \
      | sort -u -V
    exit 0 ;;
  "") ;;
  *) echo "unknown arg: $1" >&2; exit 2 ;;
esac

BAKDIR="$(mktemp -d)"
declare -a BACKUPS=()
PASS=0; FAIL=0; SKIPN=0
FAILED=()

# 只恢复文件，不清临时目录。
# 早先这两件事写在一个函数里，结果第一条注入跑完就把 $BAKDIR 删了，
# 后续注入的 log 与备份全部"No such file or directory" —— 6 条注入因此假失败。
# 教训：**恢复(幂等、可频繁调用)与清理(一次性、销毁状态)必须是两个操作。**
restore_files() {
  local entry file bak
  for entry in "${BACKUPS[@]:-}"; do
    [ -z "$entry" ] && continue
    file="${entry%%|*}"; bak="${entry##*|}"
    [ -f "$bak" ] && cp -f "$bak" "$file"
  done
  BACKUPS=()
}

cleanup() { restore_files; rm -rf "$BAKDIR"; }
trap cleanup EXIT INT TERM

say()  { printf '%s\n' "$*"; }
head_of() { sed -e 's/^/    /' "$1" | head -"${2:-12}"; }

# 靠 glob 发现目标文件；找不到就返回 1（调用方必须据此大声失败）
find_one() {
  local f
  for pat in "$@"; do
    f=$(ls $pat 2>/dev/null | head -1)
    [ -n "$f" ] && { printf '%s' "$f"; return 0; }
  done
  return 1
}

# 备份 + 记录，供 trap 恢复
snapshot() {
  local file="$1"
  local bak="$BAKDIR/$(printf '%s' "$file" | tr '/' '_')"
  cp -f "$file" "$bak"
  BACKUPS+=("$file|$bak")
}

# 变更前断言锚点存在。缺失 = 代码搬走了 = 这条注入已失效，必须红。
require_anchor() {
  local file="$1" pattern="$2"
  if ! grep -qE "$pattern" "$file" 2>/dev/null; then
    say "  ANCHOR MISSING in $file: /$pattern/"
    say "  → 这条注入的目标代码已经移动或改写。请修本脚本的锚点，不要删除这条注入。"
    return 1
  fi
  return 0
}

# 前置：先构建一次，确保基线是绿的。基线不绿的话所有"变红"都没有意义。
say "== 0) 基线检查（必须先绿）=="
if ! dotnet build OpenStore.sln -warnaserror --nologo -v q >"$BAKDIR/baseline.log" 2>&1; then
  say "基线构建失败，门禁自证无意义（变红可能只是因为本来就是红的）。"
  head_of "$BAKDIR/baseline.log" 30
  exit 2
fi
say "  基线构建 ok"
say ""

# -----------------------------------------------------------------------------
# 注入执行器
#   prove <id> <规则名/门禁命令...>
# 调用前需已 snapshot + 完成变更。
# -----------------------------------------------------------------------------
run_injection() {
  local id="$1" gate_name="$2" expect="$3"; shift 3
  local log="$BAKDIR/$id.log"

  printf '  %-3s %-18s ' "$id" "$gate_name"

  # gate 在子 shell 里跑，避免 set -e 泄漏；失败是预期路径
  if "$@" >"$log" 2>&1; then
    printf 'NO-OP\n'
    say "    门禁仍然绿 —— 这条规则是摆设。"
    head_of "$log" 8
    FAIL=$((FAIL+1)); FAILED+=("$id:$gate_name")
    return
  fi

  # ① 已红。② 还要红在预期位置。
  # expect 允许写多个候选，用 `|` 分隔——但只能按**固定字符串**逐个比对，
  # 不能直接 grep -F "a|b"（那会把竖线当字面量，永远匹配不到，导致"明明红对了却判错位"）。
  local matched=0 part
  local IFS='|'
  for part in $expect; do
    if grep -qF -- "$part" "$log"; then matched=1; break; fi
  done
  unset IFS

  if [ "$matched" -eq 0 ]; then
    printf 'WRONG-RED\n'
    say "    门禁红了，但没红在预期断言上（期望包含其一：$expect）"
    say "    实际输出前 10 行："
    head_of "$log" 10
    FAIL=$((FAIL+1)); FAILED+=("$id:$gate_name(wrong-red)")
    return
  fi

  printf 'ok\n'
  PASS=$((PASS+1))
}

# 便捷：门禁命令
# 注意：这里**不能加 --no-build**。注入改的是源码，--no-build 会跑上一轮编译出的 dll，
# 于是"改了却仍然绿"，看起来像门禁失灵，实际上是根本没被测到。M0 联调时 V8 就是这样假绿的。
g_arch()   { dotnet test tests/OpenStore.ArchitectureTests --nologo --filter "Category=$1" --logger "console;verbosity=detailed"; }
g_domain() { dotnet test tests/OpenStore.Domain.Tests --nologo --logger "console;verbosity=detailed"; }
g_tools()  { dotnet run --project tools/OpenStore.Tools -- "$1"; }

say "== 注入清单 =="

# -----------------------------------------------------------------------------
# V1  分层：Domain 引用 EF
# 注意：必须同时改 csproj，否则注入代码编译不过，会红在编译器上（假红）
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V1" ]; then
  F=$(find_one "src/OpenStore.Domain/Shared/Money.cs" "src/OpenStore.Domain/**/Money.cs")
  CSP=$(find_one "src/OpenStore.Domain/OpenStore.Domain.csproj")
  if [ -n "$F" ] && [ -n "$CSP" ] && require_anchor "$F" "record struct Money|class Money" \
     && require_anchor "$CSP" "</Project>"; then
    snapshot "$F"; snapshot "$CSP"
    python3 - "$CSP" "$F" <<'PY'
import sys,re
csp,src=sys.argv[1],sys.argv[2]
t=open(csp,encoding='utf-8').read()
t=t.replace('</Project>','  <ItemGroup><PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" /></ItemGroup>\n</Project>')
open(csp,'w',encoding='utf-8').write(t)
s=open(src,encoding='utf-8').read()
s='using Microsoft.EntityFrameworkCore;\n'+s
# 文件用的是 file-scoped namespace，直接追加类型即可；
# 早先追加 "namespace X { ... }" 会触发 CS8955（两种 namespace 形式不能共存），
# 于是门禁"红了"，但红在编译器上 —— 那是假红，架构测试根本没跑。
s+='\ninternal static class __EfLeak { internal static System.Type T => typeof(DbContext); }\n'
open(src,'w',encoding='utf-8').write(s)
PY
    run_injection V1 layering "Domain must not reference Microsoft.EntityFrameworkCore" g_arch Layering
    restore_files
  fi
fi

# -----------------------------------------------------------------------------
# V2  上下文互引：Catalog 依赖 Ordering
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V2" ]; then
  F=$(find_one "src/OpenStore.Domain/Catalog/Product.cs")
  if [ -n "$F" ] && require_anchor "$F" "class Product"; then
    snapshot "$F"
    python3 - "$F" <<'PY'
import sys
p=sys.argv[1]
s=open(p,encoding='utf-8').read()
s='using OpenStore.Domain.Ordering;\n'+s
s+='\ninternal static class __CtxLeak { internal static OrderStatus S => OrderStatus.Paid; }\n'
open(p,'w',encoding='utf-8').write(s)
PY
    run_injection V2 bounded_contexts "Domain.Catalog must not reference Domain.Ordering" g_arch BoundedContexts
    restore_files
  fi
fi

# -----------------------------------------------------------------------------
# V3  授权：删掉 admin 端点的 perm 策略
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V3" ]; then
  F=$(find_one "src/OpenStore.Api/Endpoints/Admin/*.cs")
  if [ -n "$F" ] && require_anchor "$F" "RequireAuthorization\(Permissions\."; then
    snapshot "$F"
    python3 - "$F" <<'PY'
import sys,re
p=sys.argv[1]
s=open(p,encoding='utf-8').read()
s=re.sub(r'\.RequireAuthorization\(Permissions\.[A-Za-z]+\)','.RequireAuthorization()',s)
open(p,'w',encoding='utf-8').write(s)
PY
    run_injection V3 authorization "has no permission policy" g_arch Authorization
    restore_files
  fi
fi

# -----------------------------------------------------------------------------
# V4  前台数据隔离：DTO 加内部字段
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V4" ]; then
  F=$(find_one "src/OpenStore.Application/**/StorefrontProductDto.cs")
  if [ -n "$F" ] && require_anchor "$F" "class StorefrontProductDto"; then
    snapshot "$F"
    python3 - "$F" <<'PY'
import sys,re
p=sys.argv[1]
s=open(p,encoding='utf-8').read()
i=s.rfind('}')
s=s[:i]+'    public long CostAmount { get; init; }\n'+s[i:]
open(p,'w',encoding='utf-8').write(s)
PY
    run_injection V4 storefront_dto "CostAmount leaks admin field" g_arch StorefrontDto
    restore_files
  fi
fi

# -----------------------------------------------------------------------------
# V5  模板 schema：删掉 defaults.themeId
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V5" ]; then
  F=$(find_one "assets/industry-templates/womenswear/template.json")
  if [ -n "$F" ] && require_anchor "$F" '"themeId"'; then
    snapshot "$F"
    python3 - "$F" <<'PY'
import sys,json
p=sys.argv[1]
d=json.load(open(p,encoding='utf-8'))
d.get('defaults',{}).pop('themeId',None)
json.dump(d,open(p,'w',encoding='utf-8'),ensure_ascii=False,indent=2)
PY
    run_injection V5 templates "SCHEMA_INVALID required property themeId missing" g_tools check-templates
    restore_files
  fi
fi

# -----------------------------------------------------------------------------
# V6  主题：声明区块但不给 props schema（结构层合法 → 必须语义层抓）
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V6" ]; then
  F=$(find_one "themes/base/blocks.schema.json" "themes/base/editable.schema.json")
  if [ -n "$F" ] && require_anchor "$F" 'blockKey|"enum"'; then
    snapshot "$F"
    python3 - "$F" <<'PY'
import sys,re
p=sys.argv[1]
s=open(p,encoding='utf-8').read()
# 往第一个 enum 数组里追加 "countdown"，不定义其 props
s2=re.sub(r'("enum"\s*:\s*\[)', r'\1"countdown",', s, count=1)
open(p,'w',encoding='utf-8').write(s2)
PY
    run_injection V6 themes "BLOCK_PROPS_MISSING" g_tools check-themes
    restore_files
  fi
fi

# -----------------------------------------------------------------------------
# V7  迁移漂移：改模型映射但不重新生成迁移
#
# 为什么改表名而不是改 HasMaxLength：**SQLite provider 下 MaxLength 变更不触发迁移漂移。**
# 实测（M0）：Slug 200→333、Title 300→111 均返回 "No changes"；而
# ToTable("Product") → ToTable("ProductSnapshotDrift") 立刻返回
# "Changes have been made to the model"。原因是 SQLite 把字符串一律映射成 TEXT，
# 长度只存在于逻辑模型、不改变存储类型，迁移 differ 不视为结构变更。
#
# 这个差异本身是个**必须记住的结论**：任何依赖 MaxLength 的校验（如"slug 不超 200"）
# 都不能指望迁移体系兜底，必须在应用层显式校验；否则换库到 PostgreSQL 时会突然开始
# 生效，而 SQLite 上从未拦过 —— 属"只在生产暴露"的偏差，最难排查的一类。
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V7" ]; then
  F=$(find_one "src/OpenStore.Infrastructure/Persistence/Configurations/ProductConfiguration.cs")
  if [ -n "$F" ] && require_anchor "$F" 'ToTable\("Product"\)'; then
    snapshot "$F"
    python3 - "$F" <<'PY'
import sys
p=sys.argv[1]
s=open(p,encoding='utf-8').read().replace('ToTable("Product")','ToTable("ProductSnapshotDrift")',1)
open(p,'w',encoding='utf-8').write(s)
PY
    run_injection V7 migrations "Changes have been made to the model|模型有未提交的迁移变更" \
      bash -c 'out=$(dotnet ef migrations has-pending-model-changes -p src/OpenStore.Infrastructure -s src/OpenStore.Infrastructure 2>&1); rc=$?; printf "%s\n" "$out" | tail -20; exit $rc'
    restore_files
  fi
fi

# -----------------------------------------------------------------------------
# V8  状态机：白名单放行一个非法转换（依赖 36 组合穷举才能抓到）
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V8" ]; then
  F=$(find_one "src/OpenStore.Domain/Ordering/OrderStatusMachine.cs")
  if [ -n "$F" ] && require_anchor "$F" "PendingPayment"; then
    snapshot "$F"
    python3 - "$F" <<'PY'
import sys,re
p=sys.argv[1]
s=open(p,encoding='utf-8').read()
# 找到 PendingPayment 的允许集合，把 Shipped 塞进去
m=re.search(r'(\[?\s*OrderStatus\.PendingPayment\s*\]?\s*(?:=>|:|=)\s*(?:new\[\]\s*|new\(\)\s*)?\{)', s)
if not m:
    sys.exit(3)
s=s[:m.end()]+' OrderStatus.Shipped,'+s[m.end():]
open(p,'w',encoding='utf-8').write(s)
PY
    if [ $? -ne 0 ]; then
      say "  V8 ANCHOR SHAPE MISSING：OrderStatusMachine 的允许表结构与预期不符，注入未生效。"
      FAIL=$((FAIL+1)); FAILED+=("V8:anchor")
    else
      run_injection V8 domain_tests "ORDER_INVALID_TRANSITION" g_domain
    fi
    restore_files
  fi
fi

# -----------------------------------------------------------------------------
say ""
say "== 汇总: proven=$PASS failed=$FAIL =="
if [ "$FAIL" -gt 0 ]; then
  printf '失效/错位的注入: %s\n' "${FAILED[*]}"
  say ""
  say "任一条失败都意味着「这道路门禁不能证明自己会拦住违规」。"
  say "处置顺序：① 先怀疑锚点漂移（改动脚本锚点）② 再怀疑门禁真的失效（修门禁）"
  say "**不要注释掉这条注入。** 注释掉之后 CI 依然全绿，而覆盖已经悄悄少了一块。"
  exit 1
fi
say "全部注入都被拦住，且都红在预期断言上。"
exit 0
