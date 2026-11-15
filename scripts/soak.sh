#!/usr/bin/env bash
# =============================================================================
# OpenStore AC-12 压测：30 分钟持续加压 + 三进程 RSS 采样 + OOM 证据链 + 自动判定
#
# 用法：
#   ./scripts/soak.sh --tier small  --duration 1800 --conc 8
#   ./scripts/soak.sh --tier standard --duration 1800 --conc 12
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
    --list)
      printf 'A1 采样点充足（否则 rc=2 未验证）\n'
      printf 'A2 请求量充足（否则 rc=2 未验证）\n'
      printf 'A3 压测期间无非 2xx/3xx\n'
      printf 'A4 三进程 RSS 合计峰值 ≤ 档位预算\n'
      printf 'A5 单进程不得贴近容器限额（WARN，不阻断）\n'
      printf 'A6 OOMKilled=false 且重启次数增量 0\n'
      printf 'A7 cgroup memory.events 的 oom_kill 增量 0\n'
      printf 'A8 TTFB P95 ≤ 上限\n'
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
  local csv="$1" req="$2" insp="$3" events="$4" min_samples="$5" min_reqs="$6"

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

  # ---- A7 cgroup oom_kill 增量 ----
  local kills
  kills=$(awk '{for(i=1;i<=NF;i++) if ($i ~ /^oom_kill=/) {split($i,a,"="); s+=a[2]}} END{print s+0}' "$events")
  if [ "$kills" -gt 0 ]; then
    emit_fail A7 "cgroup oom_kill" "oom_kill 增量 ${kills}（这个计数重启也不清零，最硬的一条证据）"
  else
    emit_ok A7 "cgroup oom_kill" "增量 0"
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
  run_case() {  # $1 名称 $2 期望（- 表示应绿 / A1..A8 / rc2）$3..$5 四个文件
    local name="$1" want="$2" csv="$3" req="$4" insp="$5" ev="$6"
    total=$((total+1))
    : > "$TMPD/out"
    ( judge "$csv" "$req" "$insp" "$ev" 8 100 ) > "$TMPD/out" 2>&1
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

  local TS OUT REQLOG
  TS=$(date -u +%Y%m%dT%H%M%SZ)
  OUT="$ROOT/artifacts/soak-${TS}.csv"
  REQLOG="$ROOT/artifacts/soak-${TS}-req.log"
  mkdir -p "$ROOT/artifacts"
  printf 'ts,api_mb,store_mb,caddy_mb\n' > "$OUT"
  : > "$REQLOG"

  local min_samples min_reqs
  min_samples=$(( DUR / INTERVAL / 2 )); [ "$min_samples" -lt 6 ] && min_samples=6
  min_reqs=$(( CONC * DUR / 10 )); [ "$min_reqs" -lt 100 ] && min_reqs=100

  say "== AC-12 压测：${TIER_DESC}"
  say "   目标 ${BASE}  时长 ${DUR}s  并发 ${CONC}  采样间隔 ${INTERVAL}s"
  say "   预算：RSS 合计 ≤ ${RSS_BUDGET_MB} MB；TTFB P95 ≤ ${TTFB_P95_MS} ms；三进程 OOM 证据必须干净"
  say "   原始数据：${OUT}    请求明细：${REQLOG}"

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

  # ---- 采样：RSS（按单位换算）+ 结束时读 OOM 证据 ----
  local end i
  end=$(( $(date +%s) + DUR ))
  i=0
  while [ "$(date +%s)" -lt "$end" ]; do
    local m_api m_store m_caddy
    m_api=$(to_mb "$(docker stats --no-stream --format '{{.MemUsage}}' "$cid_api" 2>/dev/null | awk -F/ '{print $1}')")
    m_store=$(to_mb "$(docker stats --no-stream --format '{{.MemUsage}}' "$cid_store" 2>/dev/null | awk -F/ '{print $1}')")
    m_caddy=$(to_mb "$(docker stats --no-stream --format '{{.MemUsage}}' "$cid_caddy" 2>/dev/null | awk -F/ '{print $1}')")
    printf '%s,%s,%s,%s\n' "$(date +%s)" "${m_api}" "${m_store}" "${m_caddy}" >> "$OUT"
    sayv "  [$(date -u +%H:%M:%S)] api=${m_api}MB store=${m_store}MB caddy=${m_caddy}MB"
    i=$((i+1))
    sleep "$INTERVAL"
  done

  touch "$STOP"; wait 2>/dev/null || true
  rm -f "$STOP"

  # ---- 压测后的 OOM 证据 ----
  local INSP EVENTS
  INSP="$ROOT/artifacts/soak-${TS}-inspect.txt"
  EVENTS="$ROOT/artifacts/soak-${TS}-events.txt"
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

  say ""
  say "== 判定（A1..A8）"
  local rc
  judge "$OUT" "$REQLOG" "$INSP" "$EVENTS" "$min_samples" "$min_reqs"
  rc=$?
  print_summary "$rc"

  say ""
  say "原始证据（回填 docs/06 的 TC-P-06 / TC-OPS-05 时贴这几份）："
  say "  ${OUT}"
  say "  ${REQLOG}"
  say "  ${INSP}"
  say "  ${EVENTS}"
  [ "$rc" -eq 0 ] && [ "$FAIL" -eq 0 ] && exit 0
  exit "$rc"
}

if [ "$SELF_TEST" -eq 1 ]; then
  self_test
  exit $?
fi

main "$@"
