#!/usr/bin/env bash
# =============================================================================
# OpenStore 工程门禁（fail-before-pass 的「pass」一半）
#
# 用法：
#   ./scripts/check.sh            # 跑当前里程碑已实现的全部步骤
#   ./scripts/check.sh --full     # 跑全部步骤（含未实现的，未实现即判红；步数以 --list 为准，本行不写数字）
#   ./scripts/check.sh --fast     # 只跑 1~6 步（提交前用，目标 < 90 秒）
#   ./scripts/check.sh --list     # 只列步骤，不执行
#
# 退出码：见 `docs/05 §4.6`（0 通过 / 1 失败 / 2 未验证 / 3 用法错误 的**定义只在那里**，
#         本文件不复述数字含义 —— 四份各自写一遍，就是四份会各自漂的真相）。
#   本脚本相关的一条契约：默认模式 skipped>0 仍退 0，但末行打印 `VERDICT: PARTIAL(p/n)`；
#   `--full` 下 skipped>0 退 2（`--full` 承诺"全部"，有跳过即不成立）；
#   要"全量通过"这个判断，就调 `--full` 并认 `VERDICT: PASS(n/n)`。
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
  --self-test) MODE="selftest" ;;
  "")     MODE="default" ;;
  *)      echo "unknown arg: $1" >&2; exit 3 ;;
esac

# ---------------------------------------------------------------------------
# 退出码的唯一算点：**汇总之后不再有任何 if 各自 exit**，否则同一个脚本里会出现
# 两套"什么算通过"。抽成函数是为了能被 --self-test 直接喂四个输入验四个码 ——
# 退出码是契约，契约必须可测，而不是"跑一遍看看是不是 0"。
# ---------------------------------------------------------------------------
verdict_token() {  # $1 pass $2 fail $3 skip $4 mode → stdout token；返回码 = 进程退出码
  local p="$1" f="$2" k="$3" m="$4" n
  n=$((p + f + k))
  if [ "$f" -gt 0 ]; then
    printf 'VERDICT: FAIL(%s/%s)' "$p" "$n"; return 1
  fi
  if [ "$k" -gt 0 ] && [ "$m" = "full" ]; then
    printf 'VERDICT: PARTIAL(%s/%s)' "$p" "$n"; return 2
  fi
  if [ "$k" -gt 0 ]; then
    printf 'VERDICT: PARTIAL(%s/%s)' "$p" "$n"; return 0
  fi
  printf 'VERDICT: PASS(%s/%s)' "$p" "$n"; return 0
}

self_test_exit_codes() {
  local failn=0 total=0
  vc() {  # $1 名称 $2 期望码 $3 期望 token $4 pass $5 fail $6 skip $7 mode
    local name="$1" want_rc="$2" want_tok="$3" out rc
    total=$((total+1))
    out=$(verdict_token "$4" "$5" "$6" "$7"); rc=$?
    if [ "$rc" = "$want_rc" ] && [ "$out" = "$want_tok" ]; then
      printf '  ok    %s → %s（rc=%s）\n' "$name" "$out" "$rc"
    else
      printf '  WRONG %s → 期望=%s/rc=%s 实得=%s/rc=%s\n' "$name" "$want_tok" "$want_rc" "$out" "$rc"
      failn=$((failn+1))
    fi
  }
  printf '== check.sh 退出码自证（四个码各一例，不跑构建）==\n'
  vc "全绿"                          0 "VERDICT: PASS(18/18)"       18 0 0  default
  vc "有步骤失败 → 1（即便也有跳过）"  1 "VERDICT: FAIL(17/18)"       17 1 0  default
  vc "--full 下有跳过 → 2（承诺全部却不全）" 2 "VERDICT: PARTIAL(13/18)" 13 0 5 full
  vc "默认模式有跳过 → 0 + PARTIAL（设计内的跳过不是异常）" 0 "VERDICT: PARTIAL(13/18)" 13 0 5 default

  # 第四个码（3 = 用法错误）只能由进程自己给出：verdict_token 不参与参数解析。
  local rc3
  bash "$0" --definitely-not-an-arg >/dev/null 2>&1; rc3=$?
  total=$((total+1))
  if [ "$rc3" = "3" ]; then
    printf '  ok    不认识参数 → rc=3（用法错误，不是"未验证"）\n'
  else
    printf '  WRONG 不认识参数 → 期望 rc=3 实得 rc=%s\n' "$rc3"
    failn=$((failn+1))
  fi

  echo
  if [ "$failn" -eq 0 ]; then
    printf '== 自证通过：%s 项（0/1/2/3 各一例）==\n' "$total"
    return 0
  fi
  printf '== 自证失败：%s/%s 项 ==\n' "$failn" "$total"
  return 1
}

if [ "$MODE" = "selftest" ]; then
  self_test_exit_codes
  exit $?
fi

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

# ---------------------------------------------------------------------------
# 并发锁（mkdir 原子锁；不用 flock —— 那是 util-linux，macOS 默认没有）
#
# 为什么要锁：同时跑两个 check.sh 会互相踩构建产物与测试临时目录，结果是**假失败**。
# 假失败的门禁下场和恒红门禁一样——被关掉。这不是"有点吵"，是门禁的存亡问题。
#
# 三条设计约束（少一条就不该加这个锁）：
#   1) mkdir 在两个平台都是原子的；`flock` 在 macOS 上根本不存在。
#   2) 抢不到必须 **rc=2 快速失败**，并打印持锁 pid + 起始时间 + 建议动作。
#      静默等待会让人以为卡死；静默继续等于没锁。
#   3) 过期锁**自愈**：持锁进程已不存在（kill -0 失败）或锁龄超阈值 → 打印"回收过期锁"后接管。
#      手工 rm 锁文件是这类脚本最常见的运维事故，不该要求人去做。
#      锁龄也要判，因为 pid 会被无关进程复用：那时 kill -0 成功、锁却早该回收了。
# ---------------------------------------------------------------------------
# TMPDIR 在 macOS 上带尾斜杠，直接拼会出现 `T//openstore-check.lock`（只是难看，
# 但报错信息里的路径就是人要去核对的东西，不该带噪声）
LOCK_ROOT="${CHECK_LOCK_DIR:-${TMPDIR:-/tmp}}"
LOCK_ROOT="${LOCK_ROOT%/}"
LOCKDIR="$LOCK_ROOT/openstore-check.lock"
LOCK_STALE_SECONDS="${CHECK_LOCK_STALE_SECONDS:-1800}"
LOCK_HELD=0

cleanup_all() {
  [ "$LOCK_HELD" = "1" ] && rm -rf "$LOCKDIR"
  rm -rf "$LOGDIR"
}
trap cleanup_all EXIT INT TERM

lock_field() {  # $1 文件 → 内容（去掉空白与 CR；文件不存在时为空）
  [ -f "$1" ] && tr -d ' \r\n' < "$1" 2>/dev/null || true
}

acquire_lock() {
  local pid started age now
  if mkdir "$LOCKDIR" 2>/dev/null; then
    LOCK_HELD=1
    printf '%s\n' "$$" > "$LOCKDIR/pid"
    date +%s > "$LOCKDIR/epoch"
    printf 'pid=%s cwd=%s mode=%s\n' "$$" "$ROOT" "$MODE" > "$LOCKDIR/who"
    return 0
  fi

  pid=$(lock_field "$LOCKDIR/pid")
  started=$(lock_field "$LOCKDIR/epoch")
  now=$(date +%s)
  age="?"
  [ -n "$started" ] && case "$started" in ''|*[!0-9]*) age="?" ;; *) age=$((now - started)) ;; esac

  if [ -n "$pid" ] && case "$pid" in ''|*[!0-9]*) false ;; *) kill -0 "$pid" 2>/dev/null ;; esac; then
    if [ "$age" != "?" ] && [ "$age" -le "$LOCK_STALE_SECONDS" ]; then
      {
        printf 'LOCKED: 另一个 check.sh 正在运行，本次拒绝并行执行（rc=2）\n'
        printf '  持锁 pid=%s  已运行 %ss（阈值 %ss）  %s\n' "$pid" "$age" "$LOCK_STALE_SECONDS" \
          "$( [ -f "$LOCKDIR/who" ] && cat "$LOCKDIR/who" || printf '起于=? (旧版锁没有记录)' )"
        printf '  为什么拒绝：两个门禁同时跑会互相踩构建产物与测试临时目录 → 假失败；\n'
        printf '  假失败的门禁最后一定会被关掉。等它结束再跑，不要并行。\n'
        printf '  若确认那个进程已经不该存在：kill %s，本脚本会自动回收过期锁（不要手工 rm -rf）\n' "$pid"
        printf 'GATE FAILED: check_lock (busy)\n'
      } >&2
      exit 2
    fi
    printf '回收过期锁：%s（pid=%s 仍存在，但锁龄 %ss 超过阈值 %ss；pid 可能已被无关进程复用）\n' \
      "$LOCKDIR" "$pid" "$age" "$LOCK_STALE_SECONDS"
  else
    printf '回收过期锁：%s（持锁 pid=%s 已不存在，上一个门禁是被强杀的？）\n' "$LOCKDIR" "${pid:-?}"
  fi

  rm -rf "$LOCKDIR"
  if mkdir "$LOCKDIR" 2>/dev/null; then
    LOCK_HELD=1
    printf '%s\n' "$$" > "$LOCKDIR/pid"
    date +%s > "$LOCKDIR/epoch"
    printf 'pid=%s cwd=%s mode=%s\n' "$$" "$ROOT" "$MODE" > "$LOCKDIR/who"
    return 0
  fi

  {
    printf 'LOCKED: 回收过期锁后仍抢不到（%s），可能有第三个实例刚抢到 → rc=2\n' "$LOCKDIR"
    printf '  这不是脚本故障，是并发。重跑一次即可。\n'
    printf 'GATE FAILED: check_lock (race)\n'
  } >&2
  exit 2
}

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

# 实际"被处理过"的步骤号（run_step 与 skip_step 都算：skip 在 --full 下也判红，
# 它同样是"这一步被考虑过了"）。下面第 4 条断言要拿它和 --list 的声明对账。
EXECUTED_NUMS=" "

run_step() {
  local no="$1" name="$2" desc="$3"; shift 3
  local log="$LOGDIR/$name.log"
  EXECUTED_NUMS="$EXECUTED_NUMS$no "
  printf '[%2s] %-20s ' "$no" "$name"
  if "$@" >"$log" 2>&1; then
    # 明细后缀：步骤可以在输出里打一行 `DETAIL: …`，这里把它提到 OK 同一行。
    # **为什么要有它**：判据的价值常常在"检查了 47 个"这种数量上（0 个和 47 个
    # 都是绿），但 `run_step` 只在失败时回放日志 —— 于是判据把话印出来，绿的
    # 时候没人看得到，判据等于白设计。同 `soak.sh 来源 = HEAD:<sha>`：**锚必须
    # 出现在绿的路径上**，只在失败时显示的锚等于没有锚。
    local detail
    detail=$(sed -n 's/^DETAIL: //p' "$log" 2>/dev/null | head -1)
    if [ -n "${detail:-}" ]; then printf 'ok（%s）\n' "$detail"
    else printf 'ok\n'; fi
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
  EXECUTED_NUMS="$EXECUTED_NUMS$no "
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
  step 16 table_integrity     "docs 表格孤行/列数不符/围栏未配对"
  step 17 bench_self_test     "基准数据生成器自证（幂等/确定性/受保护库/非法tier）"
  step 18 change_numbers      "变更编号台账（缺号/重号判红，行序错位只 WARN）"
  step 19 doc_anchors         "脚本引用的 docs/NN §节号必须真实存在"
  step 20 fixture             "AC-14a 验收 fixture 声明侧判据（不连库、不依赖 .NET）"
  step 21 soak_self_test      "压测判定逻辑自证（跑 HEAD 版：验的是提交后的状态）"
  step 22 ledger_landing      "台账裁决的落点在正文里真的提过（L1~L5，含棘轮）"
  exit 0
fi

# --list 是只读的枚举，不该占锁；真正会动构建产物的是下面这些步骤。
acquire_lock

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

# fixture 判据只读 tests/fixtures/*.json，1.7s、零依赖。**放在 fast 收口之前**：
# 改数据文件而不跑代码测试是最常见的操作，若它只在 full 档跑，改数据的人 push 完就结束了。
# 它的 --self-test（约 29s，17 条注入逐条必须变红）只在 full 档跑 —— 塞进 fast 会把
# 90 秒目标直接顶穿，然后大家开始用 --no-verify。
if [ "$MODE" = "fast" ]; then
  run_step 20 fixture "AC-14a 验收 fixture 声明侧判据" \
    bash -c './scripts/check-fixture.sh'
  echo; echo "== --fast 到此为止 =="; goto_summary=1
fi

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
  #
  # 先跑 `--self-test` 再跑正式扫描，且两者必须串在同一行（同一个入口）：
  # 这个脚本的豁免逻辑是「按文件结构推导区间」，推导错了不会报错、只会少扫一段，
  # 于是判绿——这是最容易假绿的一类判据。而自证不进 CI 就会慢慢烂掉：
  # verify-seo-matrix.sh 的 --self-test 曾经坏到「只打印一行标题就退出」，
  # 整整一轮没人发现，因为没人跑它。所以自证必须被每一次门禁运行带着跑。
  run_step 15 doc_version_refs "文档版本号引用" \
    bash -c './scripts/check-doc-version-refs.sh --self-test && ./scripts/check-doc-version-refs.sh'

  # ---- 16 表格完整性 --------------------------------------------------------
  # 与 15 相邻不是巧合：两者治的是同一个盲区——**文档结构坏了，但没有任何东西会报错**。
  # 表格断掉时 GFM 不警告，只是把那些行当普通段落渲染，于是"一份看起来在表里的内容"
  # 实际不在表里；它长得仍然像表格（文本里全是竖线），评审扫过去不会停。
  # 这一族已经两次成规模出现（05 的 Q15 行；00/03/04/06 共 4 处），故从评审移交门禁。
  #
  # 判据三条：T1 孤行/表头缺失、T2 列数不符、T3 围栏未配对。
  # 同样把 `--self-test` 串在同一行：它的判定依赖「引用深度」与「围栏状态」两个状态机，
  # 而状态机写错的方向是**少判**——把该红的行跳过，表现就是绿。这个方向没有别的信号。
  run_step 16 table_integrity "表格完整性" \
    bash -c './scripts/check-tables.sh --self-test && ./scripts/check-tables.sh'

  # ---- 17 基准数据生成器自证 ------------------------------------------------
  # 为什么这条必须进门禁：造数工具的全部失败方式都是"看起来成功了"——少造一半
  # （压测绿得漂亮，因为负载轻了）、第二次跑又插一遍、灌进了开发库（本地账号与手工
  # 数据没了）。三种都不会让调用方看到非零退出码。它的判据（幂等 / 跨库确定性 /
  # 受保护库 / 非法 tier）只活在 `--self-test` 里，而**没被跑过的自证会烂掉**——
  # verify-seo-matrix.sh 的 self-test 坏过整整一轮没人发现。
  #
  # 这一条**不带 --no-build**，是有意的：tools 工程不在 OpenStore.sln 里，
  # 第 1 步的 `dotnet build` 根本不覆盖它，带 --no-build 就会静默地跑上一版二进制
  # （实测过：源码改坏、构建失败，--no-build 照跑旧产物、自证全绿）。
  # 因此这一步自己带 build，代价是几秒。
  run_step 17 bench_self_test "基准数据生成器自证" \
    dotnet run --project tools/OpenStore.Tools -- seed-bench --self-test

  # ---- 18 变更编号台账 ------------------------------------------------------
  # 这条脚本早就存在、自证也有，却**没有任何入口在跑它**——一个只有在自己被想起时
  # 才存在的门禁，等于不存在（同 §17 的道理：没被跑过的自证会烂掉）。
  #
  # 它守的是"编号是永久记录"：该条作废就在描述里标已作废并保留编号，不要抽号。
  # 抽号不会让任何东西变红，只会让所有"见 #NN"静默指错——而引用错是读不出来的。
  # 判据 3 比**集合**不比行序：缺号 = 真的丢了记录（红），行序错位 = 抄写痕迹（WARN）。
  run_step 18 change_numbers "变更编号台账" \
    bash -c './scripts/check-change-numbers.sh --self-test && ./scripts/check-change-numbers.sh'
fi

# ---- 19 文档锚点 ------------------------------------------------------------
# 本轮把"退出码含义"收进 docs/05 §4.6 一处，四个脚本头部改成指针。指针的价值全在
# "指向的那一节真的存在"——文档一改节号，指针就静默指到别处，而脚本照样绿。
# 这类漂移没有运行时症状，只能靠结构断言挡。先跑 --self-test 证明它抓得住。
if [ "${goto_summary:-0}" != "1" ]; then
  run_step 19 doc_anchors "脚本引用的 docs 节号真实存在" \
    bash -c './scripts/check-doc-anchors.sh --self-test && ./scripts/check-doc-anchors.sh'

  # ---- 20 AC-14a fixture（声明侧）--------------------------------------------
  # 三值退出码（0 全过 / 1 有判据失败 / 2 前置缺失），见 docs/05 §4.6：
  # **2 等价于不通过**，不许写成 `[ $? -le 1 ]`。
  # --self-test 进 full 档的理由：判据会不会悄悄变成"永绿"，只有注入能发现
  # （本轮真发生过：C7 用复合串 group_by，跨商品的 SKU 撞车永远抓不到，而它一直绿）。
  run_step 20 fixture "AC-14a 验收 fixture 声明侧判据" \
    bash -c './scripts/check-fixture.sh --self-test >/dev/null && ./scripts/check-fixture.sh'

  # ---- 21 soak 判定逻辑自证（跑 HEAD 版，不跑工作区版）-------------------------
  # `soak.sh --self-test` 早就交付、自证也有，却**没有任何入口在跑它**——今天实测
  # `grep -rn soak scripts/check.sh scripts/verify-gates.sh` = 0 处（同 §18 的道理：
  # 没被跑过的自证会烂掉，而它烂掉时没人知道，因为它的产物在 30 分钟压测之后）。
  #
  # **为什么要导出 HEAD 版，而不是直接跑工作区那份文件**：这道门的作用是给"提交后的
  # 状态"背书。跑工作区版时，任何人正在编辑 soak.sh（今天发生过不止一次）都会让
  # **别人的**门禁变红，而那种红的唯一后果是人学会跳过这一步。跑 HEAD 版则：
  #   红 → HEAD 上的判定逻辑真的坏了（这是要报的）；
  #   绿 → **但它是 HEAD 的读数，不是工作区的读数**。
  # 所以下面那行来源必须打在 run_step 外面——run_step 只在失败时才回放日志，
  # 打在里面的"来源"在绿的时候是看不见的，而**没有出处的绿等于一个没有单位的数**。
  printf '     soak.sh 来源 = HEAD:%s\n' "$(git rev-parse --short HEAD 2>/dev/null || echo '?')"
  run_step 21 soak_self_test "压测判定逻辑自证（HEAD 版）" \
    bash -c 'tmp=$(mktemp -d) || exit 2;
             if ! git show HEAD:scripts/soak.sh > "$tmp/soak.sh" 2>/dev/null; then
               printf "PRECONDITION: 取不到 HEAD:scripts/soak.sh（不是 git 工作区？）\n"; exit 2; fi
             bash "$tmp/soak.sh" --self-test; rc=$?
             rm -rf "$tmp"; exit $rc'
fi

  # ---- 22 裁决落地核查（台账 → 正文落点）-------------------------------------
  # 它守的是"台账说某裁决落在 §X，而 §X 里根本没提那个名字"——本轮真实存在过
  # （`CanSellConsistencyJob` 只在 §7 任务表出现，顺着 §2.7 找不到它）。
  # **它的绿只能支撑"提过"，不能支撑"说的是那个意思"**：语义相反 / 号过期 /
  # 跨文件互斥这三类它一条都挡不住，脚本每次运行末尾会自己把这句印出来。
  # 结论行以 `DETAIL:` 回给 run_step，绿的时候也让人看见"核了多少行"。
  run_step 22 ledger_landing "台账裁决的落点真的提过（L1~L5）" \
    bash -c 'd=$(mktemp -d) || exit 2
             ./scripts/check-ledger-landing.sh --self-test >/dev/null || exit $?
             ./scripts/check-ledger-landing.sh > "$d/out" 2>&1; rc=$?
             cat "$d/out"
             sed -n "s/^结论：/DETAIL: /p" "$d/out"
             rm -rf "$d"; exit $rc'

# ---- 清单自证：--list 声明 == 实跑处理 -------------------------------------
# 步数写死连续错过三次（15→16 步时没跟着改、加 17、加 18）。根因不是"忘了改那个数字"：
# `--list` 的静态表与下面 run_step 的实跑清单**本来就是两份**，谁也不会记得同时改。
# 所以这里不是"再改一次数字"，而是把两份对账：不一致就判红并打印差集。
# 这条是"给门禁加门禁"——它挡住的正是过去几轮都发生过的那类漂移。
# --fast 提前 return 到汇总，此时没跑全，不参与对账（否则它会天天红，然后被关掉）。
if [ "${goto_summary:-0}" != "1" ]; then
  DECLARED_NUMS=$(grep -oE '^[[:space:]]*step +[0-9]+' "$0" | grep -oE '[0-9]+' | LC_ALL=C sort -u)
  ACTUAL_NUMS=$(printf '%s\n' $EXECUTED_NUMS | grep -E '^[0-9]+$' | LC_ALL=C sort -u)
  DECLARED_COUNT=$(printf '%s\n' "$DECLARED_NUMS" | grep -cE '^[0-9]+$')
  if [ "$DECLARED_NUMS" = "$ACTUAL_NUMS" ]; then
    printf '[ *] %-20s ok（--list 与实跑一致：%s 步）\n' step_count "$DECLARED_COUNT"
  else
    printf '[ *] %-20s FAIL\n' step_count
    echo "----------------------------------------------------------------"
    echo "  --list 声明的步骤号与实跑处理的步骤号对不上。这通常意味着："
    echo "    · 加了 run_step 但忘了在 --list 里加 step（或反之）"
    echo "    · 步骤号写错/复用（本脚本约定：编号不复用）"
    printf '  声明但没跑：%s\n' "$(comm -23 <(printf '%s\n' "$DECLARED_NUMS") <(printf '%s\n' "$ACTUAL_NUMS") | tr '\n' ' ')"
    printf '  跑了但没声明：%s\n' "$(comm -13 <(printf '%s\n' "$DECLARED_NUMS") <(printf '%s\n' "$ACTUAL_NUMS") | tr '\n' ' ')"
    echo "  两份清单必须逐号对齐；否则 --list / 文档 / CI 计数会各说各话。"
    echo "----------------------------------------------------------------"
    echo "GATE FAILED: step_count"
    FAIL=$((FAIL+1)); FAILED_NAMES+=(step_count)
    echo
  fi
fi

# ---- 汇总 -------------------------------------------------------------------
echo
echo "== 汇总: passed=$PASS failed=$FAIL skipped=$SKIP =="
if [ "$FAIL" -gt 0 ]; then
  printf '失败步骤: %s\n' "${FAILED_NAMES[*]}"
fi
# 末行 token 是给机器认的（退出码见 docs/05 §4.6）：默认模式的 5 个 skip 是里程碑设计，
# 不是异常，所以它退 0；但要"全量通过"这个结论，必须调 --full 并认 PASS(n/n)。
TOKEN=$(verdict_token "$PASS" "$FAIL" "$SKIP" "$MODE"); RC=$?
if [ "$SKIP" -gt 0 ] && [ "$MODE" = "default" ]; then
  echo "注意：有 $SKIP 步未实现（默认模式）。要「全量通过」这个结论请用 --full 并认 VERDICT: PASS。"
fi
echo "$TOKEN"
exit "$RC"
