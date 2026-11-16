#!/usr/bin/env bash
# =============================================================================
# CI 门禁包装器：把 `scripts/check.sh` 的结论搬进 GitHub Actions 的 Step Summary
#
# 用法（参数没有默认值，必须显式给；不认识的参数 → rc=3，与 check.sh 同一套词汇表）：
#   ./scripts/ci-gate.sh fast      # = check.sh --fast    （PR 档）
#   ./scripts/ci-gate.sh default   # = check.sh           （main 档）
#   ./scripts/ci-gate.sh full      # = check.sh --full    （全量档：有跳过即 rc=2）
#
# 为什么需要这层包装，而不是在 workflow 里直接 `run: bash scripts/check.sh --fast`：
#
#   1) **默认档在 M4 之前恒为 `VERDICT: PARTIAL(p/n)`，而退出码仍然是 0。**
#      docs/05 §4.6 定的契约：默认模式的跳过是里程碑设计，不是异常，所以它退 0。
#      于是只要 CI 只看退出码，一次 PARTIAL 就会以"绿色对勾"滚过去——读的人以为全绿了。
#      所以 VERDICT 必须以摘要形式留在页面上：**一个只在失败时才显示的信号等于没有信号。**
#
#   2) **退出码判据只写一次，写在一个地方。** 0 通过 / 1 断言失败 / 2 未验证 / 3 用法错误
#      （定义点在 docs/05 §4.6，本文件不复述数字含义，只消费它）。**2 与 3 都是失败**：
#      "没验成"不许当通过。这里没有、也永远不会有 `|| true` / `continue-on-error`。
#
#   3) **这一层必须是脚本，不能是 workflow 里的内联 shell。** 只有脚本能用 `bash -n`
#      与本地实跑验证；写成内联片段，验证方式就退化成"push 上去看结果"——
#      那正是本仓库反复拒绝的"没验过就说做好了"。
#
#   4) **两个"锚"必须出现在绿的路径上**（同 check.sh 步骤 21 的注释）：
#      · `[ *] step_count ok（--list 与实跑一致：N 步）` → 回答"跑全了吗"
#      · `     soak.sh 来源 = HEAD:<sha>`              → 回答"绿的是哪一版"
#      它们只在非 fast 档出现，所以本脚本按档位区分"缺"与"设计上没有"：
#      非 fast 档缺任何一行 → 判 rc=2（读数不可信，不是通过）；fast 档则明确写出
#      "设计上不产出这两行"，不让摘要留白。
#
#   环境变量：
#     GITHUB_STEP_SUMMARY  有值 → 摘要追加写进它（Actions 的作业摘要页）；无值 → 打到 stdout
#     GATE_LOG             日志落盘路径，默认 artifacts/ci-gate-<mode>.log（artifacts/ 已 gitignore）
#
# 纪律：bash 3.2（商家的机器就是 3.2，见 docs/05 §4.6 末）。`${...}` 内部不放中文。
# =============================================================================
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.." || exit 2
ROOT="$PWD"

MODE="${1:-}"
case "$MODE" in
  fast)    CHECK_ARGS="--fast"; TIER="PR 档（--fast：第 1~6 步 + fixture）" ;;
  default) CHECK_ARGS="";       TIER="main 档（默认：--list 声明的全部步骤）" ;;
  full)    CHECK_ARGS="--full"; TIER="全量档（--full：承诺全部，有跳过即 rc=2）" ;;
  "")
    printf '用法：%s {fast|default|full}\n' "$0" >&2
    printf '  档位必须显式给：本脚本不猜"这次该跑哪一档"，猜错了就是一次静默降级。\n' >&2
    exit 3 ;;
  *)
    printf '不认识的档位：%s\n' "$MODE" >&2
    printf '用法：%s {fast|default|full}\n' "$0" >&2
    exit 3 ;;
esac

# ---------------------------------------------------------------------------
# 前置：门禁要用的外部命令。缺了就是 rc=2（未验证），不是 rc=1（产品缺陷）——
# 两者的处置完全不同（补环境 vs 改代码），混在一起会让值班的人查错地方。
# ---------------------------------------------------------------------------
missing=""
for t in bash git dotnet python3 jq; do
  command -v "$t" >/dev/null 2>&1 || missing="$missing $t"
done
if [ -n "$missing" ]; then
  printf 'PRECONDITION: 缺少命令：%s\n' "$missing" >&2
  printf '  → CI 上由 workflow 的 setup 步骤提供（dotnet SDK；python3 与 jq 是 runner 镜像自带）；\n' >&2
  printf '    本地缺 jq 时先装它，不要绕过门禁。\n' >&2
  echo "GATE FAILED: ci_precondition (未验证，不是断言失败)" >&2
  exit 2
fi

LOG="${GATE_LOG:-artifacts/ci-gate-$MODE.log}"
mkdir -p "$(dirname "$LOG")" || { echo "GATE FAILED: ci_gate (无法创建日志目录 $LOG)" >&2; exit 2; }

printf '== ci-gate: bash scripts/check.sh %s（日志 %s）==\n' "${CHECK_ARGS:-(无参数)}" "$LOG"
START_EPOCH=$(date +%s)

# `set -o pipefail` + PIPESTATUS[0]：既要实时看到日志，又要拿到 check.sh 的真实退出码。
# 写成 `check.sh | tee log; rc=$?` 拿到的是 tee 的码（恒 0），这就是最典型的"把没验成当通过"。
# shellcheck disable=SC2086  # CHECK_ARGS 为空是合法的一档（默认档），故意不加引号
bash scripts/check.sh $CHECK_ARGS 2>&1 | tee "$LOG"
RC=${PIPESTATUS[0]}
ELAPSED=$(( $(date +%s) - START_EPOCH ))

# ---------------------------------------------------------------------------
# 从日志里取读数。**只认 check.sh 自己打的原文**，本脚本不重算步数、不重算结论 ——
# 重算就是第二份真相，两份迟早各说各话（docs/05 §4.6 的整节都在讲这件事）。
# ---------------------------------------------------------------------------
VERDICT_LINE=$(grep -E '^VERDICT: ' "$LOG" | tail -1)
SUMMARY_LINE=$(grep -E '^== 汇总: ' "$LOG" | tail -1)
STEP_COUNT_LINE=$(grep -E '^\[ \*\][[:space:]]+step_count' "$LOG" | tail -1)
SOAK_LINE=$(grep -E 'soak\.sh 来源 = ' "$LOG" | tail -1)
SKIP_LINES=$(grep -E 'SKIP \(' "$LOG")
FAILED_LINES=$(grep -E '^GATE FAILED: ' "$LOG")

rc_meaning() {  # 只出「含义」，不带 "N = " 前缀：前缀由调用点那句 "%s = %s" 统一加
  case "$1" in
    0) printf '通过（唯一可放行态）' ;;
    1) printf '有断言/步骤失败 → 产品缺陷，修代码；不得重跑碰运气' ;;
    2) printf '未验证：前置缺失 / 环境未就绪 / --full 下有跳过 → **不是通过**' ;;
    3) printf '用法错误：参数不认识或必填没给 → 修命令行' ;;
    *) printf '不在 docs/05 §4.6 的词表里 → 当作未验证处理' ;;
  esac
}

# 缺失的锚按档位区分"设计上没有"与"该有却没有"。
# ANCHOR_MISSING = 缺了哪些（只用于陈述事实）；ANCHOR_PROBLEM = 1 时才升级成"读数不可信"。
ANCHOR_MISSING=""
anchor_add() { ANCHOR_MISSING="${ANCHOR_MISSING:+${ANCHOR_MISSING}/}$1"; }
if [ "$MODE" = "fast" ]; then
  STEP_COUNT_SHOWN='（--fast 在汇总前就 return，不对账 step_count —— 这是设计，见 check.sh 的 goto_summary）'
  SOAK_SHOWN='（--fast 不跑 soak 自证；压测判定逻辑的自证只在 main 档与全量档跑）'
else
  if [ -z "$STEP_COUNT_LINE" ]; then
    STEP_COUNT_SHOWN='**未产出**'
    anchor_add step_count
  else
    STEP_COUNT_SHOWN="\`${STEP_COUNT_LINE}\`"
  fi
  if [ -z "$SOAK_LINE" ]; then
    SOAK_SHOWN='**未产出**'
    anchor_add soak_source
  else
    SOAK_SHOWN="\`${SOAK_LINE}\`"
  fi
fi
if [ -z "$VERDICT_LINE" ]; then anchor_add verdict; fi

# ---------------------------------------------------------------------------
# 退出码与"有没有结论行"的关系，**三种情况处置不同，不许合并**：
#
#   a) 有 VERDICT —— 照常，rc 就是 check.sh 给的码（含 rc=0 的 PARTIAL）。
#   b) 无 VERDICT 且 rc=0 —— 门禁自称成功却没留下结论。fail-closed：改成 2（未验证）。
#      这条只可能来自 check.sh 的退出路径被改动，而"0 但没结论"正是最该拦的形态。
#   c) 无 VERDICT 且 rc!=0 —— 这是 check.sh 的**设计内早退**：build 失败时它直接 exit 1，
#      不打印 VERDICT / step_count / soak（见 check.sh 第 1 步之后那段"构建失败，后续步骤
#      跳过"）。此时 rc=1 已经是"代码坏了"，**不许改写成 2** —— 1 与 2 绝不能混：
#      混了以后值班的人会照 2 去补环境，而实际要做的是修代码（docs/05 §4.6）。
# ---------------------------------------------------------------------------
ANCHOR_PROBLEM=0
if [ -n "$ANCHOR_MISSING" ] && [ "$RC" = "0" ]; then
  RC=2
  ANCHOR_PROBLEM=1
fi
# 中文绝不进 ${...}（bash 3.2 把高位字节当标识符字符，"$VAR）" 会被解析成另一个变量名，
# 在 `set -u` 下直接 unbound 退出——实测踩过，而且它只在 bash 3.2 的机器上炸，
# 在 CI 的 bash 5 上是静默通过的，属于"本机能跑、客户机器不能跑"那一类）。
VERDICT_SHOWN="${VERDICT_LINE}"
if [ -z "$VERDICT_SHOWN" ]; then VERDICT_SHOWN='（没有 VERDICT 行）'; fi

emit() {  # stdin → 作业摘要（有 $GITHUB_STEP_SUMMARY 时）并同时回显到 stdout
  if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    tee -a "$GITHUB_STEP_SUMMARY"
  else
    cat
  fi
}

{
  echo "### 门禁结论：$MODE 档"
  echo
  echo '| 项 | 值 |'
  echo '|---|---|'
  echo "| 档位 | $TIER |"
  echo "| 事件 / 分支 | \`${GITHUB_EVENT_NAME:-local}\` / \`${GITHUB_REF_NAME:-$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')}\` |"
  echo "| 提交 | \`${GITHUB_SHA:-$(git rev-parse HEAD 2>/dev/null || echo '?')}\` |"
  echo "| 结论 token | \`${VERDICT_SHOWN}\` |"
  if [ -n "$VERDICT_LINE" ]; then
    echo "| 明细 | \`${SUMMARY_LINE:-?}\` |"
  fi
  echo "| 退出码 | ${RC} → $(rc_meaning "$RC") |"
  echo "| 跑全了吗 | $STEP_COUNT_SHOWN |"
  echo "| 绿的是哪一版 | $SOAK_SHOWN |"
  echo "| 耗时 | ${ELAPSED}s |"
  echo "| 完整日志 | \`$LOG\`（Actions 里见同名的 gates-log artifact） |"
  echo

  if [ -n "$SKIP_LINES" ]; then
    echo '按里程碑跳过的步骤（**这些没有被验证**）：'
    echo
    echo '```'
    printf '%s\n' "$SKIP_LINES"
    echo '```'
    echo
  fi

  if [ -n "$FAILED_LINES" ]; then
    echo '失败锚点（规则名固定，可 grep / 可进文档）：'
    echo
    echo '```'
    printf '%s\n' "$FAILED_LINES"
    echo '```'
    echo
  fi

  case "$VERDICT_LINE" in
    *PARTIAL*)
      echo '> **PARTIAL 不是"全绿"。** 上表里被 SKIP 的步骤一步都没验过：'
      echo '> 它们的规则没有执行，只是被判成"按里程碑暂不在范围内"。'
      echo '> 要"全量通过"这个结论，必须 `bash scripts/check.sh --full` 并认 `VERDICT: PASS(n/n)`。'
      echo
      ;;
  esac

  if [ "$RC" -ne 0 ]; then
    echo "> **本次不放行**：${RC} = $(rc_meaning "$RC")。"
    if [ "$RC" = "2" ]; then
      echo '> 注意 `2` 不是"代码坏了"，是"没验成"：把前置补齐再跑，**不要**改成 `|| true` 让它变绿。'
    fi
    echo
  fi

  if [ "$ANCHOR_PROBLEM" = "1" ]; then
    echo "> **锚缺失（${ANCHOR_MISSING}）**：门禁声称通过，却没留下这几行读数。"
    echo '> 已按"未验证"处理（rc 置 2）—— **缺出处的绿等于一个没有单位的数**。'
    echo '> 处置：先看完整日志的退出路径（是不是没跑到汇总），不要先怀疑业务代码。'
    echo
  elif [ -n "$ANCHOR_MISSING" ]; then
    echo "> 本次没跑到汇总，因此没有（${ANCHOR_MISSING}）这几行读数——与退出码一致，"
    echo '> 不是契约漂移：check.sh 在 build 失败等前置崩塌处会早退，那条路径本来就不打结论。'
    echo
  fi
} | emit

printf '== ci-gate 结论：%s（rc=%s，%ss，日志 %s）==\n' "$VERDICT_SHOWN" "$RC" "$ELAPSED" "$LOG"
exit "$RC"
