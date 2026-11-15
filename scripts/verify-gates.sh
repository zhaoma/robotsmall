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
#         3 注入靶点已有未提交改动（基线不干净，拒绝开跑）
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
    grep -oE 'V[0-9]+ (layering|bounded_contexts|authorization|storefront_dto|templates|themes|migrations|domain_tests|doc_version_refs)' "$0" \
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

# 靠模式发现目标文件；找不到就返回 1（调用方必须据此大声失败）
#
# 用 find -path 而不是 `ls $pat`：ls 的 `**` 与 `*` 等价，只能跨一层目录。
# M0 实测：V4 的 src/OpenStore.Application/**/StorefrontProductDto.cs 实际位于
# Catalog/Dtos/Storefront/ 下（三层），ls 匹配不到 → find_one 返回空 → 整段 if 落空
# → V4 从未执行过一次，而脚本照样退出 0。**声明了 8 条注入，实跑 7 条还报成功**，
# 这正是本脚本开头写"跳过是最危险的绿"要防的东西。
# find 的 -path 里 `*` 可以跨 `/`，所以 `**` 与 `*` 都能跨任意层，语义符合直觉。
find_one() {
  local pat hit
  for pat in "$@"; do
    hit=$(find . -type f -path "./$pat" \
            -not -path "*/bin/*" -not -path "*/obj/*" -not -path "*/node_modules/*" 2>/dev/null \
          | sort | head -1)
    [ -n "$hit" ] && { printf '%s' "${hit#./}"; return 0; }
  done
  return 1
}

# 备份 + 记录，供 trap 恢复。**并且在这里断言靶点洁净。**
#
# 把洁净断言放在 snapshot 里，是因为每条注入改文件前都必然先 snapshot ——
# 放这儿等于零成本覆盖全部注入，不会漏，也不可能有人"新加一条注入忘了检查"。
#
# 为什么必须检查（M0 实测踩过）：恢复靠 trap，而 trap 对 SIGKILL / 被外层强行终止不生效。
# 上一次运行被打断时靶点会留在改坏状态，于是下一轮 V1 往已经带 PackageReference 的 csproj
# 再加一条 → NU1504 重复引用 → 构建失败 → 门禁"红了"但红在编译器上 → 判 WRONG-RED。
# 而人看到 WRONG-RED 的第一反应是"锚点漂了，去改锚点"，真正的原因（树脏了）就被埋掉，
# 还会顺手把一条本来正确的注入改坏。所以宁可在这里响亮地停住。
snapshot() {
  local file="$1"
  local st
  if [ -d .git ]; then
    st="$(git status --porcelain -- "$file" 2>/dev/null || true)"
    if [ -n "$st" ]; then
      say ""
      say "== 前置检查失败：注入靶点有未提交改动 =="
      say "  $st"
      say "  注入必须从干净基线出发，否则残留会叠加，结果不可解释（见 snapshot 注释）。"
      say "  确认这处改动是残留而非你在保留的修改，然后：git checkout -- $file"
      exit 3
    fi
  fi

  local bak="$BAKDIR/$(printf '%s' "$file" | tr '/' '_')"
  cp -f "$file" "$bak"
  BACKUPS+=("$file|$bak")
}

# 变更前断言锚点存在。缺失 = 代码搬走了 = 这条注入已失效，必须红。
#
# 注意它**自己记账**（FAIL/FAILED），不再只是 return 1 让调用方落空。
# 早先版本里调用方写的是 `if [ -n "$F" ] && require_anchor ...; then`，
# 锚点缺失时整个 if 直接为假、什么也不发生 —— 打印了一行 "ANCHOR MISSING" 就继续跑，
# 最终汇总照样是 failed=0、退出码 0。**"声明要红"和"真的红了"之间差了一次记账。**
require_anchor() {
  local id="$1" gate="$2" file="$3" pattern="$4"
  if ! grep -qE "$pattern" "$file" 2>/dev/null; then
    printf '  %-3s %-18s ' "$id" "$gate"; printf 'ANCHOR-MISSING\n'
    say "  在 $file 里找不到锚点：/$pattern/"
    say "  → 注入目标代码已经移动或改写。请修本脚本的锚点，**不要删除这条注入**。"
    FAIL=$((FAIL+1)); FAILED+=("$id:anchor-missing")
    return 1
  fi
  return 0
}

# 靶点缺失同样必须记账。这条注入等于不存在，绝不能变成"少一条但报成功"。
require_target() {
  local id="$1" gate="$2" pattern="$3"
  printf '  %-3s %-18s ' "$id" "$gate"; printf 'NO-TARGET\n'
  say "  找不到注入靶点：$pattern"
  say "  → 文件被移动/改名，或 find 模式写错。这条注入目前等于不存在，必须修。"
  FAIL=$((FAIL+1)); FAILED+=("$id:no-target")
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
    say "    门禁红了，但没红在预期断言上（期望包含其一：${expect}）"
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
g_doc()    { ./scripts/check-doc-version-refs.sh; }

say "== 注入清单 =="

# -----------------------------------------------------------------------------
# V1  分层：Domain 引用 EF
# 注意：必须同时改 csproj，否则注入代码编译不过，会红在编译器上（假红）
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V1" ]; then
  F=$(find_one "src/OpenStore.Domain/Shared/Money.cs" "src/OpenStore.Domain/**/Money.cs")
  CSP=$(find_one "src/OpenStore.Domain/OpenStore.Domain.csproj")
  if [ -z "$F" ]; then
    require_target V1 layering "src/OpenStore.Domain/**/Money.cs"
  elif [ -z "$CSP" ]; then
    require_target V1 layering "src/OpenStore.Domain/OpenStore.Domain.csproj"
  elif require_anchor V1 layering "$F" "record struct Money|class Money" \
     && require_anchor V1 layering "$CSP" "</Project>"; then
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
  if [ -z "$F" ]; then
    require_target V2 bounded_contexts "src/OpenStore.Domain/Catalog/Product.cs"
  elif require_anchor V2 bounded_contexts "$F" "class Product"; then
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
#
# 靶点必须是**确定的一个文件**，不能用 src/OpenStore.Api/Endpoints/Admin/*.cs 这种 glob：
# find_one 取的是 `ls | head -1`，也就是字典序第一个。一旦新增一个 A 开头的 admin 端点文件，
# 这条注入就静默换了对象——可能换到一个本来就没写 perm 的文件上，注入变成空转，
# 而脚本看起来照样 ok。这种"覆盖面被无关改动悄悄挪走"的退化最难发现。
# （M0 实测：新增 AdminAuth 端点目录后，glob 命中了 CategoryAdminEndpoints.cs。）
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V3" ]; then
  F=$(find_one "src/OpenStore.Api/Endpoints/Admin/ProductAdminEndpoints.cs")
  if [ -z "$F" ]; then
    require_target V3 authorization "src/OpenStore.Api/Endpoints/Admin/ProductAdminEndpoints.cs"
  elif require_anchor V3 authorization "$F" "RequireAuthorization\(Permissions\."; then
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
#
# 注入必须按 DTO 的**实际形态**写，不能假设它是带花括号的 class：
# M1 的 StorefrontProductDto 是 positional record（`sealed record X(a, b, c);`），
# 既没有 `class` 关键字、也没有花括号体。原先的锚点 "class StorefrontProductDto"
# 与注入（在最后一个 `}` 前插属性）对它全部失效 —— 而旧版脚本在锚点缺失时只是打印
# 一行就继续跑，于是 V4 从未执行过一次。这条注入的失效只有靠"执行条数会计"才能发现。
#
# 现在的注入：定位 record 头，在它的参数表收尾 `);` 处补一个带属性的 body。
# positional record 的参数本身就是属性，所以补 body 与补参数等价；用 body 更稳，
# 因为它不依赖最后一个参数叫什么名字。
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V4" ]; then
  F=$(find_one "src/OpenStore.Application/**/StorefrontProductDto.cs")
  if [ -z "$F" ]; then
    require_target V4 storefront_dto "src/OpenStore.Application/**/StorefrontProductDto.cs"
  elif require_anchor V4 storefront_dto "$F" "record StorefrontProductDto"; then
    snapshot "$F"
    python3 - "$F" <<'PY'
import sys,re
p=sys.argv[1]
s=open(p,encoding='utf-8').read()
m=re.search(r'record\s+StorefrontProductDto\s*\(', s)
if not m:
    sys.exit(3)
i=s.index(');', m.end())
s=s[:i]+')\n{\n    public long CostAmount { get; init; }\n}'+s[i+2:]
open(p,'w',encoding='utf-8').write(s)
PY
    if [ $? -ne 0 ]; then
      say "  V4 ANCHOR SHAPE MISSING：StorefrontProductDto 的参数表收尾 ');' 没找到，注入未生效。"
      FAIL=$((FAIL+1)); FAILED+=("V4:anchor-shape")
    else
      run_injection V4 storefront_dto "CostAmount leaks admin field" g_arch StorefrontDto
    fi
    restore_files
  fi
fi

# -----------------------------------------------------------------------------
# V5  模板 schema：删掉 defaults.themeId
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V5" ]; then
  F=$(find_one "assets/industry-templates/womenswear/template.json")
  if [ -z "$F" ]; then
    require_target V5 templates "assets/industry-templates/womenswear/template.json"
  elif require_anchor V5 templates "$F" '"themeId"'; then
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
#
# 注入的是"声明"而不是"实现"：往 $defs.blockKey.enum 加一个 countdown，
# 但不给它 $defs.blocks 里的 props schema。这在结构上完全合法 —— 枚举里多一个值
# 不违反任何 JSON Schema，所以拦它的必须是 check-themes 的同集性校验，而不是 schema 层。
#
# 这也是为什么 blocks.schema.json 里 blockKey 只受形态约束、不受枚举约束：
# 一旦有人"顺手把枚举补全到 manifest schema 里"，这条注入会在结构层被 SCHEMA_INVALID
# 拦掉，expect 匹配不到 BLOCK_PROPS_MISSING，V6 就从"验证语义校验"退化成"验证 JSON 形态"。
#
# 期望同时断言错误码与说明片段：只断言错误码的话，将来任何别的主题错误（比如 tokens 少个 default）
# 也会让 V6 变红，看起来照样"通过"，覆盖却已经偏了。
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V6" ]; then
  F=$(find_one "themes/base/blocks.schema.json")
  if [ -z "$F" ]; then
    require_target V6 themes "themes/base/blocks.schema.json"
  elif require_anchor V6 themes "$F" '"\$defs"|blockKey'; then
    snapshot "$F"
    python3 - "$F" <<'PY'
import sys,re
p=sys.argv[1]
s=open(p,encoding='utf-8').read()
# 往第一个 enum 数组里追加 "countdown"，不定义其 props
s2=re.sub(r'("enum"\s*:\s*\[)', r'\1"countdown",', s, count=1)
if s2==s:
    sys.stderr.write("V6 注入失败：blockKey.enum 锚点未命中\n")
    sys.exit(1)
open(p,'w',encoding='utf-8').write(s2)
PY
    run_injection V6 themes "BLOCK_PROPS_MISSING|no props schema" g_tools check-themes
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
  if [ -z "$F" ]; then
    require_target V7 migrations "src/OpenStore.Infrastructure/Persistence/Configurations/ProductConfiguration.cs"
  elif require_anchor V7 migrations "$F" 'ToTable\("Product"\)'; then
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
  if [ -z "$F" ]; then
    require_target V8 domain_tests "src/OpenStore.Domain/Ordering/OrderStatusMachine.cs"
  elif require_anchor V8 domain_tests "$F" "PendingPayment"; then
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
# V12 文档版本号：往正文插一句"依据某版本定稿"
#
# 这条注入守的是 check.sh 步骤 15（scripts/check-doc-version-refs.sh）。
# 它属于"声明层"改动：不改任何逻辑，只往正文加一行引用 —— 正是规则要治的形态。
#
# 为什么必须从这个方向注入：脚本的豁免是**结构化**的（文件头 / 变更记录 / 附录 B /
# 作废台账），所以"正文里加一句版本号"必须红、"追溯区里写版本号"必须不红。
# 只测前者会漏掉后者，只测后者会漏掉前者；本条只覆盖前者，后者由 --list-exempt
# 的输出人工审阅（把豁免区间打出来看，比断言"某行确实被豁免"更难做假）。
#
# 靶点选 docs/02 的一段**确定性正文**（SEO 字段口径那节的开头），锚点是一句
# 完整的中文短句而不是行号 —— 行号会随编辑漂移，短句在可预见的时间内有意义。
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ] || [ "$ONLY" = "V12" ]; then
  F=$(find_one "docs/02-数据模型.md")
  if [ -z "$F" ]; then
    require_target V12 doc_version_refs "docs/02-数据模型.md"
  elif require_anchor V12 doc_version_refs "$F" "核心规则：每档只用一种信号"; then
    snapshot "$F"
    python3 - "$F" <<'PY'
import sys
p=sys.argv[1]
lines=open(p,encoding='utf-8').read().split('\n')
anchor='核心规则：每档只用一种信号'
for i,l in enumerate(lines):
    if anchor in l:
        # 插在锚点行之后，保持"正文中部"而不是文件尾（文件尾可能落在豁免区里）
        lines.insert(i+1, '> （本节结论依据需求 v1.99 定稿并锁定。）')
        break
else:
    sys.exit(3)
open(p,'w',encoding='utf-8').write('\n'.join(lines))
PY
    if [ $? -ne 0 ]; then
      say "  V12 ANCHOR SHAPE MISSING：docs/02 里找不到 SEO 字段口径那节的开头句，注入未生效。"
      FAIL=$((FAIL+1)); FAILED+=("V12:anchor-shape")
    else
      run_injection V12 doc_version_refs "DOC VERSION REF|引用了 v1.99" g_doc
    fi
    restore_files
  fi
fi

# -----------------------------------------------------------------------------
# 收口：**执行条数必须等于声明条数**
#
# 前面每个"静默跳过"都被改成了记账，但没有会计对总量：如果将来有人删掉一整段 if
# （而不是注释掉某个 if 内部语句），或者 ONLY 之外加了新的跳过路径，
# failed 仍然是 0、proven 少一条，脚本照样退出 0。
# 声明清单取自 --list 的同一个正则，所以"列出来的"与"要求执行的"不会各自演化。
# -----------------------------------------------------------------------------
if [ -z "$ONLY" ]; then
  PLANNED=$(grep -oE 'V[0-9]+ (layering|bounded_contexts|authorization|storefront_dto|templates|themes|migrations|domain_tests|doc_version_refs)' "$0" \
            | sort -u | wc -l | tr -d ' ')
  EXECUTED=$((PASS + FAIL))
  if [ "$EXECUTED" -ne "$PLANNED" ]; then
    say ""
    say "== 覆盖不完整：声明 $PLANNED 条注入，实际只执行 $EXECUTED 条 =="
    say "差额是静默跳过 —— 它和「红错地方」一样致命，因为汇总看起来是成功的。"
    FAIL=$((FAIL+1)); FAILED+=("count:$EXECUTED/$PLANNED")
  fi
fi

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
