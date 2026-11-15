#!/usr/bin/env bash
# =============================================================================
# OpenStore AC-12 压测：30 分钟持续加压 + 三进程 RSS 采样 + OOM 证据链 + 自动判定
#
# 用法：
#   ./scripts/soak.sh --tier small  --duration 1800 --conc 8
#   ./scripts/soak.sh --tier standard --duration 1800 --conc 12
#   ./scripts/soak.sh --tier small --cold 300 --cold-restart   # 冷启动后立刻压 5 分钟（AC-12 最容易 OOM 的窗口）
#   ./scripts/soak.sh --tier small --kernel-log skip           # 显式放弃内核日志源（会记 WARN，不是默认）
#   ./scripts/soak.sh --tier small --seed-meta seed.out        # 把 seed-bench 的输出存进去，便于核对两轮是否同一批数据
#   ./scripts/soak.sh --tier small --seed-meta seed.out --prev-meta prev.out   # 跨轮比对指纹，不同则报 INCOMPARABLE
#   ./scripts/soak.sh --self-test          # 只验"判定逻辑能不能红"，不连站、不需要 docker
#   ./scripts/soak.sh --self-test --verbose
#
# 退出码（与 verify-seo-matrix.sh 同一套语义）：
#   0 = 全绿（唯一通过态；WARN 不阻断）
#   1 = 有断言失败
#   2 = 前置条件缺失（未验证）——**等价于不通过**，CI 只接受 0
#   3 = 用法错误
#
# -----------------------------------------------------------------------------
# 为什么"跑了 30 分钟，看着还行"不算证据
# -----------------------------------------------------------------------------
# AC-12 的失败形态有两种，而只有一种会被肉眼看出来：
#   (a) 进程被 OOM Killer 杀掉 → 容器重启 → 用户那几十秒 502。**这个能看见。**
#   (b) 内存爬到接近 limit 但没被杀 → TTFB 抖动、临界的下一次上传就崩。**这个看不见。**
# 所以本脚本不输出"峰值多少 MB"就完事，它对每个指标给出**带预算的判定**，
# 并且对 OOM 判定要三个独立来源（容器态 / cgroup 计数 / 重启次数增量），
# 因为任何一个单独看都能骗人：
#   - `docker inspect .State.OOMKilled` 只反映**最后一次**死亡原因，容器重启后可能已清零；
#   - `.RestartCount` 会被 `docker compose up -d`（重建容器）清零；
#   - cgroup `memory.events` 的 `oom_kill` **一直累加，重启也不清零** —— 这条最硬。
# 这里取"重启次数**增量**"+"oom_kill **增量**"+"OOMKilled 终态"，三者联合判定。
#
# -----------------------------------------------------------------------------
# 从文档草稿搬进代码时修掉的三个真 bug（都会输出"绿色"，所以必须记下来）
# -----------------------------------------------------------------------------
# 1) 原草稿用 `awk -F'[,/ ]+' '{print $2/1024/1024}'` 解析
#    `docker stats` 的 `{{.MemUsage}}`。但 MemUsage 是 `120.5MiB / 900MiB` 这种
#    **带单位的字符串**，不是字节数：$2 是 120.5，再除 1024² 得 0.0001MB。
#    结果"峰值合计 RSS = 0 MB"，永远低于 1900MB 预算 —— **这条断言天生为绿**。
#    本脚本按单位后缀换算（B/KiB/MiB/GiB），并断言"三个进程都采到了值"。
# 2) 原草稿用 `grep -qv ' 200$'` 判"是否出现非 200"。日志行是 `200 0.123`（码+TTFB），
#    `' 200$'` 永远匹配不上，于是 `-v` 选出全部行、rc=0 → **每次都判 FAIL**（反向错）。
#    本脚本用 `awk '$1 !~ /^[23][0-9][0-9]$/'` 数非 2xx/3xx 的行数，空集合才算通过。
# 3) 原草稿把 OOM 检查写成"打印 `docker inspect` 结果"就结束了 —— **只有观测、没有断言**。
#    AC-12 的核心断言恰好就是它。本脚本用 A6/A7 两条断言把它变成会红的东西。
# 另外 `CONC`（并发数）在原草稿里声明了但从未使用，实际只发出 3 个并发 —— 压力远低于预期，
# 那种"过"只是因为没压上去。
#
# -----------------------------------------------------------------------------
# 采样点不足 = 未验证 = 不通过（这条最容易写成 return 0）
# -----------------------------------------------------------------------------
# 压测脚本最常见的假绿：容器没起、URL 写错、curl 全 000，于是"没有非 200"、"没有 OOM"，
# 一路绿到底。所以 A1（采样点数）与 A2（请求数）是**硬门**，缺了走 rc=2，
# 与 `verify-seo-matrix.sh` 缺 fixture 时同一个处置：未验证不是通过。
# =============================================================================
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.." || exit 3
ROOT="$PWD"

TIER="small"
DUR=1800
CONC=8
BASE="https://localhost"
INTERVAL=15
SELF_TEST=0
VERBOSE=0
COLD=300            # 冷启动阶段时长（秒）。0 = 不跑。默认 300 = §2.6.1 要求的"冷启动后立刻压 5 分钟"
COLD_RESTART=0      # 1 = 冷启动阶段前真的 restart 容器（否则假定操作者刚起过站）
KERNEL_MODE=auto    # auto = 采内核日志，采不到就 rc=2；require = 同上但更严；skip = 显式放弃（记 WARN）
SEED_META=""        # seed-bench 的输出（key=value）。给了就把 fingerprint 写进 META：行数相同不代表同一批数据
PREV_META=""        # 上一轮的 seed 输出。给了就跨轮比 fingerprint：不同则两轮结论不可横向比较
ARGS=()

while [ $# -gt 0 ]; do
  case "$1" in
    --tier)      TIER="${2:-}"; shift 2 ;;
    --tier=*)    TIER="${1#--tier=}"; shift ;;
    --duration)  DUR="${2:-}"; shift 2 ;;
    --duration=*) DUR="${1#--duration=}"; shift ;;
    --conc)      CONC="${2:-}"; shift 2 ;;
    --conc=*)    CONC="${1#--conc=}"; shift ;;
    --interval)  INTERVAL="${2:-}"; shift 2 ;;
    --interval=*) INTERVAL="${1#--interval=}"; shift ;;
    --url)       BASE="${2:-}"; shift 2 ;;
    --url=*)     BASE="${1#--url=}"; shift ;;
    --self-test) SELF_TEST=1; shift ;;
    --verbose)   VERBOSE=1; shift ;;
    --cold)      COLD="${2:-}"; shift 2 ;;
    --cold=*)    COLD="${1#--cold=}"; shift ;;
    --cold-restart) COLD_RESTART=1; shift ;;
    --kernel-log)   KERNEL_MODE="${2:-}"; shift 2 ;;
    --kernel-log=*) KERNEL_MODE="${1#--kernel-log=}"; shift ;;
    --seed-meta)    SEED_META="${2:-}"; shift 2 ;;
    --seed-meta=*)  SEED_META="${1#--seed-meta=}"; shift ;;
    --prev-meta)    PREV_META="${2:-}"; shift 2 ;;
    --prev-meta=*)  PREV_META="${1#--prev-meta=}"; shift ;;
    --list)
      printf 'A1 采样点充足（否则 rc=2 未验证）\n'
      printf 'A2 请求量充足（否则 rc=2 未验证）\n'
      printf 'A3 压测期间无非 2xx/3xx\n'
      printf 'A4 三进程 RSS 合计峰值 ≤ 档位预算\n'
      printf 'A5 单进程不得贴近容器限额（WARN，不阻断）\n'
      printf 'A6 OOMKilled=false 且重启次数增量 0\n'
      printf 'A7 cgroup memory.events 的 oom_kill 增量 0\n'
      printf 'A8 TTFB P95 ≤ 上限\n'
      printf '(冷启动阶段 RSS、稳态 RSS 斜率、机器规格与数据量指纹：作为证据输出，不单独设门；见 docs/05 §2.6.1)\n'
      printf '(--list 只列"这台机器上现在能跑的判定项"；"该不该有"属文档，两者不混——否则 EXECUTED=PLANNED 会自己变绿)\n'
      exit 0 ;;
    -h|--help)
      sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'
      exit 0 ;;
    *) printf 'unknown arg: %s\n' "$1" >&2; exit 3 ;;
  esac
done

# -----------------------------------------------------------------------------
# 档位预算。**唯一出处是 docs/05 §2.6.1（2.5）的档位表**：
#   小店档 2C2G / ≤2k SKU   → RSS 合计预算 1900MB（AC-12 上限 2048，留 7% 给页缓存抖动）
#   标准档 2C4G / 10k SKU   → RSS 合计预算 3900MB
# TTFB P95 两档同为 500ms（AC-12 原文，不按档位放宽）。
# 预算写在这里而不是从 COMPOSE 文件推断，是因为"验收口径"与"运行配置"是两件事：
# 有人调大 mem_limit 让它不 OOM，不该同时把验收口径也调宽。
# -----------------------------------------------------------------------------
case "$TIER" in
  small)
    RSS_BUDGET_MB=1900; TTFB_P95_MS=500
    TIER_DESC="小店档 2C2G / ≤2k SKU / ≤10k 变体" ;;
  standard)
    RSS_BUDGET_MB=3900; TTFB_P95_MS=500
    TIER_DESC="标准档 2C4G / 10k SKU / 50k 变体" ;;
  *) printf 'tier 只接受 small|standard（收到 %s）\n' "$TIER" >&2; exit 3 ;;
esac

say()  { printf '%s\n' "$*"; }
sayv() { [ "$VERBOSE" -eq 1 ] && printf '%s\n' "$*"; return 0; }

need() {  # need <cmd> <人话>
  command -v "$1" >/dev/null 2>&1 && return 0
  printf 'PRECONDITION: 缺少命令 %s（%s）→ 未验证，不是通过\n' "$1" "$2" >&2
  exit 2
}

# 带单位的容量字符串 → MB（保留 1 位小数）。
# 不吃 `awk -F'[,/ ]+'` 那种"当成纯数字"的写法，见文件头 bug #1。
to_mb() {
  local raw v u
  raw=$(printf '%s' "$1" | tr -d ' ')
  v=$(printf '%s' "$raw" | sed 's/[A-Za-z]*$//')
  u=$(printf '%s' "$raw" | sed 's/^[0-9.]*//')
  [ -z "$v" ] && { printf '0'; return 0; }
  case "$u" in
    GiB) awk -v x="$v" 'BEGIN{printf "%.1f", x*1024}' ;;
    MiB) awk -v x="$v" 'BEGIN{printf "%.1f", x}' ;;
    KiB) awk -v x="$v" 'BEGIN{printf "%.1f", x/1024}' ;;
    B)   awk -v x="$v" 'BEGIN{printf "%.1f", x/1048576}' ;;
    "")  awk -v x="$v" 'BEGIN{printf "%.1f", x/1048576}' ;;   # 纯字节
    *)   printf '0' ;;
  esac
}

# =============================================================================
# 判定器（唯一一处"算不算通过"）
# 输入不是参数，而是四个文件；这样 --self-test 可以喂合成样本，跑**同一套判定**。
# 如果自证用另一套判定逻辑，那证明的是"另一套逻辑对"，与生产无关。
#   $1 采样 CSV：ts,api_mb,store_mb,caddy_mb
#   $2 请求日志：每行 "<http_code> <time_starttransfer 秒>"
#   $3 容器态：每行 "<name> OOMKilled=<bool> Restarts=<n> ExitCode=<n>"
#   $4 cgroup 事件：每行 "<name> oom_kill=<n>"
#   $5 期望最少采样点 / $6 期望最少请求数
# 输出：逐条 A1..A8 的 ok/WARN/FAIL，随后"== 结论 =="，返回值 0/1/2。
# =============================================================================
FAILED=()
PASS=0; FAIL=0; WARN=0

emit_ok()   { printf '  %-3s %-22s ok    %s\n' "$1" "$2" "${3:-}"; PASS=$((PASS+1)); }
emit_warn() { printf '  %-3s %-22s WARN  %s\n' "$1" "$2" "${3:-}"; WARN=$((WARN+1)); }
emit_fail() { printf '  %-3s %-22s FAIL  %s\n' "$1" "$2" "${3:-}"; FAIL=$((FAIL+1)); FAILED+=("$1"); }
emit_pre()  { printf '  %-3s %-22s NO-DATA  %s\n' "$1" "$2" "${3:-}"; }

judge() {
  local csv="$1" req="$2" insp="$3" events="$4" kern="$5" min_samples="$6" min_reqs="$7"

  # ---- A1 采样点充足（硬门：不足 = 未验证）----
  local nsamp
  nsamp=$(($(wc -l < "$csv" | tr -d ' ') - 1))
  [ "$nsamp" -lt 0 ] && nsamp=0
  if [ "$nsamp" -lt "$min_samples" ]; then
    emit_pre A1 "采样点充足" "只有 ${nsamp} 个采样点（要求 ≥ ${min_samples}）→ 未验证"
    REASON="A1"
    return 2
  fi

  # ---- A2 请求量充足（硬门）----
  local nreq
  nreq=$(grep -c . "$req" 2>/dev/null | tr -d ' ')
  : "${nreq:=0}"
  if [ "$nreq" -lt "$min_reqs" ]; then
    emit_pre A2 "请求量充足" "只有 ${nreq} 次请求（要求 ≥ ${min_reqs}）→ 压测没真的压上去，未验证"
    REASON="A2"
    return 2
  fi

  # ---- A3 无非 2xx/3xx ----
  local bad badsample
  bad=$(awk '$1 !~ /^[23][0-9][0-9]$/ {n++} END{print n+0}' "$req")
  if [ "$bad" -gt 0 ]; then
    badsample=$(awk '$1 !~ /^[23][0-9][0-9]$/ {print; c++} c>=3{exit}' "$req" | tr '\n' ' ')
    emit_fail A3 "无非 2xx/3xx" "${bad} 个非 2xx/3xx（样本：${badsample}）"
  else
    emit_ok A3 "无非 2xx/3xx" "${nreq} 次请求全为 2xx/3xx"
  fi

  # ---- A4 RSS 合计峰值 ≤ 预算 ----
  # 三个进程任一列缺失（采到空值）都要单独报，因为"缺列"会让合计虚低 → 又是一次假绿。
  local missing maxrow max_total
  missing=$(awk -F, 'NR>1 && ($2=="" || $3=="" || $4=="") {n++} END{print n+0}' "$csv")
  maxrow=$(awk -F, 'NR>1 {t=$2+$3+$4; if (t>m) {m=t; r=$0}} END{if (r=="") r="0,0,0,0"; print r}' "$csv")
  max_total=$(awk -F, 'NR>1 {t=$2+$3+$4; if (t>m) m=t} END{printf "%.1f", m+0}' "$csv")
  if [ "$missing" -gt 0 ]; then
    emit_fail A4 "RSS 合计峰值" "${missing} 个采样点三进程值不全（docker stats 没采到 → 合计会虚低）"
  elif awk -v m="$max_total" -v b="$RSS_BUDGET_MB" 'BEGIN{exit !(m>b)}'; then
    emit_fail A4 "RSS 合计峰值" "${max_total} MB > 预算 ${RSS_BUDGET_MB} MB（峰值时刻 ${maxrow}）"
  else
    emit_ok A4 "RSS 合计峰值" "${max_total} MB / 预算 ${RSS_BUDGET_MB} MB（峰值时刻 ${maxrow}）"
  fi

  # ---- A5 单进程贴近限额（WARN，不阻断）----
  # 不阻断的理由：AC-12 的验收口径是"合计 ≤ 2GB 且不被杀"，单进程贴近自己的 limit
  # 只要没被杀就不算违约；但它是"下一次上传/大促就崩"的前兆，必须出现在正文里。
  local warnline
  warnline=$(awk -F, 'NR>1 {if ($2>a) a=$2; if ($3>b) b=$3; if ($4>c) c=$4}
                      END{printf "%.1f %.1f %.1f", a+0, b+0, c+0}' "$csv")
  emit_warn A5 "单进程峰值" "api/storefront/caddy = ${warnline} MB（限额见 compose；贴限额 95% 就该查 §2.6.1(4) 的旋钮）"

  # ---- A6 OOMKilled / 重启增量 ----
  local oomt rest_zero rest_now rest_before
  oomt=$(awk '{for(i=1;i<=NF;i++) if ($i ~ /^OOMKilled=/) {split($i,a,"="); if (a[2]=="true") n++}} END{print n+0}' "$insp")
  if [ "$oomt" -gt 0 ]; then
    emit_fail A6 "OOMKilled / 重启" "${oomt} 个容器 OOMKilled=true"
  else
    # 增量：容器态文件里若带两段（前/后），比较 Restarts；只有一段则要求为 0
    rest_now=$(awk '{for(i=1;i<=NF;i++) if ($i ~ /^Restarts=/) {split($i,a,"="); s+=a[2]}} END{print s+0}' "$insp")
    rest_before="${RESTARTS_BEFORE:-0}"
    if [ "$rest_now" -gt "$rest_before" ]; then
      emit_fail A6 "OOMKilled / 重启" "重启次数增量 $((rest_now - rest_before))（压测期间被重建/被杀过）"
    else
      emit_ok A6 "OOMKilled / 重启" "OOMKilled=false，重启增量 $((rest_now - rest_before))"
    fi
  fi

  # ---- A7 cgroup oom_kill 增量 + 内核日志（第三条证据）----
  # 三源缺一就只能猜：① 容器态（A6）② cgroup 计数（这里）③ 内核 journal（这里）。
  # cgroup 计数"重启也不清零"，但它只在容器有 cgroup 限额时才有意义；
  # 内核日志则记"谁杀的谁"，能抓到"宿主机整体 OOM 把别的进程杀了"这种 cgroup 看不见的情况。
  local kills kernhits
  kills=$(awk '{for(i=1;i<=NF;i++) if ($i ~ /^oom_kill=/) {split($i,a,"="); s+=a[2]}} END{print s+0}' "$events")
  kernhits=$(grep -ciE 'out of memory|killed process' "${kern:-/dev/null}" 2>/dev/null)
  : "${kernhits:=0}"
  if [ "$kills" -gt 0 ] || [ "$kernhits" -gt 0 ]; then
    emit_fail A7 "oom_kill（cgroup+内核）" "cgroup oom_kill 增量 ${kills}；内核日志命中 ${kernhits} 行（三源里任一非零即判失败）"
  else
    emit_ok A7 "oom_kill（cgroup+内核）" "cgroup 增量 0；内核日志 0 条 OOM 记录"
  fi

  # ---- A8 TTFB P95 ----
  local p95
  p95=$(awk '{print $2}' "$req" | grep -E '^[0-9.]+$' | sort -n \
        | awk '{a[NR]=$1} END{if (NR==0) {print ""; exit} i=int(NR*0.95); if (i<1) i=1; printf "%.0f", a[i]*1000}')
  : "${p95:=}"
  if [ -z "$p95" ]; then
    emit_fail A8 "TTFB P95" "请求日志里没有可解析的 time_starttransfer（curl 可能全失败）"
  elif [ "$p95" -gt "$TTFB_P95_MS" ]; then
    emit_fail A8 "TTFB P95" "${p95} ms > 上限 ${TTFB_P95_MS} ms"
  else
    emit_ok A8 "TTFB P95" "${p95} ms / 上限 ${TTFB_P95_MS} ms"
  fi

  [ "$FAIL" -gt 0 ] && return 1
  return 0
}

print_summary() {  # $1 = 判定器返回值
  local rc="$1"
  say ""
  if [ "$rc" -eq 2 ]; then
    say "== 结论: 未验证（${REASON}）—— 未验证 = 不通过 =="
    say "   压测没有真的发生。先修前置条件（容器在跑？URL 对？fixture 数据到位？），别把它记成通过。"
    return
  fi
  say "== 汇总: pass=${PASS} warn=${WARN} fail=${FAIL} =="
  if [ "$FAIL" -gt 0 ]; then
    printf '失败的断言: %s\n' "${FAILED[*]}"
    say "任一条失败都意味着 AC-12 不达标，**不要用'再压一次'来碰运气**："
    say "  先按 §2.6.1(4) 的旋钮顺序调（堆比例 → cache_size → mmap_size → cpus），再压。"
  fi
}

# =============================================================================
# 自证：同一套 judge()，喂合成样本，断言"每种坏情况各自红在它那条断言上"。
# 这是 fail-before-pass：只断言"退出码非零"不够 —— 上一轮 verify-seo-matrix.sh
# 的三个注入全红在同一条断言上，退出码一样是 1，看着 3/3 通过，实际只证明了 1 件事。
# =============================================================================
self_test() {
  need awk "自证需要 awk 生成/解析样本"
  # 注意：临时目录**不能用 local**。local 在函数返回时就出栈，而 trap 是在 EXIT 时
  # 才求值 `$tmp` —— 在 `set -u` 下会直接 unbound variable 把整个 shell 炸掉（本脚本第一版
  # 就是这个错：自证全绿了，但退出时吐一行 unbound 并把退出码改成 1，看起来像"自证失败"）。
  TMPD=$(mktemp -d)
  trap 'rm -rf "$TMPD"' EXIT

  gen_csv() {  # $1 文件 $2 api $3 store $4 caddy $5 行数
    local f="$1" a="$2" s="$3" c="$4" n="${5:-20}" i=1
    printf 'ts,api_mb,store_mb,caddy_mb\n' > "$f"
    while [ "$i" -le "$n" ]; do
      printf '%d,%s,%s,%s\n' "$((1700000000 + i*15))" "$a" "$s" "$c" >> "$f"
      i=$((i+1))
    done
  }
  gen_req() {  # $1 文件 $2 次数 $3 状态码 $4 ttfb
    local f="$1" n="$2" code="${3:-200}" t="${4:-0.12}" i=1
    : > "$f"
    while [ "$i" -le "$n" ]; do
      printf '%s %s\n' "$code" "$t" >> "$f"
      i=$((i+1))
    done
  }
  gen_insp() {  # $1 文件 $2 OOMKilled $3 Restarts
    printf 'openstore-api-1 OOMKilled=%s Restarts=%s ExitCode=0\n' "$2" "$3" > "$1"
    printf 'openstore-storefront-1 OOMKilled=false Restarts=%s ExitCode=0\n' "$3" >> "$1"
    printf 'openstore-caddy-1 OOMKilled=false Restarts=%s ExitCode=0\n' "$3" >> "$1"
  }

  local failn=0 total=0
  run_case() {  # $1 名称 $2 期望（- 表示应绿 / A1..A8 / rc2）$3..$6 四个文件 $7 内核日志（可缺省）
    local name="$1" want="$2" csv="$3" req="$4" insp="$5" ev="$6" kern="${7:-/dev/null}"
    total=$((total+1))
    : > "$TMPD/out"
    ( judge "$csv" "$req" "$insp" "$ev" "$kern" 8 100 ) > "$TMPD/out" 2>&1
    local rc=$?
    local got=""
    if [ "$rc" -eq 2 ]; then
      got="rc2:$(grep -oE 'A[0-9]' "$TMPD/out" | head -1)"
    elif grep -qE '^  A[0-9] .*FAIL' "$TMPD/out"; then
      got=$(awk '/^  A[0-9] .*FAIL/{print $1; exit}' "$TMPD/out")
    else
      got="-"
    fi
    if [ "$got" = "$want" ]; then
      printf '  ok    %s → 期望=%s 实得=%s（rc=%d）\n' "$name" "$want" "$got" "$rc"
    else
      printf '  WRONG %s → 期望=%s 实得=%s（rc=%d）\n' "$name" "$want" "$got" "$rc"
      sed -e 's/^/        /' "$TMPD/out" | head -12
      failn=$((failn+1))
    fi
  }

  say "== soak.sh 判定逻辑自证（合成样本，不连站、不需要 docker）"

  # 1) 正确样本：全绿
  gen_csv  "$TMPD/c1" 700 300 70; gen_req "$TMPD/r1" 200 200 0.12
  gen_insp "$TMPD/i1" false 0;    printf 'openstore-api-1 oom_kill=0\nopenstore-storefront-1 oom_kill=0\nopenstore-caddy-1 oom_kill=0\n' > "$TMPD/e1"
  run_case "正确样本（RSS 1070/1900，TTFB 120ms，无 OOM）" "-" "$TMPD/c1" "$TMPD/r1" "$TMPD/i1" "$TMPD/e1"

  # 2) 采样点不足 → rc=2（未验证，不是通过）
  gen_csv "$TMPD/c2" 700 300 70 3
  run_case "只有 3 个采样点（压测没跑起来）" "rc2:A1" "$TMPD/c2" "$TMPD/r1" "$TMPD/i1" "$TMPD/e1"

  # 3) 请求量不足 → rc=2
  gen_req "$TMPD/r3" 5 200 0.12
  run_case "只有 5 次请求（压力没真的加上去）" "rc2:A2" "$TMPD/c1" "$TMPD/r3" "$TMPD/i1" "$TMPD/e1"

  # 4) 出现 500 → A3
  gen_req "$TMPD/r4" 200 200 0.12; printf '500 0.08\n' >> "$TMPD/r4"
  run_case "压测期间出现 1 个 500" "A3" "$TMPD/c1" "$TMPD/r4" "$TMPD/i1" "$TMPD/e1"

  # 5) RSS 超预算 → A4
  gen_csv "$TMPD/c5" 1500 380 80
  run_case "RSS 合计 1960MB > 1900 预算" "A4" "$TMPD/c5" "$TMPD/r1" "$TMPD/i1" "$TMPD/e1"

  # 6) 某个采样点缺列 → A4（"缺列会让合计虚低"这条假绿的守门）
  gen_csv "$TMPD/c6" 700 300 70; awk -F, 'BEGIN{OFS=","} NR==5{$3=""} {print}' "$TMPD/c6" > "$TMPD/c6b"
  run_case "docker stats 漏采 storefront（合计虚低）" "A4" "$TMPD/c6b" "$TMPD/r1" "$TMPD/i1" "$TMPD/e1"

  # 7) OOMKilled=true → A6
  gen_insp "$TMPD/i7" true 0
  run_case "容器 OOMKilled=true" "A6" "$TMPD/c1" "$TMPD/r1" "$TMPD/i7" "$TMPD/e1"

  # 8) 重启增量 > 0 → A6
  gen_insp "$TMPD/i8" false 2
  run_case "压测期间容器重启 2 次（进程莫名重启）" "A6" "$TMPD/c1" "$TMPD/r1" "$TMPD/i8" "$TMPD/e1"

  # 9) cgroup oom_kill 增量 → A7（重启也不清零的那条证据）
  printf 'openstore-api-1 oom_kill=1\nopenstore-storefront-1 oom_kill=0\nopenstore-caddy-1 oom_kill=0\n' > "$TMPD/e9"
  run_case "cgroup 记录 oom_kill=1（曾被杀过）" "A7" "$TMPD/c1" "$TMPD/r1" "$TMPD/i1" "$TMPD/e9"

  # 10) TTFB P95 超 → A8
  gen_req "$TMPD/r10" 200 200 0.72
  run_case "TTFB 全为 720ms > 500 上限" "A8" "$TMPD/c1" "$TMPD/r10" "$TMPD/i1" "$TMPD/e1"

  # 11) 内核日志命中 OOM（cgroup 看不见的那种）
  printf 'kern: [12345.678] Out of memory: Killed process 4242 (dotnet) total-vm:1234567kB\n' > "$TMPD/k11"
  run_case "内核日志记录 OOM（cgroup 计数仍为 0）" "A7" "$TMPD/c1" "$TMPD/r1" "$TMPD/i1" "$TMPD/e1" "$TMPD/k11"

  # ---- seed-meta 解析：键名对不上必须红，而且必须**在加压前**红 ----
  # 这一组的来历：本脚本第一版按 `per_category` 取值，造数器打的是
  # `spec_products_per_category`，于是 META 里那行恒为空 —— 一个"总是成功、从不生效"的守卫。
  # 判据不是"能解析出值"，而是"**键名写错时解析会失败**"，所以下面特地把键改坏再验一次。
  mt() {  # $1 名称 $2 期望 $3 实得
    total=$((total+1))
    if [ "$2" = "$3" ]; then
      printf '  ok    %s → %s\n' "$1" "$3"
    else
      printf '  WRONG %s → 期望=%s 实得=%s\n' "$1" "$2" "$3"
      failn=$((failn+1))
    fi
  }
  probe_meta() {  # $1 文件 → 打印 "rc|products|variants|ppc|fingerprint"
    ( SEED_META="$1"; load_seed_meta >/dev/null 2>&1; rc=$?
      printf '%s|%s|%s|%s|%s' "$rc" "$SM_PRODUCTS" "$SM_VARIANTS" "$SM_PPC" "$SM_FP" )
  }
  # 这份样本严格照 SeedBenchCommand.PrintReport 的实际形状写（键名逐字、含中文与全角括号）。
  # $4 = verdict 值，"-" 表示**故意不打这一行**（模拟旧版工具/只抄了一半）；
  # $5/$6 = 两个质量计数（默认 0，健康的形状）。
  mk_seed() {  # $1 文件 $2 ppc 键名 $3 是否带 fp(1/0) $4 verdict("-"=不打) $5 变体数错的数 $6 重复轴签名数
    local f="$1" ppck="$2" hasfp="$3"
    local vd="${4:-OK 造满了（行数 = 规格，每分类 = 规格）}"   # 空串也回落到默认：生成器不生产"半截样本"
    local bv="${5:-0}" ba="${6:-0}"
    {
      printf '== seed-bench：小店档 ≤2,000 SKU ==\n'
      printf 'db=/tmp/a=b.db\ntier=small\nseed_version=1\nprotected_databases=2\n\n'
      printf '== 规格与数据量指纹（可直接贴进验收记录） ==\n'
      printf 'seed_version=1\ntier=small\nspec_products=2000\nspec_variants=10000\n'
      printf 'spec_categories=12\n%s=166\n' "$ppck"
      printf 'products=2000\nvariants=10000\nstatus_active=1900\nstatus_draft=100\n'
      printf 'inserted_products=2000\ninserted_variants=10000\nskipped_products=0\n'
      printf 'products_with_wrong_variant_count=%s\nduplicate_axis_signatures=%s\n' "$bv" "$ba"
      printf 'category_check_scope=slug-segment（M0 无 ProductCategory 表；只证明列表数据量，不证明分类 join 成本）\n'
      [ "$vd" = "-" ] || printf 'verdict=%s\n' "$vd"
      [ "$hasfp" = "1" ] && printf 'fingerprint=0123456789abcdef\n'
      printf 'table_Product_rows=2000\ntable_Product_sha256=deadbeef\n'
    } > "$f"
  }
  probe_verdict() {  # $1 文件 → "rc|verdict|wrong_variant|dup_axis"
    ( SEED_META="$1"; load_seed_meta >/dev/null 2>&1; rc=$?
      printf '%s|%s|%s|%s' "$rc" "$SM_VERDICT" "$SM_BADVAR" "$SM_BADAXIS" )
  }

  mk_seed "$TMPD/m-good" spec_products_per_category 1
  mt "seed-meta 完整块：五个键都取到值" "0|2000|10000|166|0123456789abcdef" "$(probe_meta "$TMPD/m-good")"
  mt "seed-meta 值里含 '=' 不被截断（db=/tmp/a=b.db）" "/tmp/a=b.db" "$(seed_get "$TMPD/m-good" db)"
  mt "seed-meta 里 seed_version 出现两次不误判（取第一个）" "1" "$(seed_get "$TMPD/m-good" seed_version)"

  mk_seed "$TMPD/m-badkey" per_category 1
  mt "seed-meta 键名写错（per_category）→ 判 rc=2，不是记个空值" "2|2000|10000||0123456789abcdef" "$(probe_meta "$TMPD/m-badkey")"

  mk_seed "$TMPD/m-nofp" spec_products_per_category 0
  mt "seed-meta 缺 fingerprint → 判 rc=2（绑定不了数据集）" "2|2000|10000|166|" "$(probe_meta "$TMPD/m-nofp")"

  ( SEED_META="$TMPD/不存在的文件"; load_seed_meta >/dev/null 2>&1 ) ; mt "seed-meta 文件不存在 → rc=2" "2" "$?"
  ( SEED_META=""; load_seed_meta >/dev/null 2>&1 ) ; mt "seed-meta 未给 → rc=0（不算错，META 里写明原因）" "0" "$?"

  # ---- verdict：造数器自己已经判过了，不要替它再判一遍 ----
  # 这一组的存在理由：**行数够不代表数据能用**。造数器会打印
  # `verdict=OK 造满了（…）` / `verdict=FAILED 造不满：行数 != 规格…`，它是唯一知道
  # "每分类有没有凑够"的组件。我们只看 fingerprint+products 就压，等于把它那句结论扔了。
  mk_seed "$TMPD/m-vd-ok"     spec_products_per_category 1
  mt "verdict=OK → 放行" "0|OK 造满了（行数 = 规格，每分类 = 规格）|0|0" "$(probe_verdict "$TMPD/m-vd-ok")"

  mk_seed "$TMPD/m-vd-failed" spec_products_per_category 1 "FAILED 造不满：行数 != 规格，这份数据不能用来出压测报告"
  mt "verdict=FAILED → rc=2（造数器自己说了不能用，别再压）" \
     "2|FAILED 造不满：行数 != 规格，这份数据不能用来出压测报告|0|0" "$(probe_verdict "$TMPD/m-vd-failed")"

  mk_seed "$TMPD/m-vd-none"   spec_products_per_category 1 "-"
  mt "没有 verdict 行（旧版工具/只抄一半）→ rc=2" "2||0|0" "$(probe_verdict "$TMPD/m-vd-none")"

  # ---- 质量计数：总数对、分布坏 ----
  mk_seed "$TMPD/m-badvar"  spec_products_per_category 1 "" 7 0
  mt "products_with_wrong_variant_count=7 → rc=2（变体数不对，join 成本全变）" "2|OK 造满了（行数 = 规格，每分类 = 规格）|7|0" "$(probe_verdict "$TMPD/m-badvar")"

  mk_seed "$TMPD/m-badaxis" spec_products_per_category 1 "" 0 3
  mt "duplicate_axis_signatures=3 → rc=2（唯一索引没生效，库本身有问题）" "2|OK 造满了（行数 = 规格，每分类 = 规格）|0|3" "$(probe_verdict "$TMPD/m-badaxis")"

  # ---- 「每分类 500」的成立范围：原样抄，不许改写 ----
  # 这条只证明列表数据量。META 里若把这句话压成 "每分类 500 ✅"，下游会读成
  # "分类页指标已达标" —— 而分类页的 join 成本在 M0 根本没测到（没有 ProductCategory 表）。
  mt "category_check_scope 原样取到（含中文/全角括号，不截断）" \
     "slug-segment（M0 无 ProductCategory 表；只证明列表数据量，不证明分类 join 成本）" \
     "$(seed_get "$TMPD/m-vd-ok" category_check_scope)"

  # ---- 跨轮可比性：两轮指纹不同必须**说出来**，不能让人自己比 ----
  # 这组防的是"两个 META 各自都完整、指纹却不同"：报告上没有任何异常，
  # 而 RSS 斜率一涨就会被读成泄漏，真实原因是中间换过数据集。
  mk_seed "$TMPD/m-prev-same" spec_products_per_category 1     # fingerprint=0123456789abcdef
  sed 's/^fingerprint=0123456789abcdef$/fingerprint=fedcba9876543210/' \
    "$TMPD/m-prev-same" > "$TMPD/m-prev-diff"
  mt "跨轮指纹相同 → 可比" "yes" "$(prev_comparable 0123456789abcdef "$TMPD/m-prev-same")"
  mt "跨轮指纹不同 → 不可比（不是\"看着差不多就行\"）" "no" "$(prev_comparable 0123456789abcdef "$TMPD/m-prev-diff")"
  mt "未给 --prev-meta → 未判定（不冒充\"可比\"）" "?" "$(prev_comparable 0123456789abcdef "")"
  mt "上一轮文件缺 fingerprint → 未判定" "?" "$(prev_comparable 0123456789abcdef "$TMPD/m-nofp")"
  mt "上一轮文件不存在 → 未判定" "?" "$(prev_comparable 0123456789abcdef "$TMPD/not-exist")"
  mt "本轮没有 fingerprint（没给 --seed-meta）→ 未判定" "?" "$(prev_comparable "" "$TMPD/m-prev-same")"

  # 光算出 no 不够：**这句话必须真的会说出口**。所以断言文案本身，
  # 而不是只断言状态码——"判成了 no 但没说"与"判成了 no 并说清楚"对读报告的人是两回事。
  has() { case "$1" in *"$2"*) printf 'yes' ;; *) printf 'no' ;; esac; }
  LINE_NO="$(comparable_verdict_line no)"; LINE_YES="$(comparable_verdict_line yes)"
  mt "不可比文案含 INCOMPARABLE" "yes" "$(has "$LINE_NO" INCOMPARABLE)"
  mt "不可比文案说清「不可横向比较」（只写 INCOMPARABLE 等于让人去查字典）" "yes" "$(has "$LINE_NO" 不可横向比较)"
  mt "可比文案不出现 INCOMPARABLE（否则每次都喊狼来了）" "no" "$(has "$LINE_YES" INCOMPARABLE)"

  say ""
  if [ "$failn" -eq 0 ]; then
    say "== 自证通过：${total} 个样本，正确样本绿、每种坏情况各自红在预期断言上 =="
    return 0
  fi
  say "== 自证失败：${failn}/${total} 个样本没有红在预期断言上 =="
  say "  判定器本身不可信 —— 此时它给出的 PASS 不能作为 AC-12 的证据。"
  return 1
}

# =============================================================================
# seed-bench 结论块的解析（`--seed-meta`）
# -----------------------------------------------------------------------------
# 为什么要有这一层：**行数相同不代表同一批数据**。两次都是 products=2000 的两个库，
# 内容可以完全不同；此时两轮压测结论不可横向比较，而人只会去怀疑代码退化了。
# 所以结论必须和 fingerprint 绑定。
#
# 为什么不是一行 awk 就完事（第一版就是那样，然后错了）：
#   1) 键名必须与 SeedBenchCommand.PrintReport 实际打印的**逐字一致**。第一版这里写
#      `per_category`，而造数器打的是 `spec_products_per_category` —— 于是 META 里
#      那一行永远是空的：看着"记录了分类密度"，其实什么都没记，而且**不会报错**。
#   2) 取不到值时不能打印空字符串了事。给了 `--seed-meta` 却说不出是什么数据，
#      属于"未验证"，要在**加压前**判 rc=2，而不是压完 30 分钟才发现记录是空的。
#   3) 值里可能含 `=`（`db=/tmp/a=b.db`），所以按**第一个** `=` 切，不能用 `$2` 了事。
# =============================================================================
SM_VERSION=""; SM_PRODUCTS=""; SM_VARIANTS=""; SM_PPC=""; SM_FP=""
SM_VERDICT=""; SM_SCOPE=""; SM_BADVAR=""; SM_BADAXIS=""

seed_get() {  # $1 文件 $2 键 → stdout 值；取不到返回 1（不输出空串冒充成功）
  local v
  v=$(awk -F= -v key="$2" '$1==key{ sub(/^[^=]*=/, ""); print; exit }' "$1" 2>/dev/null | tr -d '\r')
  [ -n "$v" ] || return 1
  printf '%s' "$v"
}

# =============================================================================
# 跨轮可比性（`--prev-meta`）
# -----------------------------------------------------------------------------
# 为什么"--seed-meta 绑定了本轮指纹"还不够：它只解决"这一轮压的是什么"，
# 不解决"这一轮能不能和上一轮比"。两轮各自都记录得很完整、指纹却不同的情况，
# 报告上看不出任何异常——两个 META 各自都是绿的，RSS 斜率一涨就会被读成"泄漏"，
# 而真实原因是中间换过数据集（重跑过 seed-bench、换过 tier、库被重建）。
#
# 所以必须在**看数字之前**把这句话说出来。三种答案，不许合并：
#   yes 两轮指纹相同 → 可横向比较
#   no  两轮指纹不同 → **不可横向比较**，先查数据集
#   ?   未给 --prev-meta / 该文件缺 fingerprint → 未判定（不冒充"可比"）
#
# 为什么 no 不判非 0：它不否定本轮压测（本轮数据没问题），它否定的是"两轮之差"。
# 用退出码挡掉本轮会让人去关掉这条判据；用一行显眼的 INCOMPARABLE 才不会。
# =============================================================================
prev_comparable() {  # $1 本轮 fingerprint $2 上一轮的 seed 输出 → yes|no|?
  local cur="${1:-}" prevfile="${2:-}" prev
  [ -n "$cur" ] || { printf '?'; return 0; }
  [ -n "$prevfile" ] && [ -f "$prevfile" ] || { printf '?'; return 0; }
  prev=$(seed_get "$prevfile" fingerprint 2>/dev/null) || { printf '?'; return 0; }
  [ -n "$prev" ] || { printf '?'; return 0; }
  if [ "$prev" = "$cur" ]; then printf 'yes'; else printf 'no'; fi
}

# 结论行。测试要断言"这句话真的会说出口"，所以文案单独成一个纯函数，
# 而不是散在 case 分支里的 say —— 散着写就没法在自证里断言。
comparable_verdict_line() {  # $1 yes|no|?
  case "${1:-}" in
    yes) printf '跨轮可比性：可比（两轮 fingerprint 相同）' ;;
    no)  printf 'INCOMPARABLE：两轮压的不是同一批数据，RSS 斜率与延迟结论不可横向比较（先查数据集换没换，再怀疑代码退化）' ;;
    *)   printf '跨轮可比性：未判定（未给 --prev-meta 或该文件缺 fingerprint）——不冒充"可比"' ;;
  esac
}

# 把 seed-bench 的结论块读进 SM_*；0 = 可用或未给，2 = 给了但不可用（未验证）
load_seed_meta() {
  SM_VERSION=""; SM_PRODUCTS=""; SM_VARIANTS=""; SM_PPC=""; SM_FP=""
  SM_VERDICT=""; SM_SCOPE=""; SM_BADVAR=""; SM_BADAXIS=""
  [ -n "$SEED_META" ] || return 0          # 没给不算错：META 里会写明"未给，两轮不可横向比较"

  if [ ! -f "$SEED_META" ]; then
    printf 'PRECONDITION: --seed-meta 指向的文件不存在：%s → 未验证，不是通过\n' "$SEED_META" >&2
    printf '  造数时直接把 stdout 存下来：dotnet run --project tools/OpenStore.Tools -- seed-bench --tier %s --db <sqlite> > %s\n' "$TIER" "$SEED_META" >&2
    return 2
  fi

  SM_VERSION=$(seed_get  "$SEED_META" seed_version)              || SM_VERSION=""
  SM_PRODUCTS=$(seed_get "$SEED_META" products)                  || SM_PRODUCTS=""
  SM_VARIANTS=$(seed_get "$SEED_META" variants)                  || SM_VARIANTS=""
  SM_PPC=$(seed_get     "$SEED_META" spec_products_per_category) || SM_PPC=""
  SM_FP=$(seed_get      "$SEED_META" fingerprint)                || SM_FP=""
  # verdict 是**造数器自己的结论**（`verdict=OK 造满了（…）` / `verdict=FAILED 造不满…`）。
  # 不看它就等于我们替它重新判断一遍，而它已经判断过了 —— 还比我们多知道一件事：
  # 行数可能凑够而每分类没凑够。所以它是第六个硬键。
  SM_VERDICT=$(seed_get "$SEED_META" verdict)                    || SM_VERDICT=""
  # 两个质量计数：总数对、分布坏的形态（每商品变体数不对 / 轴签名重复）。非零 = 这份数据
  # 本身不合格，压出来的数字再漂亮也不代表 AC-12。**只在这两键存在时判**——
  # 旧版工具不打印它们，缺键不该当成"数据坏了"。
  SM_BADVAR=$(seed_get  "$SEED_META" products_with_wrong_variant_count) || SM_BADVAR=""
  SM_BADAXIS=$(seed_get "$SEED_META" duplicate_axis_signatures)         || SM_BADAXIS=""
  # 「每分类 500」的成立范围。原样抄进 META：否则下游会把"每分类 500 ✅"读成
  # "分类页指标已达标"，而它只证明列表数据量（M0 没有 ProductCategory 表，测不到 join 成本）。
  SM_SCOPE=$(seed_get   "$SEED_META" category_check_scope)              || SM_SCOPE=""

  # 五个键**一个都不许缺**。
  # 起初我只硬要求 fingerprint 与 products（"两根腿"），把其余三个记成 `?` —— 自证当场
  # 把这条判成 WRONG：键名写错时 products 照样在，于是 rc=0、META 里留一个空值，
  # 正是我要修的那个"看着记录了、其实没记"。**判据不严到能抓住自己写错的键名，
  # 就等于没有判据**。缺哪个都说明这个文件不是当前 seed-bench 的完整结论块
  # （贴串／截断／工具是旧版本），三种情况都不该拿它当绑定依据。
  local miss=""
  [ -n "$SM_VERSION" ]  || miss="$miss seed_version"
  [ -n "$SM_PRODUCTS" ] || miss="$miss products"
  [ -n "$SM_VARIANTS" ] || miss="$miss variants"
  [ -n "$SM_PPC" ]      || miss="$miss spec_products_per_category"
  [ -n "$SM_FP" ]       || miss="$miss fingerprint"
  [ -n "$SM_VERDICT" ]  || miss="$miss verdict"
  if [ -n "$miss" ]; then
    printf 'PRECONDITION: %s 里缺%s → 这不是 seed-bench 的完整结论块 → 未验证，不是通过\n' "$SEED_META" "$miss" >&2
    printf '  该文件应是 seed-bench 的完整 stdout（含 "== 规格与数据量指纹（可直接贴进验收记录） ==" 一节），五行缺一不可\n' >&2
    printf '  常见原因：stdout 只重定向了一半／手工只抄了 fp／造数用的是旧版 OpenStore.Tools（先 dotnet build tools/OpenStore.Tools -warnaserror）\n' >&2
    return 2
  fi

  # 造数器已经给出了结论，照它的结论走 —— 不要"我看到行数够了所以应该没事"。
  case "$SM_VERDICT" in
    OK*) : ;;
    *) printf 'PRECONDITION: seed-bench 自己判了这份数据不合格 → 未验证，不是通过\n' >&2
       printf '  %s 里：verdict=%s\n' "$SEED_META" "$SM_VERDICT" >&2
       printf '  行数/分布没造满的数据压出来的 TTFB 会偏乐观，拿它出 AC-12 报告等于自证清白。先修造数再看性能。\n' >&2
       return 2 ;;
  esac

  # 两个质量计数非零 = 总数对但分布坏。同样不压。
  #
  # 这里刻意**不拼字符串**：第一版写的是 `bad="$bad；…"` 再用 `${bad#；}` 去掉前导分号，
  # 在 bash 3.2 上直接炸 —— `badï: unbound variable`（`set -u` 下整个子壳退出，
  # 输出全空，看起来像"探针什么都没返回"）。原因是参数展开里出现多字节字符时，
  # bash 3.2 会把它当成变量名的一部分来切词。**商家的机器就是 3.2**，所以：
  # 参数展开里只放 ASCII，中文一律放进 printf 的格式串。
  local bad1="" bad2=""
  [ -n "$SM_BADVAR" ]  && [ "$SM_BADVAR" != "0" ]  && bad1="yes"
  [ -n "$SM_BADAXIS" ] && [ "$SM_BADAXIS" != "0" ] && bad2="yes"
  if [ -n "$bad1$bad2" ]; then
    printf 'PRECONDITION: %s 的数据质量计数非零 → 未验证，不是通过\n' "$SEED_META" >&2
    [ -z "$bad1" ] || printf '  products_with_wrong_variant_count=%s（有商品的变体数不等于规格，索引与 join 的成本都变了）\n' "$SM_BADVAR" >&2
    [ -z "$bad2" ] || printf '  duplicate_axis_signatures=%s（(ProductId,AxisSignature) 唯一索引没生效，这份库本身有问题）\n' "$SM_BADAXIS" >&2
    return 2
  fi
  return 0
}

# =============================================================================
# 真实压测
# =============================================================================
main() {
  need curl "压测用 curl 并行循环"
  need docker "要读容器 RSS 与 OOM 证据"
  docker info >/dev/null 2>&1 || {
    printf 'PRECONDITION: docker daemon 不可用 → 未验证，不是通过\n' >&2
    exit 2
  }

  local SVC cid_api cid_store cid_caddy
  cid_api=$(docker compose ps -q api 2>/dev/null | head -1)
  cid_store=$(docker compose ps -q storefront 2>/dev/null | head -1)
  cid_caddy=$(docker compose ps -q caddy 2>/dev/null | head -1)
  local cid
  for SVC in api storefront caddy; do
    case "$SVC" in
      api)        cid="$cid_api" ;;
      storefront) cid="$cid_store" ;;
      caddy)      cid="$cid_caddy" ;;
    esac
    [ -n "$cid" ] || {
      printf 'PRECONDITION: 服务 %s 没在跑（docker compose ps -q %s 为空）→ 未验证\n' "$SVC" "$SVC" >&2
      exit 2
    }
  done

  local TS OUT REQLOG INSP EVENTS KERN META
  TS=$(date -u +%Y%m%dT%H%M%SZ)
  OUT="$ROOT/artifacts/soak-${TS}.csv"
  REQLOG="$ROOT/artifacts/soak-${TS}-req.log"
  INSP="$ROOT/artifacts/soak-${TS}-inspect.txt"
  EVENTS="$ROOT/artifacts/soak-${TS}-events.txt"
  KERN="$ROOT/artifacts/soak-${TS}-kernel.log"
  META="$ROOT/artifacts/soak-${TS}-meta.txt"
  mkdir -p "$ROOT/artifacts"
  # phase 列区分 cold / steady：冷启动阶段的峰值往往才是最高峰，混在一起就看不出来了
  printf 'ts,api_mb,store_mb,caddy_mb,phase\n' > "$OUT"
  : > "$REQLOG"

  local min_samples min_reqs
  min_samples=$(( DUR / INTERVAL / 2 )); [ "$min_samples" -lt 6 ] && min_samples=6
  min_reqs=$(( CONC * DUR / 10 )); [ "$min_reqs" -lt 100 ] && min_reqs=100

  say "== AC-12 压测：${TIER_DESC}"
  say "   目标 ${BASE}  时长 ${DUR}s  并发 ${CONC}  采样间隔 ${INTERVAL}s"
  say "   预算：RSS 合计 ≤ ${RSS_BUDGET_MB} MB；TTFB P95 ≤ ${TTFB_P95_MS} ms；三进程 OOM 证据必须干净"
  say "   原始数据：${OUT}    请求明细：${REQLOG}"

  # ---- 数据集绑定：先解析 seed-bench 的结论块，解析不出来就**在加压前**退出 ----
  # 顺序是有讲究的：这一步必须在 30 分钟压测**之前**做完。压完才发现"记了个空的
  # fingerprint"，等于把半小时加一次真机窗口一起扔掉，而人只会看到一行空格子。
  load_seed_meta || exit 2

  # 跨轮可比性：解析出本轮指纹之后立刻算，因为它的结论要**和本轮数字一起**出现在屏幕上。
  # 放到压完之后再算，等于让人先看完 30 分钟的曲线，才被告知"这两轮不能比"。
  local COMPARABLE
  COMPARABLE=$(prev_comparable "$SM_FP" "$PREV_META")

  # 压测库里**数出来**的行数。它与 seed-bench 声称的 products 是两回事：
  # 前者是"我现在压的东西"，后者是"造数器以为我压的东西"。两者不等 = 压错库。
  local DB_SKU DB_ORD
  DB_SKU=$(docker compose exec -T api sqlite3 /data/openstore.db "select count(*) from Product;" 2>/dev/null | tr -d ' \r')
  DB_ORD=$(docker compose exec -T api sqlite3 /data/openstore.db "select count(*) from Order;" 2>/dev/null | tr -d ' \r')

  # ---- 规格与数据量指纹：结论必须能和"在什么机器、多少数据上跑"绑定 ----
  # 不带指纹的压测结论无法比较：同一份脚本在 8 核开发机与 2C2G 上跑，绿的含金量完全不同。
  # 键名一律带来源前缀——`db_*` 是本脚本从容器里数出来的，`seed_*` 是 seed-bench 声称的。
  # 第一版两边都叫 `products`，同一个文件里出现了两行同名键，grep 取第一个：
  # 谁先谁后取决于写入顺序，等于给后来人埋了一个"看到哪个算哪个"的坑。
  {
    printf 'tier=%s (%s)\n' "$TIER" "$TIER_DESC"
    printf 'time=%s\n' "$TS"
    printf 'nproc=%s\n' "$( (nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo '?') | head -1)"
    printf 'mem_total=%s\n' "$( (awk '/MemTotal/{printf "%.1fG", $2/1048576}' /proc/meminfo 2>/dev/null) || echo '?' )"
    printf 'rss_budget_mb=%s ttfb_p95_ms=%s\n' "$RSS_BUDGET_MB" "$TTFB_P95_MS"
    printf 'duration_s=%s interval_s=%s conc=%s cold_s=%s\n' "$DUR" "$INTERVAL" "$CONC" "$COLD"
    printf 'db_products=%s db_orders=%s\n' "${DB_SKU:-?}" "${DB_ORD:-?}"
    if [ -n "$SEED_META" ]; then
      printf 'seed_version=%s\n' "$SM_VERSION"
      printf 'seed_products=%s seed_variants=%s seed_products_per_category=%s\n' "$SM_PRODUCTS" "$SM_VARIANTS" "$SM_PPC"
      printf 'seed_fingerprint=%s\n' "$SM_FP"
      printf 'seed_verdict=%s\n' "$SM_VERDICT"
      printf 'seed_quality=wrong_variant_count=%s duplicate_axis_signatures=%s\n' "${SM_BADVAR:-?}" "${SM_BADAXIS:-?}"
      # 「每分类 500」只证明列表数据量。原样抄，不要改写 —— 下游读的是这句话本身。
      if [ -n "$SM_SCOPE" ]; then
        printf 'seed_category_check_scope=%s\n' "$SM_SCOPE"
      else
        # 同样不在参数展开里放中文（bash 3.2）。缺这个键时必须把"别过度解读"写出来，
        # 否则下游会把"每分类 500 ✅"读成"分类页指标已达标"。
        printf 'seed_category_check_scope=?（该版本 seed-bench 未打印此键：只证明列表数据量，不代表分类页指标已达标）\n'
      fi
      printf 'seed_meta_file=%s\n' "$SEED_META"
      # 跨轮可比性进 META：报告被单独转发时，这一行是唯一能阻止"两轮斜率相减"的东西
      printf 'comparable=%s prev_meta_file=%s prev_fingerprint=%s\n' \
        "$COMPARABLE" "${PREV_META:--}" "$( [ "$COMPARABLE" = "no" ] || [ "$COMPARABLE" = "yes" ] && seed_get "$PREV_META" fingerprint || printf '-' )"
    else
      # 空着比编一个强，但必须**写明为什么空**，否则后来人会当成"填了但没值"
      printf 'seed_version=? seed_products=? seed_variants=? seed_fingerprint=? seed_verdict=?\n'
      printf 'seed_meta=未给 --seed-meta：本次结论无法与其它轮次横向比较（先怀疑数据集变了，再怀疑代码退化）\n'
    fi
    printf 'sitemap_urls=%s\n' "$(curl -s --max-time 10 "${BASE}/sitemap.xml" 2>/dev/null | grep -c '<loc>')"
  } > "$META" 2>&1
  say "   规格/数据量指纹：${META}"
  say "   $(comparable_verdict_line "$COMPARABLE")"

  # ---- 对账：数出来的行数 vs 造数器声称的行数 ----
  # 这一步失败要**立刻**退，不能等到 30 分钟后按 A8 判 P95 —— 压的不是那批数据，
  # 后面每一个数字都不代表 AC-12 要测的东西。
  case "$DB_SKU" in ''|*[!0-9]*) DB_SKU="" ;; esac
  if [ -n "$SM_PRODUCTS" ] && [ -n "$DB_SKU" ] && [ "$SM_PRODUCTS" != "$DB_SKU" ]; then
    printf 'db_products_mismatch=1 db_products=%s seed_products=%s\n' "$DB_SKU" "$SM_PRODUCTS" >> "$META"
    printf 'PRECONDITION: 压测库里的商品数（%s）与 --seed-meta 记录（%s，fingerprint=%s）不一致 → 压的不是那批数据 → 未验证，不是通过\n' \
      "$DB_SKU" "$SM_PRODUCTS" "$SM_FP" >&2
    printf '  要么库被重建过（换库后没换 --seed-meta），要么 seed-bench 灌的不是这个 /data/openstore.db。先对齐再看性能。\n' >&2
    exit 2
  fi

  # ---- 压测前的 OOM 基线（增量判定的起点）----
  local RESTARTS_BEFORE
  RESTARTS_BEFORE=$(docker inspect -f '{{.RestartCount}}' "$cid_api" "$cid_store" "$cid_caddy" 2>/dev/null \
                    | awk '{s+=$1} END{print s+0}')
  say "   压测前重启计数合计：${RESTARTS_BEFORE}"

  local URLS
  URLS=("${BASE}/zh/" "${BASE}/zh/c/robot-parts")
  local prod
  prod=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "${BASE}/sitemap.xml" >/dev/null 2>&1; \
         curl -s --max-time 10 "${BASE}/sitemap.xml" | grep -oE '/[a-z]{2}/products/[^<]+' | head -1)
  [ -n "$prod" ] && URLS+=("${BASE}${prod}")

  # ---- 冷启动阶段：AC-12 最容易 OOM 的窗口（分类树 + 模板 + 首屏区块同时加载）----
  if [ "${COLD}" -gt 0 ] 2>/dev/null; then
    if [ "$COLD_RESTART" -eq 1 ]; then
      say "   冷启动：先 restart api storefront（模拟刚上线），再立刻压 ${COLD}s"
      docker compose restart api storefront >/dev/null 2>&1 || true
      local k
      k=0
      until curl -fsS --max-time 2 -o /dev/null "http://127.0.0.1:5080/health/ready" 2>/dev/null; do
        k=$((k+1)); [ "$k" -gt 60 ] && break; sleep 1
      done
    else
      say "   冷启动：不 restart（假定你刚起过站），直接压 ${COLD}s。要真的冷启请加 --cold-restart"
    fi
  else
    say "   冷启动阶段：跳过（--cold 0）。注意：热稳态过不等于能上线。"
  fi
  local KERN_SINCE
  KERN_SINCE=$(date -u '+%Y-%m-%d %H:%M:%S')

  # ---- 加压：CONC 个 worker（原草稿的 CONC 是死参数，这里真的用上）----
  local STOP="$ROOT/artifacts/.soak-stop-${TS}"
  rm -f "$STOP"
  worker() {
    local u out
    while [ ! -f "$STOP" ]; do
      for u in "${URLS[@]}"; do
        [ -f "$STOP" ] && break
        out=$(curl -s -o /dev/null -w '%{http_code} %{time_starttransfer}' --max-time 10 "$u" 2>/dev/null) || out="000 0"
        printf '%s\n' "$out" >> "$REQLOG"
      done
    done
  }
  local w
  for w in $(seq 1 "$CONC"); do worker & done

  # ---- 采样：RSS（按单位换算），两段（cold / steady）写同一份 CSV，用 phase 列区分 ----
  sample_phase() {
    local secs="$1" phase="$2" stop_at m_api m_store m_caddy
    stop_at=$(( $(date +%s) + secs ))
    while [ "$(date +%s)" -lt "$stop_at" ]; do
      m_api=$(to_mb "$(docker stats --no-stream --format '{{.MemUsage}}' "$cid_api" 2>/dev/null | awk -F/ '{print $1}')")
      m_store=$(to_mb "$(docker stats --no-stream --format '{{.MemUsage}}' "$cid_store" 2>/dev/null | awk -F/ '{print $1}')")
      m_caddy=$(to_mb "$(docker stats --no-stream --format '{{.MemUsage}}' "$cid_caddy" 2>/dev/null | awk -F/ '{print $1}')")
      printf '%s,%s,%s,%s,%s\n' "$(date +%s)" "${m_api}" "${m_store}" "${m_caddy}" "$phase" >> "$OUT"
      sayv "  [$(date -u +%H:%M:%S)][${phase}] api=${m_api}MB store=${m_store}MB caddy=${m_caddy}MB"
      sleep "$INTERVAL"
    done
  }
  [ "${COLD}" -gt 0 ] 2>/dev/null && sample_phase "$COLD" cold
  sample_phase "$DUR" steady

  touch "$STOP"; wait 2>/dev/null || true
  rm -f "$STOP"

  # ---- 压测后的 OOM 证据（三源：容器态 / cgroup / 内核日志）----
  docker inspect -f '{{.Name}} OOMKilled={{.State.OOMKilled}} Restarts={{.RestartCount}} ExitCode={{.State.ExitCode}}' \
    "$cid_api" "$cid_store" "$cid_caddy" 2>/dev/null | sed 's#^/##' > "$INSP"
  : > "$EVENTS"
  local c
  for c in "$cid_api" "$cid_store" "$cid_caddy"; do
    local nm
    nm=$(docker inspect -f '{{.Name}}' "$c" 2>/dev/null | sed 's#^/##')
    local ev
    ev=$(docker exec "$c" cat /sys/fs/cgroup/memory.events 2>/dev/null | awk '$1=="oom_kill"{print $2}')
    printf '%s oom_kill=%s\n' "$nm" "${ev:-0}" >> "$EVENTS"
  done

  # 内核日志源：唯一能抓到"宿主机整体 OOM、被杀的不是这三个容器"的证据。
  # 采不到就 rc=2（三源缺一只能猜），除非操作者显式 --kernel-log skip（记 WARN）。
  : > "$KERN"
  if [ "$KERNEL_MODE" = "skip" ]; then
    say "   WARN 内核日志源被显式跳过（--kernel-log skip）：A7 只剩 cgroup 一个来源，证据强度下降，回填时必须写明"
  elif command -v journalctl >/dev/null 2>&1; then
    journalctl -k --since "$KERN_SINCE" 2>/dev/null \
      | grep -iE 'out of memory|killed process' > "$KERN" 2>/dev/null || true
    if [ ! -s "$KERN" ] && ! journalctl -k -n 1 >/dev/null 2>&1; then
      printf 'PRECONDITION: 内核日志不可读（journalctl 需要权限，请用 sudo 跑；或用 --kernel-log skip 显式放弃该来源）→ 未验证\n' >&2
      touch "$STOP"; wait 2>/dev/null || true; rm -f "$STOP"; exit 2
    fi
  else
    printf 'PRECONDITION: 没有 journalctl（非 systemd 宿主）→ 内核日志源不可用 = 未验证（或用 --kernel-log skip 显式放弃）\n' >&2
    touch "$STOP"; wait 2>/dev/null || true; rm -f "$STOP"; exit 2
  fi

  say ""
  say "== 证据摘要（数值，不是门限；供回填与趋势对比）"
  awk -F, 'NR>1 && $5=="cold"   {t=$2+$3+$4; if(t>m) m=t} END{if (m=="") print "  冷启动阶段峰值 RSS = 未采集（--cold 0）"; else printf "  冷启动阶段峰值 RSS = %.1f MB\n", m}' "$OUT"
  awk -F, 'NR>1 && $5=="steady" {t=$2+$3+$4; if(t>m) m=t} END{if (m=="") print "  稳态阶段峰值 RSS = 未采集"; else printf "  稳态阶段峰值 RSS = %.1f MB\n", m}' "$OUT"
  # 斜率：稳态段合计 RSS 对采样序号的最小二乘斜率（换算成 MB/min）。
  # **观测项、不设门限**：一次 30 分钟的斜率不足以区分"慢泄漏"与"缓存还没热透"，
  # 至少要三次同规格跑出同向趋势才可下结论。设成门限只会造出一个偶尔红、没人看得懂的断言。
  awk -F, -v iv="$INTERVAL" '
    NR>1 && $5=="steady" {n++; x=n; y=$2+$3+$4; sx+=x; sy+=y; sxx+=x*x; sxy+=x*y}
    END{
      if (n<3) {print "  稳态 RSS 斜率 = 样本不足（<3）"; exit}
      d=n*sxx-sx*sx
      if (d==0) {print "  稳态 RSS 斜率 = 无法计算"; exit}
      printf "  稳态 RSS 斜率 = %+.2f MB/min（%d 个稳态采样点；观测项，无门限）\n", (n*sxy-sx*sy)/d*(60/iv), n
    }' "$OUT"
  say ""
  say "== 判定（A1..A8；判定项清单以 --list 为唯一出处）"
  local rc
  judge "$OUT" "$REQLOG" "$INSP" "$EVENTS" "$KERN" "$min_samples" "$min_reqs"
  rc=$?
  print_summary "$rc"

  say ""
  say "原始证据（回填 docs/06 的 TC-P-06 / TC-OPS-05 时贴这几份）："
  say "  ${OUT}"
  say "  ${REQLOG}"
  say "  ${INSP}"
  say "  ${EVENTS}"
  say "  ${KERN}   （内核 OOM 记录；--kernel-log skip 时为空）"
  say "  ${META}   （规格与数据量指纹：结论必须与它绑定才能比较）"
  [ "$rc" -eq 0 ] && [ "$FAIL" -eq 0 ] && exit 0
  exit "$rc"
}

if [ "$SELF_TEST" -eq 1 ]; then
  self_test
  exit $?
fi

main "$@"
