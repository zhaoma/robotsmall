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
EOF
}

# 从配置目录抽 CK 名
collect_conf() {
  grep -rhoE 'HasCheckConstraint\("[^"]+"' "$1" 2>/dev/null \
    | sed 's/.*("//; s/"$//' | LC_ALL=C sort -u
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
  return 0
}

self_test() {
  local tmp failures=0
  tmp=$(mktemp -d) || { echo "FAIL: mktemp 失败"; return 1; }
  trap 'rm -rf "$tmp"' RETURN

  # 样本 1：配置有、迁移有 → 必须绿
  mkdir -p "$tmp/s1/conf" "$tmp/s1/mig"
  printf 'b.HasCheckConstraint("CK_A_Bit", "\\"Bit\\" IN (0,1)");\n' > "$tmp/s1/conf/XConfiguration.cs"
  printf 'table.CheckConstraint("CK_A_Bit", "\\"Bit\\" IN (0,1)");\n' > "$tmp/s1/mig/M1.cs"
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

  echo
  if [ "$failures" -eq 0 ]; then
    echo "自证通过：5/5"
    return 0
  fi
  echo "自证失败：$failures/5 条样本未达预期"
  return 1
}

case "${1:-}" in
  --list) print_list; exit 0 ;;
  --self-test) self_test; exit $? ;;
  "") check_dirs "$CONF_DIR" "$MIG_DIR"; exit $? ;;
  -h|--help) usage; exit 0 ;;
  *) printf '未知参数：%s\n' "$1" >&2; usage >&2; exit 2 ;;
esac
