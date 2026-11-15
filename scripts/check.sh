#!/usr/bin/env bash
# =============================================================================
# OpenStore 工程门禁（fail-before-pass 的「pass」一半）
#
# 用法：
#   ./scripts/check.sh            # 跑当前里程碑已实现的全部步骤
#   ./scripts/check.sh --full     # 跑全部 15 步；未实现的步骤判红（不允许"未实现=通过"）
#   ./scripts/check.sh --fast     # 只跑 1~6 步（提交前用，目标 < 90 秒）
#   ./scripts/check.sh --list     # 只列步骤，不执行
#
# 退出码：0 全绿 / 1 有步骤失败 / 2 用法错误
#
# 设计约束（改这个脚本前请先读）：
#   1. 每步失败必须打印 `GATE FAILED: <规则名>`，规则名固定且被 CI 与文档引用。
#   2. **未实现的步骤在 --full 下必须判红。** "跳过"是最危险的绿：它让覆盖率
#      看起来完整。宁可红着说"这步还没做"，也不要静默绿。
#   3. 本脚本只做编排，不内联业务断言。断言属于 tests/ 与 tools/，否则断言
#      会分裂成两份（脚本一份、测试一份），然后两份各自演化。
# =============================================================================
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.." || exit 2
ROOT="$PWD"

MODE="default"
case "${1:-}" in
  --full) MODE="full" ;;
  --fast) MODE="fast" ;;
  --list) MODE="list" ;;
  "")     MODE="default" ;;
  *)      echo "unknown arg: $1" >&2; exit 2 ;;
esac

# ---------------------------------------------------------------------------
# 环境自愈：本地 dotnet 安装在用户目录时必须补这两项，否则 dotnet-ef 会报
#   "Failed to resolve libhostfxr.dylib [not found]"
# 这不是"掩盖问题"——这两项在任何一台用用户级安装的机器上都要手工做一次，
# 而漏做的症状是"工具明明装好了但说找不到运行时"，极易被误判成"门禁坏了"。
# CI 里 DOTNET_ROOT 通常已由镜像设置，此处的 fallback 不会覆盖它。
# ---------------------------------------------------------------------------
if [ -z "${DOTNET_ROOT:-}" ] && [ -d "$HOME/.dotnet" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
fi
case ":$PATH:" in
  *":$HOME/.dotnet/tools:"*) ;;
  *) PATH="$HOME/.dotnet/tools:$PATH"; export PATH ;;
esac

LOGDIR="$(mktemp -d)"
trap 'rm -rf "$LOGDIR"' EXIT

PASS=0; FAIL=0; SKIP=0
FAILED_NAMES=()

# ---------------------------------------------------------------------------
# 步骤定义。字段：序号 | 规则名 | 说明 | 命令
# 规则名是 CI / 文档 / verify-gates.sh 的共同锚点，**不要改名**（编号同理不复用）。
# ---------------------------------------------------------------------------
step() {
  local no="$1" name="$2" desc="$3"; shift 3
  printf '  [%2s] %-20s %s\n' "$no" "$name" "$desc"
}

run_step() {
  local no="$1" name="$2" desc="$3"; shift 3
  local log="$LOGDIR/$name.log"
  printf '[%2s] %-20s ' "$no" "$name"
  if "$@" >"$log" 2>&1; then
    printf 'ok\n'
    PASS=$((PASS+1))
  else
    printf 'FAIL\n'
    FAIL=$((FAIL+1)); FAILED_NAMES+=("$name")
    echo "----------------------------------------------------------------"
    sed -e 's/^/    /' "$log" | tail -40
    echo "----------------------------------------------------------------"
    echo "GATE FAILED: $name"
    echo
  fi
}

skip_step() {
  local no="$1" name="$2" desc="$3"
  if [ "$MODE" = "full" ]; then
    printf '[%2s] %-20s FAIL (未实现，--full 下判红)\n' "$no" "$name"
    FAIL=$((FAIL+1)); FAILED_NAMES+=("$name")
    echo "GATE FAILED: $name (not implemented)"
    echo
  else
    printf '[%2s] %-20s SKIP (%s)\n' "$no" "$name" "$desc"
    SKIP=$((SKIP+1))
  fi
}

have() { command -v "$1" >/dev/null 2>&1; }

echo "== OpenStore 门禁 (mode=$MODE) =="
echo

if [ "$MODE" = "list" ]; then
  step  1 build               "dotnet build -warnaserror（零警告）"
  step  2 layering            "Domain 不依赖 EF/ASP.NET；Api 不含 SaveChanges"
  step  3 bounded_contexts    "8x8 上下文命名空间矩阵（白名单）"
  step  4 authorization       "每个 /api/admin 端点都有 perm:* 策略"
  step  5 storefront_dto      "前台 DTO 不含内部字段"
  step  6 domain_tests        "领域单测（无 DB）"
  step  7 integration_tests   "集成测（双 provider）"
  step  8 migrations          "模型与迁移一致"
  step  9 templates           "行业模板 schema + 引用完整性"
  step 10 themes              "主题 manifest + token 单一真源"
  step 11 contract_drift      "OpenAPI 与生成类型一致"
  step 12 e2e_smoke           "E2E 冒烟 + 密钥扫描 + 漏洞"
  step 13 sql_count           "分类页首屏 SQL 条数 <= 8"
  step 14 seo_matrix          "索引控制矩阵 S1~S11"
  step 15 doc_version_refs    "docs 正文不得出现需求基线版本号"
  exit 0
fi

# ---- 1 构建 -----------------------------------------------------------------
run_step 1 build "构建零警告" \
  dotnet build OpenStore.sln -warnaserror --nologo -v q

# 构建失败后续全部无意义，早退比连着报 13 个错更有用
if [ "$FAIL" -gt 0 ]; then
  echo "构建失败，后续步骤跳过（它们会因为缺少产物产生误导性报错）。"
  exit 1
fi

# ---- 2~5 架构门禁（四类，各自独立 Category）--------------------------------
run_step 2 layering "分层依赖" \
  dotnet test tests/OpenStore.ArchitectureTests --no-build --nologo \
  --filter "Category=Layering" --logger "console;verbosity=detailed"

run_step 3 bounded_contexts "上下文互引" \
  dotnet test tests/OpenStore.ArchitectureTests --no-build --nologo \
  --filter "Category=BoundedContexts" --logger "console;verbosity=detailed"

run_step 4 authorization "授权完备性" \
  dotnet test tests/OpenStore.ArchitectureTests --no-build --nologo \
  --filter "Category=Authorization" --logger "console;verbosity=detailed"

run_step 5 storefront_dto "前台数据隔离" \
  dotnet test tests/OpenStore.ArchitectureTests --no-build --nologo \
  --filter "Category=StorefrontDto" --logger "console;verbosity=detailed"

# ---- 6 领域单测 -------------------------------------------------------------
run_step 6 domain_tests "领域单测" \
  dotnet test tests/OpenStore.Domain.Tests --no-build --nologo --logger "console;verbosity=detailed"

[ "$MODE" = "fast" ] && { echo; echo "== --fast 到此为止 =="; goto_summary=1; }

# ---- 7 集成测 ---------------------------------------------------------------
if [ "${goto_summary:-0}" = "1" ]; then
  :
else
  # M0 尚未有集成测项目；--full 下判红，避免"没有测试 = 通过"
  skip_step 7 integration_tests "M1 引入（需 DB/HTTP）"

  # ---- 8 迁移漂移 -----------------------------------------------------------
  if have dotnet-ef; then
    run_step 8 migrations "迁移漂移" \
      dotnet ef migrations has-pending-model-changes \
      -p src/OpenStore.Infrastructure -s src/OpenStore.Infrastructure --no-build
  elif dotnet ef --version >/dev/null 2>&1; then
    run_step 8 migrations "迁移漂移" \
      dotnet ef migrations has-pending-model-changes \
      -p src/OpenStore.Infrastructure -s src/OpenStore.Infrastructure --no-build
  else
    printf '[ 8] %-20s FAIL (dotnet-ef 未安装)\n' migrations
    echo "  → dotnet tool install --global dotnet-ef --version 8.*  并把 ~/.dotnet/tools 加入 PATH"
    echo "GATE FAILED: migrations (tool missing)"
    FAIL=$((FAIL+1)); FAILED_NAMES+=(migrations)
  fi

  # ---- 9~10 模板与主题 ------------------------------------------------------
  run_step 9 templates "行业模板 schema" \
    dotnet run --project tools/OpenStore.Tools --no-build -- check-templates

  run_step 10 themes "主题与 token" \
    dotnet run --project tools/OpenStore.Tools --no-build -- check-themes

  # ---- 11~14 尚未实现 ------------------------------------------------------
  skip_step 11 contract_drift "M2 引入（OpenAPI + pnpm gen:types）"
  skip_step 12 e2e_smoke      "M4 引入（需真实支付通道与前端）"
  skip_step 13 sql_count      "M2 引入（需查询拦截器）"
  skip_step 14 seo_matrix     "M3 引入（scripts/verify-seo-matrix.sh 已存在，待前端落地后接真站）"

  # ---- 15 文档版本号引用 ----------------------------------------------------
  # 放在最后：它是纯文本扫描，零依赖、秒级；失败信息自带行号与上下文，
  # 不需要像其他步骤那样靠日志尾巴定位。
  # 判据在 docs/01 的「结构规则 3」，机器实现在 scripts/check-doc-version-refs.sh。
  run_step 15 doc_version_refs "文档版本号引用" \
    ./scripts/check-doc-version-refs.sh
fi

# ---- 汇总 -------------------------------------------------------------------
echo
echo "== 汇总: passed=$PASS failed=$FAIL skipped=$SKIP =="
if [ "$FAIL" -gt 0 ]; then
  printf '失败步骤: %s\n' "${FAILED_NAMES[*]}"
  exit 1
fi
if [ "$SKIP" -gt 0 ] && [ "$MODE" = "default" ]; then
  echo "注意：有 $SKIP 步未实现（非 --full 模式）。发布前必须用 --full 再跑一次。"
fi
exit 0
