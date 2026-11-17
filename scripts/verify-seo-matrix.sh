#!/usr/bin/env bash
# scripts/verify-seo-matrix.sh
#
# AC-11「索引控制矩阵」的**唯一机器判据**。语义定义见 docs/01 §6.1（S1~S11），本脚本只负责断言，不复制语义。
# 覆盖矩阵（六档 × S 编号）由 docs/01 §6.1 维护；本脚本漏一条 = 那一档没有判据 = AC-11 不通过。
#
# 用法:
#   ./scripts/verify-seo-matrix.sh https://shop.example.com
#   ./scripts/verify-seo-matrix.sh https://shop.example.com --locale en
#   ./scripts/verify-seo-matrix.sh https://shop.example.com \
#       --archived-moved /zh/products/old-sku \
#       --archived-gone  /zh/products/dead-sku \
#       --archived-gone-from /zh/products/dead-sku-v1   # 同一商品的历史 slug，也必须是 410
#       --moved-from /zh/products/kettle-v1 \
#       --moved-to   /zh/products/kettle         # 在售商品改过 slug：旧 URL 必须 301 到新 URL
#       --out-of-stock   /zh/products/sold-out   # 不给则从 sitemap 自动找
#   # 规则 4 的**负向半边**（未登记旧 slug / 语种串台，语义见 docs/06「规则 4 四断言一组」）：
#   ./scripts/verify-seo-matrix.sh https://shop.example.com --locale zh \
#       --slug-history-locale en \               # 登记过 SlugHistory 的那个语种（默认 en）
#       --slug-history-from /en/products/fx-kettle-v1 \   # 该商品改 slug 前的旧地址（必须 301）
#       --slug-history-to   /en/products/fx-kettle \      # 它的新地址（301 目标 + 正向锚）
#       --p404-a /en/products/fx-mug-v1 \        # 三条"近似但从未存在"的串，必须各自 404：
#       --p404-b /en/products/fx-kettle-v2 \     #   剥 -vN 后缀 / 版本号 +1 / 前缀模糊
#       --p404-c /en/products/fx-kettl
#     ↑ 这五条**必须由调用方给、脚本不派生**：判据自己生成的被检对象，判据无法判断它是否合法。
#       派生一个"新地址去掉末字符"的串，它可能恰好是真商品 → 期望 404 而站点 200 = 假红，
#       而假红的下一个动作是有人去改期望值，一改这条断言就废了。
#   ./scripts/verify-seo-matrix.sh --self-test     # fail-before-pass 自证（本地 mock，不连真站）
#
# 退出码：见 `docs/05 §4.6`（0/1/2/3 的定义**只在那里**，本文件不复述数字含义）。
#   本脚本落到这四个码上的情形：0 全绿（唯一通过态）/ 1 有断言失败
#   / 2 前置条件缺失（缺 sitemap、缺可筛选属性、缺归档或缺货 fixture）= 未验证
#   / 3 用法错误（参数不认识、没给站点地址）。
# CI 里只允许 0。**不要把 2 和 1 混**：2 是"今天没测成"，1 是"站点真的错了"。
set -uo pipefail

BASE=""
LOCALE="zh"
SELF_TEST=0
TIMEOUT="${TIMEOUT:-10}"
ARCH_MOVED="${ARCHIVED_MOVED_PATH:-}"
ARCH_GONE="${ARCHIVED_GONE_PATH:-}"
ARCH_GONE_FROM="${ARCHIVED_GONE_FROM_PATH:-}"
MOVED_FROM="${MOVED_FROM_PATH:-}"
MOVED_TO="${MOVED_TO_PATH:-}"
OOS="${OUT_OF_STOCK_PATH:-}"
# 规则 4 负向半边（未登记旧 slug / 语种串台）。
# SH_LOCALE 有默认值（en），其余五个**故意没有默认值**：默认值会让"忘了传"变成
# "用一个编出来的地址去断言 404，而它一直绿"。缺 = rc=2（未验证），见 run_matrix 里的前置。
SH_LOCALE="${SLUG_HISTORY_LOCALE:-en}"
SH_FROM="${SLUG_HISTORY_FROM_PATH:-}"
SH_TO="${SLUG_HISTORY_TO_PATH:-}"
P404_A="${P404_A_PATH:-}"
P404_B="${P404_B_PATH:-}"
P404_C="${P404_C_PATH:-}"

while [ $# -gt 0 ]; do
  case "$1" in
    --self-test)       SELF_TEST=1; shift ;;
    --locale)          LOCALE="$2"; shift 2 ;;
    --timeout)         TIMEOUT="$2"; shift 2 ;;
    --archived-moved)  ARCH_MOVED="$2"; shift 2 ;;
    --archived-gone)   ARCH_GONE="$2"; shift 2 ;;
    --archived-gone-from) ARCH_GONE_FROM="$2"; shift 2 ;;
    --moved-from)      MOVED_FROM="$2"; shift 2 ;;
    --moved-to)        MOVED_TO="$2"; shift 2 ;;
    --out-of-stock)    OOS="$2"; shift 2 ;;
    --slug-history-locale) SH_LOCALE="$2"; shift 2 ;;
    --slug-history-from)   SH_FROM="$2"; shift 2 ;;
    --slug-history-to)     SH_TO="$2"; shift 2 ;;
    --p404-a)          P404_A="$2"; shift 2 ;;
    --p404-b)          P404_B="$2"; shift 2 ;;
    --p404-c)          P404_C="$2"; shift 2 ;;
    -h|--help)         sed -n '2,34p' "$0"; exit 0 ;;
    -*)                echo "未知参数: $1" >&2; exit 3 ;;
    *)                 BASE="${1%/}"; shift ;;
  esac
done

OK=0; BAD=0; WARN=0
pass(){    OK=$((OK+1));     printf '  ok    %s\n' "$1"; }
fail(){    BAD=$((BAD+1));   printf '  FAIL  %s  —— %s\n' "$1" "$2"; }
warned(){  WARN=$((WARN+1)); printf '  WARN  %s  —— %s\n' "$1" "$2"; }
same(){ [ "${1%/}" = "${2%/}" ]; }

# 规范化：去追踪参数、统一尾斜杠、参数排序。
# WARN 只在"规范化后字节相等"时成立——否则"看起来自指"其实是跨 URL
# （带 utm 的自指 canonical 是最常见的一种错，不规范化会把它放过去）。
norm_url(){
  local u="$1" base q=""
  case "$u" in *\?*) base="${u%%\?*}"; q="${u#*\?}" ;; *) base="$u" ;; esac
  base="${base%/}"
  if [ -n "$q" ]; then
    q=$(printf '%s' "$q" | tr '&' '\n' | grep -viE '^(utm_[a-z_]+|gclid|fbclid|msclkid|yclid)=' | sort | tr '\n' '&')
    q="${q%&}"
  fi
  if [ -n "$q" ]; then printf '%s?%s\n' "$base" "$q"; else printf '%s\n' "$base"; fi
}

# 「这条 canonical 指向的是**别的 URL** 吗」——**只有一个出口**。
# 0 = 跨 URL（指向别处，该红）；1 = 自指（同一页）。
#
# 为什么必须是函数、不许就地写比较：S6 与 S8 的三处判断问的是**同一个问题**，各写一遍就会出现
# "两处归一口径、一处逐字口径"——上一版真的出现过（S8 的 Disallow 分支逐字比 `same`、noindex
# 分支归一化比 `norm_url`），后果是「自指但带 utm」的 canonical 被报成"外指"，失败信息还把病因
# 指到"canonical 读不到"上，属"结果对、原因错"。抽成单一出口之后，"两处口径不一致"在结构上
# **不可能发生**，那条监视它的结构断言因此可以删掉（断言监视漂移，结构消灭漂移）。
#
# **它不用于样本 C（S5b）**：那里问的是"该保留的参数有没有**逐字**保留"，与"指向哪一页"是
# 反方向的要求（S5b 用 `same` 逐字比，正是为了抓"剔过头"）。同一个函数在不同问题上合格性不同。
is_cross_url(){ [ "$(norm_url "$1")" != "$(norm_url "$2")" ]; }

# ---------------------------------------------------------------- 抓取与信号提取
HF=""; BF=""
P_STATUS=""; P_HDR=""; P_BODY=""; P_NI="no"; P_FOFF="no"; P_CAN=""; P_LOC=""

_probe_one(){
  local url="$1"
  P_STATUS=$(curl -s -o "$BF" -D "$HF" -w '%{http_code}' --max-time "$TIMEOUT" "$url" 2>/dev/null || echo 000)
  P_HDR=$(tr -d '\r' <"$HF" 2>/dev/null)
  P_BODY=$(cat "$BF" 2>/dev/null)
  P_NI="no"; P_FOFF="no"; P_CAN=""
  P_LOC=$(printf '%s\n' "$P_HDR" | grep -i '^location:' | tail -1 | sed 's/^[^:]*:[[:space:]]*//')
  if printf '%s\n' "$P_HDR"  | grep -qiE '^x-robots-tag:.*noindex'; then P_NI="yes"; fi
  if printf '%s\n' "$P_BODY" | grep -qiE '<meta[^>]*name=.?robots.?[^>]*content=[^>]*noindex'; then P_NI="yes"; fi
  if printf '%s\n' "$P_BODY" | grep -qiE '<meta[^>]*content=[^>]*noindex[^>]*name=.?robots.?'; then P_NI="yes"; fi
  if printf '%s\n' "$P_HDR"  | grep -qiE '^x-robots-tag:.*(nofollow|none)'; then P_FOFF="yes"; fi
  if printf '%s\n' "$P_BODY" | grep -qiE '<meta[^>]*name=.?robots.?[^>]*content=[^>]*(nofollow|none)'; then P_FOFF="yes"; fi
  local tag
  tag=$(printf '%s' "$P_BODY" | grep -oiE '<link[^>]*>' | grep -i 'canonical' | head -1)
  if [ -n "$tag" ]; then
    P_CAN=$(printf '%s' "$tag" | grep -o 'href="[^"]*"' | head -1 | cut -d'"' -f2)
    [ -z "$P_CAN" ] && P_CAN=$(printf '%s' "$tag" | grep -o "href='[^']*'" | head -1 | cut -d"'" -f2)
  fi
}

# 跟最多 2 跳。链上任一跳带 noindex 即算命中——被 302 的 URL 自身也可能以裸链接进结果，
# 所以"跳转源带 noindex"同样合格，不按"只看最终页"判。
probe_chain(){
  local url="$1" hop=0 ni="no" foff="no" can="" loc="" st=""
  while : ; do
    _probe_one "$url"
    st="$P_STATUS"
    [ "$P_NI" = yes ]   && ni="yes"
    [ "$P_FOFF" = yes ] && foff="yes"
    [ -n "$P_CAN" ]     && can="$P_CAN"
    loc="$P_LOC"
    case "$st" in 30*) hop=$((hop+1)); [ "$hop" -gt 2 ] && break ;; *) break ;; esac
    [ -z "$loc" ] && break
    case "$loc" in http*) url="$loc" ;; /*) url="$BASE$loc" ;; *) break ;; esac
  done
  P_STATUS="$st"; P_NI="$ni"; P_FOFF="$foff"; P_CAN="$can"
}

path_of(){ local u="$1"; u="${u#*//}"; printf '/%s\n' "${u#*/}"; }

# ---------------------------------------------------------------- robots.txt / sitemap
ROBOTS=""; DIS=""; LOCS=""
load_robots(){ ROBOTS=$(curl -fsS --max-time "$TIMEOUT" "$BASE/robots.txt") || return 1; return 0; }
parse_dis(){
  DIS=$(printf '%s\n' "$ROBOTS" | tr -d '\r' | grep -iE '^[[:space:]]*disallow:' \
        | sed 's/^[^:]*:[[:space:]]*//' | sed 's/[[:space:]]*$//')
}
disallowed(){
  local u="$1" p
  [ -z "$DIS" ] && return 1
  printf '%s\n' "$DIS" | while IFS= read -r p; do
    [ -z "$p" ] && continue
    case "$u" in "$p"*) echo hit; break ;; esac
  done | grep -q hit
}
in_sitemap(){ printf '%s\n' "$LOCS" | grep -qxF "$1"; }

# ---------------------------------------------------------------- 主判定
REC=""   # url|noindex|canonical 逐行记录，供 S8 交叉校验
rec(){ REC="${REC}$1|$2|$3
"; }

run_matrix(){
  OK=0; BAD=0; WARN=0; REC=""
  echo "== 被测站点: $BASE    locale=$LOCALE"
  # 把**本次实际生效的入参**印出来（缺的印 `<缺>`）。为什么要多这一行：手抄的长命令里少一个
  # 参数或抄错一个 slug，症状会和"实现有缺陷"一模一样（都表现为某条断言红），而排查方向完全
  # 相反——一个改命令行，一个改代码。日志里带上入参，至少能让下一个人先看命令再怀疑实现。
  echo "== 入参: archived-moved=${ARCH_MOVED:-<缺>} archived-gone=${ARCH_GONE:-<缺>} archived-gone-from=${ARCH_GONE_FROM:-<缺>} moved-from=${MOVED_FROM:-<缺>} moved-to=${MOVED_TO:-<缺>} out-of-stock=${OOS:-<自动>}"
  echo "== 入参(规则4 负向半边): slug-history-locale=$SH_LOCALE slug-history-from=${SH_FROM:-<缺>} slug-history-to=${SH_TO:-<缺>} p404-a=${P404_A:-<缺>} p404-b=${P404_B:-<缺>} p404-c=${P404_C:-<缺>}"

  load_robots || { echo "PRECONDITION: 取不到 $BASE/robots.txt" >&2; return 2; }
  parse_dis

  local sm catpath prodpath
  sm=$(curl -fsS --max-time "$TIMEOUT" "$BASE/sitemap.xml") || { echo "PRECONDITION: 取不到 $BASE/sitemap.xml" >&2; return 2; }
  LOCS=$(printf '%s\n' "$sm" | grep -oE '<loc>[^<]+</loc>' | sed 's/<[^>]*>//g' | tr -d ' ')
  catpath=$(printf '%s\n' "$LOCS" | grep -E "/$LOCALE/c/[^?]+$" | head -1)
  prodpath=$(printf '%s\n' "$LOCS" | grep -E "/$LOCALE/products/[^?]+$" | head -1)
  catpath="${catpath#"$BASE"}"; prodpath="${prodpath#"$BASE"}"
  [ -n "$catpath" ]  || { echo "PRECONDITION: sitemap 里没有 /$LOCALE/c/ 分类页" >&2; return 2; }
  [ -n "$prodpath" ] || { echo "PRECONDITION: sitemap 里没有 /$LOCALE/products/ 商品页" >&2; return 2; }

  # ---- S1 私有页：noindex 存在、follow 未关闭
  echo "-- S1 私有页 noindex,follow"
  local p
  for p in "/$LOCALE/cart" \
           "/$LOCALE/checkout" \
           "/$LOCALE/checkout/result?orderNo=OS-241101-AAAAAA&token=x" \
           "/$LOCALE/account" \
           "/$LOCALE/account/login" \
           "/$LOCALE/account/orders/OS-241101-AAAAAA" \
           "/$LOCALE/orders/track" \
           "/setup/1" \
           "/$LOCALE/_preview?token=x" ; do
    probe_chain "$BASE$p"
    if [ "$P_STATUS" = "404" ] && [ "$p" = "/setup/1" ]; then
      pass "S1 $p 返回 404（安装已完成、向导已下线 = 合格状态）"
      continue
    fi
    if [ "${P_STATUS:0:1}" != "2" ]; then
      fail "S1 $p 站内链接不可达" "http=${P_STATUS}（可达是 S1 的前提）"; continue
    fi
    if [ "$P_NI" = "yes" ]; then pass "S1 $p 带 noindex"
    else fail "S1 $p 缺 noindex" "meta robots 与 X-Robots-Tag 都没命中 → 该私有页会被收录（a 类）"; fi
    if [ "$P_FOFF" = "yes" ]; then fail "S1 $p 关闭了 follow" "a 类要求 noindex,follow；nofollow 会切断站内权重传递"
    else pass "S1 $p follow 未关闭"; fi
    rec "$BASE$p" "$P_NI" "$P_CAN"
  done

  # ---- S2 私有页不得进 Disallow
  echo "-- S2 私有页未出现在 Disallow 中"
  for p in "/$LOCALE/cart" "/$LOCALE/checkout" "/$LOCALE/account" "/$LOCALE/orders/track" "/setup" ; do
    if disallowed "$p"; then
      fail "S2 $p 被 Disallow" "robots.txt 不得 Disallow 前台路径；当前清单=[$(printf '%s' "$DIS" | tr '\n' ' ')]"
    else pass "S2 $p 未被 Disallow"; fi
  done

  # ---- S3 筛选/排序页（含带筛选的第 2 页）→ canonical 指向无参数第 1 页
  echo "-- S3 筛选页 canonical → 无参数第 1 页（含筛选×分页交集）"
  probe_chain "$BASE$catpath"
  local catcanon="$P_CAN" catbody="$P_BODY" f1 f2 facet
  if [ -n "$catcanon" ]; then pass "S3 分类页第 1 页输出 canonical（基准=${catcanon}）"
  else fail "S3 分类页第 1 页无 canonical" "没有基准就无从判断归并目标"; fi

  facet=$(printf '%s' "$catbody" | grep -oiE 'href="[^"]*\?[^"]*f\.[^"]*"' | head -1 | cut -d'"' -f2)
  if [ -z "$facet" ]; then
    echo "PRECONDITION: $catpath 里找不到任何 f.* 筛选链接 —— fixture 需 ≥2 个 IsFilterable 属性（docs/01 §6 F4）" >&2
    return 2
  fi
  case "$facet" in http*) f1="$facet" ;; *) f1="$BASE$facet" ;; esac

  probe_chain "$f1"
  [ "$P_STATUS" = "200" ] && pass "S3 筛选页允许抓取（200）" || fail "S3 筛选页不可达" "http=$P_STATUS url=$f1"
  if [ "$P_NI" = "yes" ]; then fail "S3 筛选页出现 noindex" "b1 档必须被收录、只做 canonical 归并；加 noindex 等于整批页面放弃收录"
  else pass "S3 筛选页无 noindex"; fi
  if [ -n "$catcanon" ] && same "$P_CAN" "$catcanon"; then pass "S3 筛选页第 1 页 canonical → 无参数分类页"
  else fail "S3 筛选页第 1 页 canonical 不指向无参数分类页" "实际=${P_CAN:-<空>} 期望=$catcanon url=$f1"; fi
  rec "$f1" "$P_NI" "$P_CAN"

  case "$f1" in *\?*) f2="$f1&page=2" ;; *) f2="$f1?page=2" ;; esac
  probe_chain "$f2"
  if [ -n "$catcanon" ] && same "$P_CAN" "$catcanon"; then pass "S3 筛选+第 2 页 canonical → 无参数第 1 页（交集规则）"
  else fail "S3 筛选+第 2 页 canonical 指错" "实际=${P_CAN:-<空>} 期望=${catcanon}（不得自指、也不得指向 ?page=2）url=$f2"; fi
  if [ "$P_NI" = "yes" ]; then fail "S3 筛选+第 2 页出现 noindex" "同属 b1 档，该收录"; else pass "S3 筛选+第 2 页无 noindex"; fi
  rec "$f2" "$P_NI" "$P_CAN"

  # ---- S4 无筛选的第 2 页起 canonical 自引用
  echo "-- S4 无筛选分页 canonical 自引用"
  local p2="$BASE$catpath?page=2"
  probe_chain "$p2"
  if same "$P_CAN" "$p2"; then pass "S4 无筛选第 2 页 canonical 自引用"
  else fail "S4 无筛选第 2 页 canonical 不自指" "实际=${P_CAN:-<空>} 期望=${p2}（指回第 1 页会与第 1 页的归并族不自洽）"; fi
  if [ "$P_NI" = "yes" ]; then fail "S4 无筛选第 2 页出现 noindex" "分页页该被收录"; else pass "S4 无筛选第 2 页无 noindex"; fi
  rec "$p2" "$P_NI" "$P_CAN"

  # ---- S5 商品详情页（含 utm）canonical 自引用
  echo "-- S5 商品页 canonical 自引用、剔除追踪参数"
  probe_chain "$BASE$prodpath"
  if [ -n "$P_CAN" ]; then pass "S5 商品页输出 canonical（${P_CAN}）"
  else fail "S5 商品页无 canonical" "b3 档要求永远自引用；AC-10 也要求在原始 HTML 里能 grep 到"; fi
  local pu="$BASE$prodpath?utm_source=news&utm_medium=email&utm_campaign=spring"
  probe_chain "$pu"
  if same "$P_CAN" "$BASE${prodpath}"; then pass "S5 带 utm 时 canonical 自引用且不含追踪参数"
  else fail "S5 带 utm 的 canonical 不对" "实际=${P_CAN:-<空>} 期望=$BASE${prodpath}（b3 永不做参数归并）"; fi
  rec "$pu" "$P_NI" "$P_CAN"

  # ---- S5b 非追踪参数必须在 canonical 里被保留 -------------------------------
  # **为什么这条不能省**：S5 只喂 `?utm_*`，而"只剔命中 TAG 的参数"（正确）与
  # "剔光所有查询参数"（错）对同一个 utm 请求吐出**逐字相同**的 canonical——
  # S5 永远不会因为"剔过头"而红。docs/04 的 b3 反例句点名的后果正是
  # 「变体/来源流量全指向同一页」，所以 C 用 `ref`（非追踪、无业务语义）
  # 而不是臆造一个 `?variant=`：测的是 spec 已写的那句"**只**剔除追踪参数"。
  local pref="$BASE$prodpath?ref=abc"
  probe_chain "$pref"
  if same "$P_CAN" "$pref"; then pass "S5b 非追踪参数被保留（只剔追踪参数，未剔光）"
  else fail "S5b canonical 把非追踪参数剔掉了" "实际=${P_CAN:-<空>} 期望=${pref}（只许剔 utm_*/gclid/fbclid/msclkid/yclid；剔光等于把变体/来源流量归并成一页）"; fi
  rec "$pref" "$P_NI" "$P_CAN"

  # ---- S6 站内搜索：noindex,follow + canonical 不跨 URL + 不 Disallow + 真渲染
  #
  # ⚠️ **这一格的旧写法是「有 canonical 就红」，已作废**——它和本文件 S8 直接矛盾：
  #    同一份"输出自指 canonical 的搜索页"会同时得到 `S6 fail` 与 `S8 warned`，脚本对同一个实现
  #    给出两个相反结论。矛盾不在脚本，在**上游文档自己**：
  #      · docs/01 §6.1 的 S6 单元格 + 三步判别口诀第 1 步 + 反例③ 仍写「不输出 canonical」；
  #      · 同一张表的 S8 告警条、需求 #44/#68、docs/06 TC-SEO-09② 写的是「只禁**跨 URL**，
  #        自指是告警且**不得因此去删它**」。
  #    本脚本按后一组实现，因为旧口径会**误杀正常实现**（模板统一输出 canonical），而且那个假红
  #    会逼人去删 canonical——删完整页 `<head>` 也能满足旧口径（noindex 可只靠 X-Robots-Tag 给），
  #    于是断言变成"逼出更差的实现"。已在 docs/01 §6.1 登记待改（S6 单元格 / 口诀第 1 步 / 反例③
  #    三处），改完这整段注释就该删。
  echo "-- S6 站内搜索 noindex,follow / canonical 不跨 URL / 不 Disallow / 真渲染"
  local su="$BASE/$LOCALE/search?q=robot"
  probe_chain "$su"
  [ "$P_STATUS" = "200" ] && pass "S6 搜索页允许抓取（200）" || fail "S6 搜索页不可达" "http=$P_STATUS"
  if [ "$P_NI" = "yes" ]; then pass "S6 搜索页带 noindex"; else fail "S6 搜索页缺 noindex" "b2 档：不该收录 → noindex,follow"; fi
  if [ "$P_FOFF" = "yes" ]; then fail "S6 搜索页关闭了 follow" "要求 noindex,follow"; else pass "S6 搜索页 follow 未关闭"; fi
  # ② 闸门条件只能是「跨 URL」。缺省 canonical 与自指 canonical **都算合格**。
  if [ -z "$P_CAN" ] || ! is_cross_url "$P_CAN" "$su"; then
    pass "S6 搜索页 canonical 未跨 URL（缺省或自指）"
  else
    fail "S6 搜索页 canonical 指向其他 URL" "noindex 优先 → canonical 被忽略；多个 noindex 页并到同一目标还会反向污染目标页。缺省或自指都合格，只拦跨 URL 这一条边；实际=$P_CAN"
  fi
  # ③ 页面必须真渲染：结果条目，或「无结果」文案，二者之一。
  #    为什么必须有：少了它，上面"canonical 未跨 URL"的绿可以由**空壳页**达成（CSR 未 SSR、
  #    或反代把 5xx 改写成 200 静态页，见 docs/06 VQ14）。**不得写成"body 非空"**——空壳页的 body 也不空。
  #    已知局限（写清楚，别当它更强）：它判不出"结果集是否真的与 q 相关"，只判"渲染了 vs 空壳"；
  #    页脚若带"推荐商品"列表，空搜索结果也可能命中。它拦的是"整页没渲染"，不是"搜索逻辑错"。
  if printf '%s' "$P_BODY" | grep -qiE 'href=[^ >]*/products/[^ >]*'; then
    pass "S6 搜索页真渲染（有商品结果条目）"
  elif printf '%s' "$P_BODY" | grep -qiE '没有找到|未找到|无结果|no (result|results|match|matches)|nothing found|0 results'; then
    pass "S6 搜索页真渲染（无结果文案）"
  else
    fail "S6 搜索页是空壳" "200 但正文既没有商品结果条目、也没有「无结果」文案：noindex 那条的绿可以由空壳页达成，空壳页一旦被索引，用户点进去什么都没有（docs/06 VQ14 / TC-SEO-09③）"
  fi
  if disallowed "/$LOCALE/search"; then fail "S6 搜索页被 Disallow" "b2 不用 Disallow（挡不住收录，只让 noindex 读不到）"
  else pass "S6 搜索页未被 Disallow"; fi
  rec "$su" "$P_NI" "$P_CAN"

  # ---- S7 robots.txt：Disallow 只有 /api，无查询串通配
  echo "-- S7 robots.txt Disallow 仅 /api、无 ?/&/*"
  local n=0 d
  while IFS= read -r d; do
    [ -z "$d" ] && continue
    n=$((n+1))
    if printf '%s' "$d" | grep -qxE '/api/?'; then
      pass "S7 Disallow ${d}（唯一合法项）"
    else
      fail "S7 Disallow $d 非法" "只允许 /api；前台路径一律不得 Disallow（docs/01 §6.1 S7）"
    fi
    case "$d" in
      *\?*|*\&*|*\**) fail "S7 Disallow $d 含查询串通配" "/*?* 会把 b1 筛选页与 b3 商品页一起挡掉" ;;
    esac
  done < <(printf '%s\n' "$DIS")
  [ "$n" -ge 1 ] && pass "S7 robots.txt 有 Disallow 段（$n 条）" || fail "S7 robots.txt 没有任何 Disallow" "至少应有 /api"

  # ---- S8 全局交叉校验：指令不得互相抵消
  echo "-- S8 交叉校验（Disallow / noindex / canonical 不得叠加）"
  local u ni can path
  while IFS='|' read -r u ni can; do
    [ -z "$u" ] && continue
    path=$(path_of "$u")
    if disallowed "$path"; then
      [ "$ni" = "yes" ] && fail "S8 $path 同时 Disallow + noindex" "指令互相抵消：爬虫读不到 noindex，页面仍以裸链接进结果"
      # 与下面 noindex 分支**同口径**——现在不是"两边写着一样的代码"，而是**同一个函数**。
      # 上一版这里是逐字比 `same`、下面是归一化比 `norm_url`，于是"自指但带 utm"的 canonical
      # 被报成外指，且病因指到"canonical 读不到"。抽成 `is_cross_url` 之后这种漂移结构上不可能。
      if [ -n "$can" ] && is_cross_url "$can" "$u"; then
        fail "S8 $path 同时 Disallow + 外指 canonical" "canonical 读不到 → 归并失效，白做；规范化后仍不等（实际=${can}）"
      fi
    fi
    if [ "$ni" = "yes" ] && [ -n "$can" ]; then
      if ! is_cross_url "$can" "$u"; then
        # 规范上冗余无害；禁全会逼实现方删掉正确的 canonical（模板统一输出是正常写法）
        warned "S8 $path noindex + 自指 canonical" "冗余但无害（规范只禁跨 URL）；规范化后不等则判红"
      else
        fail "S8 $path noindex + 跨 URL canonical" "看着自指但规范化后不等（尾斜杠/追踪参数/参数序）就是跨 URL；实际=$can"
      fi
    fi
  done < <(printf '%s' "$REC")
  [ "$BAD" -eq 0 ] && pass "S8 未发现互相抵消的指令组合"

  # ---- S9 归档商品：有替代分类 → 301；无替代 → 410（不是 404）
  echo "-- S9 归档商品页（301 / 410）"
  if [ -z "$ARCH_MOVED" ] || [ -z "$ARCH_GONE" ] || [ -z "$ARCH_GONE_FROM" ]; then
    # **文案要指名缺的是哪一条入参**。旧版三条合并成一句、并且只说"缺归档商品 fixture"，
    # 于是漏敲 `--archived-gone-from` 时读者会以为"站点没有历史 slug 数据"——这是把
    # **命令行用法错误**印成了**实现缺陷**。真读站点得到的 404 由下面 status 分支报，文案不同。
    echo "PRECONDITION: S9 的入参不全，本次**未验证**（不是站点错了、也不代表 SlugHistory 未实现）：" >&2
    [ -n "$ARCH_MOVED" ]    || echo "  · 缺 --archived-moved（归档且有替代分类的商品）" >&2
    [ -n "$ARCH_GONE" ]     || echo "  · 缺 --archived-gone（归档且无替代分类的商品）" >&2
    [ -n "$ARCH_GONE_FROM" ]|| echo "  · 缺 --archived-gone-from（上面那个商品改名前的历史 slug；**这条只是没给入参**，不用它推断实现是否存在）" >&2
    echo "  补齐后重跑；缺 fixture = 未验证 = AC-11 不通过（docs/01 §6.1 退出码语义）" >&2
    return 2
  fi
  _probe_one "$BASE$ARCH_MOVED"
  if [ "$P_STATUS" = "301" ]; then
    pass "S9 归档+有替代 → 301"
    local loc="$P_LOC"
    # ① Location 必须**绝对 URL 且以 $BASE 开头**。契约（00-architecture）：Location 一律绝对 URL、
    #   来源 PublicSiteUrl，禁止读请求 Host；归档 301 的 redirectTo 同样受这条约束。
    #   相对路径的 origin 由接收方决定，无法证明来源未被 Host 头影响 —— 正是 AC-10 禁止的形态。
    case "$loc" in
      "$BASE"*) pass "S9 301 的 Location 是绝对 URL（来源 PublicSiteUrl）" ;;
      "") fail "S9 301 缺 Location 头" "301 没有 Location：爬虫不知道该去哪，权重直接落空" ;;
      *) fail "S9 301 的 Location 不是绝对 URL" "契约要求 Location 一律绝对 URL 且来源 PublicSiteUrl（禁止读请求 Host）。相对路径的 origin 由接收方决定，无法证明来源未被 Host 头影响 —— AC-10 禁止的形态。实际=${loc}" ;;
    esac
    # ② 前缀只判 /$LOCALE/c/。**不收窄到具体某一层**：取最深哪一层归 fixture 级 E2E（W83），
    #   脚本钉死层级会把合法变通判红——而"多余约束让合法实现变红"的门禁最终会被关掉。
    case "${loc#"$BASE"}" in
      "/$LOCALE/c/"*) pass "S9 301 指向分类页（${loc#"$BASE"}）" ;;
      *) fail "S9 301 目标不是分类页" "Location=${loc}（应指向 /$LOCALE/c/…）" ;;
    esac
    # ③ 落点必须**再取一次**并断言 200。这一条只有一种合法结果（页面存在就是存在），
    #   与"取最深"不同，不能交给 E2E。
    #   为什么必须单列：301 指对了、目标页却 404 时，上面两条判据全绿 —— 只查 Location 字面永远发现不了。
    #   resolver 只保证"有替代分类就 301"，不保证该分类页真的可渲染（分类被删/被合并/层级算错），
    #   而"跳到一个 404"比不跳更糟：既丢权重又让 Search Console 记"重定向错误"。
    case "$loc" in
      "$BASE"*)
        _probe_one "$loc"
        if [ "$P_STATUS" = "200" ]; then
          pass "S9 301 落点可访问（200）"
        else
          fail "S9 301 落点不可访问（${P_STATUS}）" "301 指对了、目标页却 ${P_STATUS}：爬虫跟过去拿到错误页，权重无处落地。Location=${loc}"
        fi ;;
    esac
  else
    fail "S9 归档+有替代 未返回 301" "http=$P_STATUS url=$BASE${ARCH_MOVED}（302 是临时跳转，301 才传递权重）"
  fi
  _probe_one "$BASE$ARCH_GONE"
  if [ "$P_STATUS" = "410" ]; then pass "S9 归档+无替代 → 410 Gone"
  elif [ "$P_STATUS" = "404" ]; then
    fail "S9 归档+无替代 返回了 404" "必须是 410：404 会被当成'可能回来'反复重试，410 才被更快移除"
  else
    fail "S9 归档+无替代 状态码错" "http=${P_STATUS}（期望 410）url=$BASE$ARCH_GONE"
  fi

  # 历史 slug（商品改名前的旧地址）：也必须 410，**不得** 301 到新 slug。
  # 为什么钉这一格：规则 4「slug 变更 → 301 到新 slug」与 (b3)「归档 → 410」各自都对，
  # 合起来若按"先查历史、再判状态"实现，老外链会走 301 → 410 两跳，
  # Search Console 报"重定向错误"，比直接 410 更慢摘除。正确顺序是**先按 slug 解析出实体，再按实体当前状态分派**。
  # 每次 _probe_one 都重新发请求、不复用上面的 P_*，所以与"301 落点必须 200"那条断言不会互相污染。
  _probe_one "$BASE$ARCH_GONE_FROM"
  # 先判 Location 再判状态码：带 Location 就是"两跳"的直接痕迹，比状态码更能归因
  # （301 → 410 两跳里，第一跳的 Location 才是病因；只看最终状态码会把病因写在"状态码错"上）。
  if [ -n "$P_LOC" ]; then
    fail "S9 历史 slug 不得带 Location" "响应带了 Location=${P_LOC}：说明走了\"先查历史、后判状态\"，形成 301 → 410 两跳。实体当前状态必须优先于入口路径，正确顺序是\"先按 slug 解析出实体 → 再按实体当前状态分派\""
  else
    pass "S9 历史 slug 未带 Location（无两跳痕迹）"
  fi
  case "$P_STATUS" in
    410) pass "S9 归档商品的历史 slug → 410（不跳转）" ;;
    301) fail "S9 历史 slug 被 301 到新 slug" "形成 301 → 410 两跳：爬虫读到'重定向错误'，比直接 410 更慢摘除。正确顺序是先解析实体、再按状态分派" ;;
    404) fail "S9 历史 slug 返回了 404" "必须是 410（404 会被当成'可能回来'反复重试）url=$BASE$ARCH_GONE_FROM" ;;
    *)   fail "S9 历史 slug 状态码错" "http=${P_STATUS}（期望 410，且不得 301）url=$BASE$ARCH_GONE_FROM" ;;
  esac

  # ---- S9 规则 4：在售商品改过 slug → 旧 URL 必须 301 到新 URL
  #
  # 补的是「**规则 4 零覆盖**」，**不是**"禁止项有洞"。这一点我第一版写错了，独立校验用旧版
  # 脚本做变异实验（把历史 slug 的 mock 响应 410 → 404）证伪：旧版 S9 在 Location 那条禁止项
  # 之外**还有**一条 `404) fail "S9 历史 slug 返回了 404"`，所以 SlugHistory 完全不实现时它会红。
  # **禁止项旁边那条状态码分支，本来就把"什么都没做"挡住了**——写禁止项时必须配状态码分支，
  # 这正是上一条当初写对的地方，别把它的功劳记在别处。
  #
  # 真正的缺口是：S9 原有三个对象**全是 `Archived` 实体**，而规则 4 讲的是"slug 修改后 301 到
  # 新 slug"——**在售商品这一侧原本根本不在扫描面上**（旧版 mock 里连一个"在售且改过 slug"的
  # 商品都没有）。`docs/01` #89 曾把它的载体登记为"`docs/06` 的集成用例"，而那条用例从未存在
  # ——属于"**登记了载体、载体不存在**"，不是"判据写得不严"。
  #
  # 成对的意义（这个表述是准确的）：两个对象**入口形态完全相同**（都是 SlugHistory 里的旧地址、
  # 走同一个 resolver），唯一差别是实体当前状态（Active / Archived）。同一个 if/else 的两侧
  # 都在场时，**任一侧写错都会有一侧红**：把"命中历史就 410"写死 → 在售侧红；写死 301 → 归档侧红。
  # 分裂成两个编号反而做不到这一点——**两份档案可以各自对、合起来错**。
  echo "-- S9 规则4 slug 变更（在售：旧 URL → 301 新 URL）"
  if [ -z "$MOVED_FROM" ] || [ -z "$MOVED_TO" ]; then
    echo "PRECONDITION: 缺改过 slug 的在售商品 fixture（--moved-from / --moved-to 两个都要给）。缺 fixture = 未验证 = AC-11 不通过" >&2
    return 2
  fi
  _probe_one "$BASE$MOVED_FROM"
  case "$P_STATUS" in
    301) pass "S9 规则4 旧 slug → 301" ;;
    200) fail "S9 规则4 旧 slug 仍返回 200" "旧地址还在提供内容 = 同一商品两个 URL 并存 = 重复内容，canonical 也救不了（两个 URL 都自指）。必须 301 到新 slug" ;;
    404) fail "S9 规则4 旧 slug 返回 404" "两种可能，先分清楚再改：① SlugHistory 未实现或未登记 —— 改过 slug 的在售商品，旧外链全部断掉（外部站点与书签指向的地址一律 404，且不会自己恢复）；② **这个路径是手抄错的**（抄错也一样 404，而且和 ① 在输出里长得一模一样）。核对上面那行『入参』再动手。必须 301 到新 slug" ;;
    *)   fail "S9 规则4 旧 slug 状态码错" "http=${P_STATUS}（期望 301）url=$BASE${MOVED_FROM}" ;;
  esac
  if [ "$P_STATUS" = "301" ]; then
    case "$P_LOC" in
      "$BASE"*) pass "S9 规则4 301 的 Location 是绝对 URL（来源 PublicSiteUrl）" ;;
      "") fail "S9 规则4 301 缺 Location 头" "301 没有 Location：爬虫不知道该去哪，权重直接落空" ;;
      *) fail "S9 规则4 301 的 Location 不是绝对 URL" "契约要求 Location 一律绝对 URL 且来源 PublicSiteUrl（禁止读请求 Host）。实际=${P_LOC}" ;;
    esac
    # 目标必须**就是新 slug**，不是"某个 200 的页面"：跳到别处等于把已积累的权重指向错的页面，
    # 而落点照样 200、日志里看不出任何异常。尾斜杠两侧都算对（那是同一个 URL 的两种写法）。
    case "${P_LOC#"$BASE"}" in
      "${MOVED_TO%/}"|"${MOVED_TO%/}/") pass "S9 规则4 301 指向新 slug（${MOVED_TO}）" ;;
      *) fail "S9 规则4 301 目标不是新 slug" "Location=${P_LOC}（期望 ${MOVED_TO}）。跳到别处时落点可能仍返回 200，只有这条能发现" ;;
    esac
    case "$P_LOC" in
      "$BASE"*)
        _probe_one "$P_LOC"
        if [ "$P_STATUS" = "200" ]; then
          pass "S9 规则4 301 落点可访问（200）"
        else
          fail "S9 规则4 301 落点不可访问（${P_STATUS}）" "301 指对了、目标页却 ${P_STATUS}：旧外链跟过去拿到错误页，权重无处落地。Location=${P_LOC}"
        fi ;;
    esac
  fi

  # ---- S9 规则 4 的**负向半边**：未登记旧 slug / 语种串台 ----------------------
  #
  # 语义唯一出处是 docs/06「规则 4 的负向半边已有固定值，且必须四断言一组」：
  #   ① /en/products/fx-kettle      → 200（正向锚）
  #   ② /en/products/fx-kettle-v1   → 301 + Location 逐字 + **落点再取一次 200**
  #   ③ /zh/products/fx-kettle-v1   → 404（N1：查历史表漏了 Locale）
  #   ④ /en/products/fx-kettle-v2   → 404（N2：近似匹配）
  #
  # ⚠️ **编号别照抄**：docs/06 的 ③ 是语种串台，本块的 ③ 是"三条近似串"——
  #    因为 N2 的坏法不止一种（剥 `-vN` 后缀 / 版本号 +1 / 前缀模糊），**一条串只抓一种**，
  #    所以把 ④ 拆成三条各自独立的断言，语种串台顺延成最后一条。
  #
  # 不新开 S 编号：docs/01 §6.1 的矩阵是"六档 × S1~S11"，新增 S 要同时改矩阵，而矩阵不归本脚本。
  # 这块是规则 4 的另一侧（"什么不该跳"），挂在 S9 下语义才对。
  #
  # **为什么这四条必须相邻在同一个块里**（docs/06 原话："单独一条 404 断言不可证伪"）：
  # 只写"X → 404"，一个**全站 404** 的实现会全绿。正向锚①的作用就是把那种绿挡住 ——
  # 解析链整体不通时，下面每一条"期望 404"都会自动成立，那种绿一个信息量都没有。
  echo "-- S9 规则4 负向半边（未登记旧 slug / 语种串台）"
  if [ -z "$SH_FROM" ] || [ -z "$SH_TO" ] || [ -z "$P404_A" ] || [ -z "$P404_B" ] || [ -z "$P404_C" ]; then
    echo "PRECONDITION: 缺未登记旧 slug / SlugHistory fixture（--slug-history-from/--slug-history-to/--p404-a/b/c 五个都要给）。缺 fixture = 未验证 = AC-11 不通过" >&2
    return 2
  fi
  # 语种串台的探针 = 把 $SH_FROM 首段的语种换成 $LOCALE。两者相同时派生出来的就是 $SH_FROM 本身，
  # "期望 404"会变成对一条**已知 301 的地址**要 404 —— 恒红，且病因被指向实现。宁可 rc=2。
  if [ "$LOCALE" = "$SH_LOCALE" ]; then
    echo "PRECONDITION: --locale 与 --slug-history-locale 相同（都是 ${LOCALE}），无法派生语种串台探针（会退化成对同一条地址的重复断言）" >&2
    return 2
  fi
  local xover p anchor_ok=1
  case "$SH_FROM" in
    "/$SH_LOCALE/"*) xover="/$LOCALE/${SH_FROM#"/$SH_LOCALE/"}" ;;
    *) xover="" ;;
  esac
  if [ -z "$xover" ]; then
    # 派生错比不派生更糟：会去探一个**别的**地址，而那个 404 是假绿（探针本身不存在，
    # 地址里任何一处抄错都返回 404）。所以形状不对就退 2，不猜。
    echo "PRECONDITION: --slug-history-from 必须以 /$SH_LOCALE/ 开头（实际 ${SH_FROM}），否则派生不出语种串台探针" >&2
    return 2
  fi

  # ① 正向锚。**必须是这一组的第一条。**
  _probe_one "$BASE$SH_TO"
  if [ "$P_STATUS" = "200" ]; then
    pass "S9 规则4 正向锚 $SH_TO 返回 200"
  else
    anchor_ok=0
    fail "S9 规则4 正向锚 $SH_TO 返回 $P_STATUS" "整条解析链不通（含全站 404）。这一条不绿，下面三条 404 全无意义"
  fi

  # ② 历史行生效 + 落点。三条失败文案各自指向不同病因（没跳 / 跳错 / 跳到死链）。
  _probe_one "$BASE$SH_FROM"
  if [ "$P_STATUS" = "301" ]; then
    pass "S9 规则4 未登记前的历史地址 $SH_FROM → 301"
  else
    fail "S9 规则4 未登记前的历史地址 $SH_FROM 未 301" "http=${P_STATUS}（期望 301）。$SH_LOCALE 这一侧的历史行必须单跳：404 = SlugHistory 没生效或没登记（旧外链全断）；200 = 同一商品两个 URL 并存（重复内容，canonical 两面都自指，救不了）"
  fi
  if [ "$P_STATUS" = "301" ]; then
    # 比较**必须**走 norm_url（尾斜杠 / 追踪参数 / 参数序），不新写一套逐字比较：
    # 同一份"指向哪一页"的判断在本文件里只有一个出口（is_cross_url/norm_url），
    # 各写一遍就会漂成两套口径 —— 那正是 S8 上一版的病。
    if same "$(norm_url "$P_LOC")" "$(norm_url "$BASE$SH_TO")"; then
      pass "S9 规则4 301 目标等于 ${SH_TO}（规范化后相等）"
    else
      fail "S9 规则4 301 目标不是 $SH_TO" "Location=${P_LOC:-<空>}（期望 $BASE$SH_TO 规范化后相等）。跳到别处时落点可能仍返回 200，只有这条能发现"
    fi
    case "$P_LOC" in
      "$BASE"*)
        _probe_one "$P_LOC"
        if [ "$P_STATUS" = "200" ]; then
          pass "S9 规则4 301 落点可访问（200）"
        else
          fail "S9 规则4 301 落点不可访问" "301 指对了、目标页却 ${P_STATUS}：旧外链跟过去拿到错误页，权重无处落地。Location=${P_LOC}"
        fi ;;
    esac
    # 上面这条"落点再取一次"**在今天与 ① 正向锚是同一个 URL 的同一个请求**：
    # 前一条断言已经要求 `Location` 规范化后等于 `$SH_TO`，而 ① 探的就是 `$BASE$SH_TO`。
    # 于是它不可能出现"① 绿而落点红"——`docs/02-数据模型.md §7.3.4` 说 ①/② "归因不同"，
    # 那句话对**"301 没发生"**这一半成立（② 红 ⇒ 坏在历史查找），对**落点**这一半不成立。
    # （写全文件名而不是 `docs/02`：本仓有两个 `02-*` 文件，短名会被 doc_anchors
    #   解析到 `docs/02-数据模型-M1落地对照表.md` 上去，而那个文件没有 §7.3.4。）
    #
    # 保留它的理由不是覆盖，是**防御**：一旦将来 Location 的规则放宽（允许指向同一实体的
    # 另一个合法 URL），这条立刻变成唯一的落点检查。删掉它省一次 curl，代价是那一天
    # 没有任何东西会发现"301 指到一个 404"。
    # 对应地，自证 `v26` 的说明写的是"证明这条断言存在且会红"，**不是**"去掉它以后
    # 只有它能抓"——后者在本 fixture 下不成立（落点 404 必然连带锚 404，两者同一个 URL）。
  fi

  # ③ 三条"近似但从未存在"的串，**逐条 404**。
  #    · 不许 `break`、不许"其中一条 404 就算过"：三条串各贴一种启发式（剥 `-vN` / 版本号 +1 /
  #      前缀模糊），只断言其中一条 = 只防一种。自证 v21/v22/v23 就是为了证明它们**各自独立**。
  #    · 这三条**由调用方显式给**（理由见文件头：判据自己生成的被检对象，判据无法判断其合法性）。
  #    · 正向锚红着 → 这三条 404 无信息量（全站 404 也能让它们全绿）。此时**不记 pass**，
  #      记 WARN 并点名"未验证"：一条静默的绿比一条红更坏。
  if [ "$anchor_ok" = "0" ]; then
    warned "S9 规则4 未登记旧 slug 三条（${P404_A} / ${P404_B} / ${P404_C}）" "未验证：正向锚红着，全站 404 也能让这三条全绿，因此本次不作为通过"
  else
    for p in "$P404_A" "$P404_B" "$P404_C"; do
      _probe_one "$BASE$p"
      if [ "$P_STATUS" = "404" ]; then
        pass "S9 规则4 未登记旧 slug $p → 404"
      else
        fail "S9 规则4 未登记旧 slug $p 返回 $P_STATUS" "三种近似各抓一种启发式：去 -vN 后缀 / 版本号 +1 / 前缀模糊。只断言其中一条 = 只防一种"
      fi
    done
  fi

  # ④ 语种串台：同一旧地址换到另一个语种，必须 404。
  #    **只有这条能发现"查历史表的谓词漏了 Locale 列"**：唯一索引是三元组，漏一列不报错、
  #    照样命中，而 Location 的域与形状**全对**（那个语种里新地址真的存在）——
  #    所以 ② 的"301 落点返回 200"对它是**全绿**。这是在售侧唯一能看见语种串台的探针。
  p="$xover"
  if [ "$anchor_ok" = "0" ]; then
    warned "S9 规则4 语种串台 $p" "未验证：正向锚红着，本次不作为通过"
  else
    _probe_one "$BASE$p"
    if [ "$P_STATUS" = "404" ]; then
      pass "S9 规则4 语种串台 $p → 404"
    else
      fail "S9 规则4 语种串台 $p 返回 $P_STATUS" "历史行只登记 ${SH_LOCALE}；谓词漏了 Locale 列照样命中，而 Location 的域与形状全对，只有这条能发现"
    fi
  fi

  # ---- S10 在售但库存 0：200 + OutOfStock + 留在 sitemap；归档不在 sitemap
  echo "-- S10 缺货商品页（200 + OutOfStock + 留在 sitemap）"
  if [ -z "$OOS" ]; then
    local cand
    for cand in $(printf '%s\n' "$LOCS" | grep -E "/$LOCALE/products/[^?]+$" | head -40); do
      cand="${cand#"$BASE"}"
      _probe_one "$BASE$cand"
      if printf '%s' "$P_BODY" | grep -qiE 'availability[^,}]{0,40}OutOfStock'; then OOS="$cand"; break; fi
    done
  fi
  if [ -z "$OOS" ]; then
    echo "PRECONDITION: 找不到'在售但库存 0'的商品（sitemap 前 40 个商品里没有 availability=OutOfStock）。缺 fixture = 未验证 = AC-11 不通过" >&2
    return 2
  fi
  _probe_one "$BASE$OOS"
  if [ "$P_STATUS" = "200" ]; then pass "S10 缺货商品页返回 200"
  else fail "S10 缺货商品页状态码错" "http=${P_STATUS}（缺货 ≠ 下架：404/410 会把还有货在卖的入口自己关掉）url=$BASE$OOS"; fi
  if printf '%s' "$P_BODY" | grep -qiE 'availability[^,}]{0,40}OutOfStock'; then pass "S10 结构化数据 availability=OutOfStock"
  else fail "S10 结构化数据未声明 OutOfStock" "缺了它搜索引擎把'缺货'当'有货'，用户点进去发现买不了"; fi
  if [ "$P_NI" = "yes" ]; then fail "S10 缺货商品页出现 noindex" "缺货商品还在卖、还有排名价值，必须允许收录"
  else pass "S10 缺货商品页无 noindex"; fi
  if in_sitemap "$BASE$OOS"; then pass "S10 缺货商品仍留在 sitemap"
  else fail "S10 缺货商品被从 sitemap 摘掉" "缺货 ≠ 下架；摘掉等于自愿放弃该页面的收录"; fi
  if in_sitemap "$BASE$ARCH_MOVED" || in_sitemap "$BASE$ARCH_GONE" || in_sitemap "$BASE$ARCH_GONE_FROM"; then
    fail "S10 归档商品仍出现在 sitemap 里" "已归档商品必须从 sitemap 移除（否则爬虫反复抓 410）"
  else pass "S10 归档商品不在 sitemap"; fi

  # ---- S11 /admin 的 X-Robots-Tag（c 档）
  echo "-- S11 /admin 的 X-Robots-Tag（c 档）"
  probe_chain "$BASE/admin/"
  if [ "${P_STATUS:0:1}" = "2" ]; then pass "S11 /admin/ 可达（${P_STATUS}）"
  else fail "S11 /admin/ 不可达" "http=${P_STATUS}（可达才能验头）"; fi
  if printf '%s\n' "$P_HDR" | grep -qiE '^x-robots-tag:.*noindex'; then
    pass "S11 /admin/ 带 X-Robots-Tag: noindex"
  else
    fail "S11 /admin/ 缺 X-Robots-Tag: noindex" "admin 是静态产物、没有应用层能加头，必须由 Caddy 在 /admin 的 handle 内注入；漏了就是登录页被收录"
  fi
  if disallowed "/admin"; then fail "S11 /admin 被 Disallow" "c 档要允许抓取 + noindex"; else pass "S11 /admin 未被 Disallow"; fi

  echo "== $OK passed, $BAD failed, $WARN warned"
  [ "$BAD" -eq 0 ] || return 1
  return 0
}

# ---------------------------------------------------------------- fail-before-pass 自证
# 一个只看退出码的闸门等于没有闸门。这里用本地 mock 站点证明两件事：
#   ① 正确样本 → 绿；
#   ② 每个注入 → 必须红，**且必须红在预期的那条断言上**（红在别处 = 没拦住，只是碰巧非零退出）。
self_test(){
  command -v python3 >/dev/null 2>&1 || { echo "self-test 需要 python3（仅自证用）" >&2; return 2; }
  local tmp; tmp=$(mktemp -d)
  cat > "$tmp/mock.py" <<'PY'
import http.server, socketserver, os, urllib.parse

MODE = os.environ.get("MODE", "ok")
PORT = int(os.environ.get("PORT", "8000"))
BASE = "http://127.0.0.1:%d" % PORT
CATP  = "/zh/c/robot-parts"
PRODP = "/zh/products/robot-arm"     # 在售有货
OOSP  = "/zh/products/sold-out"      # 在售、库存 0
ARCHM = "/zh/products/old-sku"       # 归档、有替代分类
ARCHG = "/zh/products/dead-sku"      # 归档、无替代
ARCHGF = "/zh/products/dead-sku-v1"  # 上面那个商品的历史 slug（改名前的旧地址），也必须 410
MVDT  = "/zh/products/kettle"        # 在售商品，改过 slug
MVDF  = "/zh/products/kettle-v1"     # 它改名前的旧地址，必须 301 到 MVDT
# 规则 4 的**负向半边**（docs/06「四断言一组」的 ①②③④）。
# 这一组刻意用 /en，而 --locale 是 zh：正向/历史/串台三个位置必须落在**不同语种**上，
# 否则语种串台那条探针派不出来（会退化成对同一条地址的重复断言）。
EN_TO   = "/en/products/fx-kettle"      # 现 slug：正向锚 + 301 目标
EN_FROM = "/en/products/fx-kettle-v1"   # 登记过的历史 slug（同一商品的旧地址）
P404A   = "/en/products/fx-mug-v1"      # 剥掉 -vN 就归当前商品（fx-mug）
P404B   = "/en/products/fx-kettle-v2"   # 版本号 +1 归当前 slug（N2；docs/06：**永不写进 SlugHistory**）
P404C   = "/en/products/fx-kettl"       # 前缀模糊 / 拼写纠正
LOCMISS = "/zh/products/fx-kettle-v1"   # 漏 Locale：同一旧地址换到 zh，必须 404
NOI = ["/zh/cart", "/zh/checkout", "/zh/account", "/zh/orders/track",
       "/setup", "/zh/_preview"]   # 前缀匹配，同 NOINDEX_PATTERNS
# 站内搜索页**单列一条分支**：它要多渲染一份结果列表（TC-SEO-09③ 要求"页面必须真渲染"），
# 而 NOI 那一段只吐一个 noindex 空壳 —— 那正好是 ③ 要拦的形态，不能复用。
SEARCH = "/zh/search"

def canon(p):   return '<link rel="canonical" href="%s%s">' % (BASE, p)
def noindex():  return '<meta name="robots" content="noindex,follow">'
def page(head): return "<!doctype html><html><head>%s</head><body></body></html>" % head
def ld(avail):
    return ('<script type="application/ld+json">{"@type":"Product","offers":'
            '{"@type":"Offer","availability":"https://schema.org/%s"}}</script>' % avail)

def route(path, q):
    if path == "/robots.txt":
        if MODE == "v1":
            return 200, "text/plain", "User-agent: *\nDisallow: /api\nDisallow: /*?*\n", {}
        if MODE == "v18":
            # 注入：给一条**前台路径**加 Disallow。它必须**同时红在三处**（S2 / S7 / S8）——
            # "一个注入命中多条判据、且每条都能各自点名"这件事本身需要样本来验，只造
            # "一个注入红一条"的样本，验不出"红在预期那条"是不是碰巧。
            return 200, "text/plain", "User-agent: *\nDisallow: /api\nDisallow: /zh/cart\n", {}
        return 200, "text/plain", "User-agent: *\nDisallow: /api\n", {}
    if path == "/sitemap.xml":
        # 归档的两个都不在 sitemap。
        # v8：连缺货商品也摘掉 → 判据应当走 PRECONDITION 并返回 2。这条分支必须被自证覆盖：
        # 文档承诺"退出码 2 = 未验证、不得静默放过"，但没人跑过 rc=2，那句话就只是承诺。
        urls = [CATP, PRODP] if MODE == "v8" else [CATP, PRODP, OOSP]
        items = "".join("<url><loc>%s%s</loc></url>" % (BASE, u) for u in urls)
        return 200, "application/xml", '<?xml version="1.0" encoding="UTF-8"?><urlset>%s</urlset>' % items, {}
    if path.startswith("/admin"):
        return 200, "text/html", page(""), {"X-Robots-Tag": "noindex"}
    if path == ARCHM:
        if MODE == "v7":                           # 注入：归档有替代却返回 200
            return 200, "text/html", page(canon(ARCHM)), {}
        if MODE == "v9":                           # 注入：301 指向**不存在的分类页** → 落点 404
            return 301, "text/html", "", {"Location": BASE + "/zh/c/no-such-category"}
        return 301, "text/html", "", {"Location": BASE + CATP}
    if path == ARCHG:
        if MODE == "v6":                           # 注入：归档无替代返 404 而非 410
            return 404, "text/html", page(""), {}
        return 410, "text/html", page(""), {}
    if path == ARCHGF:
        if MODE == "v10":  # 注入：先查 SlugHistory 再判状态 → 历史 slug 被 301 到新 slug（301 → 410 两跳）
            return 301, "text/html", "", {"Location": BASE + ARCHG}
        return 410, "text/html", page(""), {}
    if path == MVDF:
        # 规则 4（在售侧）的三种坏法。它们各自红在**三条不同的断言**上（状态码 / 状态码 / 目标比对）：
        #   v13 404 = SlugHistory 干脆没实现（在售商品的旧地址断掉）
        #   v14 200 = 旧地址还活着（重复内容，canonical 两面都自指，救不了）
        #   v15 301 但指向别处（落点照样 200，只有"目标必须等于新 slug"能抓）
        if MODE == "v13":
            return 404, "text/html", page(""), {}
        if MODE == "v14":
            return 200, "text/html", page(canon(MVDF)), {}
        if MODE == "v15":
            return 301, "text/html", "", {"Location": BASE + "/zh/products/wrong-target"}
        return 301, "text/html", "", {"Location": BASE + MVDT}
    if path == MVDT:
        return 200, "text/html", page(canon(MVDT) + ld("InStock")), {}
    # ---- 规则 4 的负向半边（/en 一侧）
    if path == EN_TO:
        # v25：连正向锚都不通（"全站 404"那种实现）。
        # v26：301 指对了（就是这里），但落点 404。
        # **注意 v26 同时让正向锚红** —— 这不是注入写糙了，是结构决定的：落点就是锚本身
        # 这个 URL（② 要求 Location 规范化后等于 ①），所以"落点 404"必然"锚 404"。
        # 因此 v26 证明的是"落点那条断言存在、会红"，**不是**"去掉它以后只有它能抓"。
        if MODE in ("v25", "v26"):
            return 404, "text/html", page(""), {}
        return 200, "text/html", page(canon(EN_TO) + ld("InStock")), {}
    if path == EN_FROM:
        if MODE == "v25":                      # 注入：历史行干脆没生效（全站 404 的一部分）
            return 404, "text/html", page(""), {}
        return 301, "text/html", "", {"Location": BASE + EN_TO}
    if path in (P404A, P404B, P404C):
        # 三条近似串各自一个注入：证明它们是**三条独立断言**，不是"任意一条 404 就过"。
        # 若判据里把 for 写成短路（命中一条就 break），v21 只会红一条、v22/v23 照样绿。
        if (path, MODE) in ((P404A, "v21"), (P404B, "v22"), (P404C, "v23")):
            return 301, "text/html", "", {"Location": BASE + EN_TO}
        return 404, "text/html", page(""), {}
    if path == LOCMISS:
        if MODE == "v24":                      # 注入：查历史表的谓词漏了 Locale 列
            return 301, "text/html", "", {"Location": BASE + EN_TO}
        return 404, "text/html", page(""), {}
    if path == OOSP:
        if MODE == "v5":                           # 注入：库存 0 返 404
            return 404, "text/html", page(""), {}
        return 200, "text/html", page(canon(OOSP) + ld("OutOfStock")), {}
    if path == PRODP:
        # canonical 规则（b3）：永远自引用，**只剔除追踪参数**（不归并、不剔光）。
        # v16 注入的正是"剔光所有查询参数"——这是一条只喂 utm 的断言永远抓不到的坏法：
        # 对 `?utm=…` 请求，正确实现与全剔实现吐出**逐字相同**的 canonical。
        TAGK = ("gclid", "fbclid", "msclkid", "yclid")
        if MODE == "v16":
            keep = ""
        else:
            parts = []
            for k in sorted(q):
                if k.startswith("utm_") or k in TAGK:
                    continue
                for v in q[k]:
                    parts.append("%s=%s" % (k, v))
            keep = ("?" + "&".join(parts)) if parts else ""
        return 200, "text/html", page(canon(PRODP + keep) + ld("InStock")), {}
    if path == CATP:
        if any(k.startswith("f.") for k in q):
            if q.get("page"):
                if MODE == "v4":                   # 注入：筛选第 2 页 canonical 指 ?page=2
                    return 200, "text/html", page(canon(CATP + "?page=2")), {}
                return 200, "text/html", page(canon(CATP)), {}
            if MODE == "v2":                       # 注入：给筛选页加 noindex
                return 200, "text/html", page(canon(CATP) + noindex()), {}
            return 200, "text/html", page(canon(CATP)), {}
        if q.get("page"):
            if MODE == "v3":                       # 注入：无筛选第 2 页 canonical 指第 1 页
                return 200, "text/html", page(canon(CATP)), {}
            return 200, "text/html", page(canon(CATP + "?page=" + q["page"][0])), {}
        return 200, "text/html", page(canon(CATP) + '<a href="%s?f.interface=usb-c">USB-C</a>' % CATP), {}
    if path.startswith("/zh/c/") and path != CATP:
        # 不存在的分类页：v9 靠它证明"301 指对了、落点却 404"会被第 ③ 条断言抓住。
        # 若这里兜底返 200，v9 就永远绿 —— 那样新断言等于不存在。
        return 404, "text/html", page(""), {}
    if path == SEARCH:
        # 正确样本：noindex + **自指** canonical + 一条真实结果条目。
        #   · canonical 自指是刻意的：它让"缺省或自指都合格"这条口径在正样本里**被真的走到**
        #     （否则 S6 只走过 `-z` 那一支，`is_cross_url` 返回 1 的情形永远没人跑）；
        #   · 同时它会触发 S8 的一条 `warned`（noindex + 自指 canonical 冗余但无害）——
        #     那是裁定要求的行为，不是噪声。
        qs = "".join("?q=%s" % urllib.parse.quote_plus(v) for v in q.get("q", []))
        body = ('<ol class="search-results"><li><a href="%s%s">Robot arm</a></li></ol>'
                % (BASE, PRODP))
        head = noindex() + canon(SEARCH + qs)
        if MODE == "v19":
            # 注入：搜索页 canonical 指向别的 URL。S6 与 S8 **必须同时红**（两条判据同一件事）。
            head = noindex() + canon("/zh/c/robot-parts")
        if MODE == "v20":
            # 注入：200 但什么也没渲染（CSR 空壳 / 反代把 5xx 改写成 200 静态页，VQ14）。
            # 这条专门证明 TC-SEO-09③「页面必须真渲染」不是一条永远绿的断言。
            body = ""
        return 200, "text/html", page(head + body), {}
    if any(path.startswith(x) for x in NOI):
        if MODE == "v17" and path == "/zh/cart":
            # 注入：私有页丢了 noindex（a 类唯一的收录信号缺失）。证明 S1 会红。
            return 200, "text/html", page(""), {}
        return 200, "text/html", page(noindex()), {}
    return 200, "text/html", page(""), {}

class H(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    def log_message(self, *a): pass
    def do_HEAD(self): self.handle_all(True)
    def do_GET(self):  self.handle_all(False)
    def handle_all(self, head):
        u = urllib.parse.urlsplit(self.path)
        st, ctype, body, hdrs = route(u.path, urllib.parse.parse_qs(u.query))
        data = body.encode()
        self.send_response(st)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        for k, v in hdrs.items(): self.send_header(k, v)
        self.end_headers()
        if not head: self.wfile.write(data)

socketserver.TCPServer.allow_reuse_address = True
with socketserver.TCPServer(("127.0.0.1", PORT), H) as srv:
    srv.serve_forever()
PY
  echo "== fail-before-pass 自证（本地 mock，不连真站）"
  ARCHM="/zh/products/old-sku"; ARCHG="/zh/products/dead-sku"; ARCHGF="/zh/products/dead-sku-v1"; OOSF="/zh/products/sold-out"
  MVDF="/zh/products/kettle-v1"; MVDT="/zh/products/kettle"
  # 规则 4 负向半边的 fixture（与 mock 里的常量必须逐字一致；两处都是"固定值"，
  # 不是"随便起一个 404 的串"—— docs/06 已把 fx-kettle-v2 钉成"永不写进 SlugHistory"）。
  # 先 local、后赋值：`local X`（不带值）会把 X 置成**未赋值的局部变量**，在 `set -u`
  # 下就是 unbound —— 顺序写反的话下面每一行 "$SHL" 都会炸（实测踩过）。
  local SHL SHF SHT PA PB PC
  SHL="en"; SHF="/en/products/fx-kettle-v1"; SHT="/en/products/fx-kettle"
  PA="/en/products/fx-mug-v1"; PB="/en/products/fx-kettle-v2"; PC="/en/products/fx-kettl"
  local mode desc want want2 port rc out pid i sfail=0 ninj=0 first miss
  # 用 heredoc 逐行读，不用空格分隔的字符串——描述里有空格会被 for 拆散
  # 第 4 列 want2 可选：**一个注入命中多条判据**时，每条都必须各自被点名（v18/v19 用）。
  while IFS='|' read -r mode desc want want2; do
    [ -z "$mode" ] && continue
    port=$(python3 -c 'import socket;s=socket.socket();s.bind(("127.0.0.1",0));print(s.getsockname()[1]);s.close()')
    MODE="$mode" PORT="$port" python3 "$tmp/mock.py" >/dev/null 2>&1 &
    pid=$!
    i=0
    until curl -fsS --max-time 1 "http://127.0.0.1:$port/robots.txt" >/dev/null 2>&1; do
      i=$((i+1)); [ "$i" -gt 60 ] && break; sleep 0.1
    done
    BASE="http://127.0.0.1:${port}"
    ARCH_MOVED="$ARCHM"; ARCH_GONE="$ARCHG"; ARCH_GONE_FROM="$ARCHGF"; OOS="$OOSF"
    MOVED_FROM="$MVDF"; MOVED_TO="$MVDT"
    SH_LOCALE="$SHL"; SH_FROM="$SHF"; SH_TO="$SHT"; P404_A="$PA"; P404_B="$PB"; P404_C="$PC"
    # v8 故意不给 --out-of-stock，让判据自己去 sitemap 里找；v8 的 sitemap 里找不到。
    [ "$mode" = "v8" ] && OOS=""
    # v11：站点完全正常，只是没给"历史 slug"这个 fixture —— 必须走 PRECONDITION，
    # 而不是"没传就跳过这条断言"。后者正是"缺 fixture 变假绿"的同一条通道。
    [ "$mode" = "v11" ] && ARCH_GONE_FROM=""
    # v12：站点完全正常，只是没给"改过 slug 的在售商品"这对 fixture —— 同 v11 的理由：
    # 缺 fixture 必须是 rc=2，而不是"没传就跳过这一格"。
    [ "$mode" = "v12" ] && MOVED_FROM=""
    # `< /dev/null` 不是可选项：清零时 stdin 正是下面那个 CASES heredoc，
    # 判据里任何一处 read 都会静默吃掉后面的用例（表现为"少跑几条还报绿"）。
    run_matrix > "$tmp/out.$mode" 2>&1 < /dev/null; rc=$?
    out=$(cat "$tmp/out.$mode")
    if [ "$mode" = "ok" ]; then
      if [ "$rc" -eq 0 ]; then echo "  ok    样本[ok] 绿（$OK passed,$BAD failed,$WARN warned）"
      else echo "  FAIL  样本[ok] 应为绿却红了（rc=${rc}）"; printf '%s\n' "$out" | grep 'FAIL' | head -5; sfail=$((sfail+1)); fi
      # 正样本必须真的走到「noindex + 自指 canonical → 告警」那条分支。
      # 理由：一条从不出现的 WARN 分支，和它不存在在输出里是同一种观感；而裁定明确要求
      # "自指只告警、**不得因此去删 canonical**" —— 这个行为只能在正样本里被看见。
      # 它同时是"有人把自指改判成红"的探测器（那时 rc 会变 1，上面那条先红）。
      if [ "$WARN" -ge 1 ]; then
        echo "  ok    样本[ok] 里 noindex+自指 canonical 走到告警（$WARN 条）：自指不判红，符合裁定"
      else
        echo "  FAIL  样本[ok] 一条告警都没有：S8 的「自指 canonical」分支没被走到（被删了、被改判成红、或样本不再输出自指 canonical）"
        sfail=$((sfail+1))
      fi
    else
      ninj=$((ninj+1))
      first=$(printf '%s\n' "$out" | grep 'FAIL' | head -1 | sed 's/^  FAIL  //')
      if [ "$want" = "PRECONDITION" ]; then
        # 缺 fixture 必须是 2，不是 0 也不是 1：0 会让人以为"验过了且通过"，1 会被当成"真失败"去看红字，
        # 两者都错。只有 2 + 明确的 PRECONDITION 行才表示"这一档没验"。
        # **必须用 here-string，不能 `printf ... | grep -q`**：本脚本开了 `pipefail`，
        # grep -q 一命中就把读端关掉，printf 还在写就得到 "Broken pipe"、整条管线变非零 ——
        # 于是"确实打印了 PRECONDITION"反被判成失败（v8/v11/v12 三条假红，实测踩过）。
        if [ "$rc" -eq 2 ] && grep -q '^PRECONDITION' <<< "$out"; then
          echo "  ok    注入[$mode] $desc → rc=2 且打印 PRECONDITION（未验证不被当成绿）"
        else
          echo "  FAIL  注入[$mode] $desc → 期望 rc=2 + PRECONDITION，实际 rc=${rc}，首条红：${first:-（无）}"
          sfail=$((sfail+1))
        fi
      elif [ "$rc" -eq 1 ]; then
        # "红在预期那条"要逐条点名：want 必中，want2（若给了）也必须中。
        # 只判 `grep -q "$want"` 而允许多出一条红，会把"顺手也红了一条无关的"当成通过；
        # 但也不能要求"**只有**这一条红"——v18 那种一个注入命中三条是**正确**行为。
        miss=""
        printf '%s\n' "$out" | grep 'FAIL' > "$tmp/fails.$mode"
        grep -q -- "$want" "$tmp/fails.$mode" || miss="$want"
        if [ -n "$want2" ] && ! grep -q -- "$want2" "$tmp/fails.$mode"; then
          miss="${miss:+$miss 与 }$want2"
        fi
        if [ -z "$miss" ]; then
          echo "  ok    注入[$mode] $desc → 红在 ${want}${want2:+ 与 ${want2}}：$first"
        else
          echo "  FAIL  注入[$mode] $desc → 期望红在 ${want}${want2:+ 与 ${want2}}，实际 rc=${rc}，缺：${miss}，首条红：${first:-（无）}"
          sfail=$((sfail+1))
        fi
      else
        echo "  FAIL  注入[$mode] $desc → 期望 rc=1，实际 rc=${rc}，首条红：${first:-（无）}"
        sfail=$((sfail+1))
      fi
    fi
    kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
  # 注入编号空间说明（防止下一个人误以为跨文件同名同义）：
  #   v1~v26 是**本文件自证空间的编号**，与 docs/03 §9.2 的 V1~V12（后端/架构门禁注入，
  #   例如 V9 = storefront DTO 不泄露内部字段）**毫无关系**。跨文件同名不同义是允许的，
  #   但两边都必须写明，否则会有人以为"V9 已经有人做了"。
  #   v1~v16 都是「**值对不对**」型注入（canonical 指向谁、状态码是几、Location 落在哪）。
  #   v17~v20 补的是「**信号在不在**」型 —— 删一个 meta、加一条 Disallow、把正文留空。
  #   这类坏法比"构造一个指错的 canonical"简单得多，却是最容易漏测的一类：**一个永远绿的
  #   断言和一条不存在的断言，在输出里是同一种观感**。补之前 S1/S2/S6/S8 四档只有"正样本绿"。
  #   v9  = 归档 301 的落点返回 404（红在"落点可访问"那条，不是状态码那条）
  #   v10 = 归档商品的历史 slug 被 301 到新 slug（红在"不得带 Location"那条）
  #   v13~v15 = 规则 4 的三种坏法，各自红在不同的断言上（见 mock 里 MVDF 分支的注释）
  #   v18 = **一个注入命中三条判据**（S2 / S7 / S8），v19 = **一个注入命中两条**（S6 / S8）
  #         —— "红在预期那条"这种断言本身需要这种样本才验得出来（只造一对一，验不出"碰巧"）
  #   v21~v26 = 规则 4 负向半边（未登记旧 slug / 语种串台）的六个注入：
  #     v21/v22/v23 分别把三条近似串之一从 404 改成 301 → 证明这**三条是各自独立的断言**
  #       （判据若把 for 写成"命中一条就 break"，v21 只红一条、v22/v23 照样绿）；
  #     v24 = 历史行漏 Locale 列（语种串台），只有逐字比对语种前缀能发现；
  #     v25 = 全站 404（连正向锚都不通）→ 同时证明"锚红时那三条 404 不会被记成 pass"；
  #     v26 = 301 指对了但落点 404。**它不能证明"只断言第一跳会全绿"**：落点就是原锚
  #       那个 URL，所以这条注入必然同时红在"正向锚"上（实测两条 FAIL 并列）。它证明的是
  #       "落点那条断言存在、且会红" —— 删掉那条断言，本档的 `want` 字符串就不再出现，自证会红。
  #       想把"只判第一跳"单独关进笼子，得给 ② 换一个与 ① 不同的目标 URL，那要改 docs/02 的
  #       fixture 设计，不是改脚本能办的。
  #   v19 的 want2 是**逐字全长锚**（含 `?q=robot`）：S8 的失败文案本身带 S8 前缀，但 path 由
  #     `path_of` 算出、**含查询串**，所以更早的短锚 `noindex + 跨 URL canonical` 虽然能匹配，
  #     却松到"任何一行带这几个字的红"都算通过。收紧成整行之后，改探针 URL 的人会被迫
  #     回来改锚（这是好事：锚跟着断言走，不跟着实现走）。
  #   仍未覆盖的一支（写清楚，别当成已覆盖）：S8 的「Disallow + **外指** canonical」。
  #   进入它要求"前台路径被 Disallow"且"该页 canonical 外指"，而前者已由 S2/S7 判红 ——
  #   给它造样本等于给一条已知非法状态再造一个样本，收益为零；**比较本身的正确性由 v19 通过
  #   同一个谓词 `is_cross_url` 担保**（S6 与 S8 两条分支调的是同一个函数）。
  done <<'CASES'
ok|正确样本（六档全对）|-
v1|robots 加 Disallow:/*?*|S7
v2|给筛选页加 noindex|S3
v3|无筛选第 2 页 canonical 指第 1 页|S4
v4|筛选第 2 页 canonical 指 ?page=2|S3
v5|库存 0 的商品返 404|S10
v6|归档无替代返 404（而非 410）|S9 归档+无替代 返回了 404
v7|归档有替代返 200（而非 301）|S9 归档+有替代 未返回 301
v8|缺 out-of-stock fixture（sitemap 里找不到 OutOfStock 商品）|PRECONDITION
v9|归档 301 落点 404（Location 指向不存在的分类页）|S9 301 落点不可访问
v10|归档商品的历史 slug 被 301 到新 slug（301→410 两跳）|S9 历史 slug 不得带 Location
v11|缺 --archived-gone-from（历史 slug fixture 未给）|PRECONDITION
v12|缺 --moved-from（改过 slug 的在售商品 fixture 未给）|PRECONDITION
v13|在售商品的旧 slug 返 404（SlugHistory 未实现或未登记）|S9 规则4 旧 slug 返回 404
v14|旧 slug 仍返 200（同一商品两个 URL 并存）|S9 规则4 旧 slug 仍返回 200
v15|旧 slug 301 到错的页面（落点仍 200，只有目标比对能抓）|S9 规则4 301 目标不是新 slug
v16|canonical 剔光所有查询参数（不只剔追踪参数）|S5b canonical
v17|私有页 /zh/cart 丢了 noindex（a 类唯一的收录信号没了）|S1 /zh/cart 缺 noindex
v18|robots 给前台路径 /zh/cart 加 Disallow（一个注入命中 S2/S7/S8）|S2 /zh/cart 被 Disallow|S8 /zh/cart 同时 Disallow + noindex
v19|搜索页 canonical 指向别的 URL（S6 与 S8 必须同时红）|S6 搜索页 canonical 指向其他 URL|S8 /zh/search?q=robot noindex + 跨 URL canonical
v20|搜索页 200 但是空壳（结果条目与「无结果」文案都没有）|S6 搜索页是空壳
v21|未登记旧 slug 剥 -vN 后缀就归当前商品（应 404 却 301）|S9 规则4 未登记旧 slug /en/products/fx-mug-v1
v22|未登记旧 slug 版本号 +1 归当前 slug（应 404 却 301）|S9 规则4 未登记旧 slug /en/products/fx-kettle-v2
v23|未登记旧 slug 前缀模糊归当前 slug（应 404 却 301）|S9 规则4 未登记旧 slug /en/products/fx-kettl
v24|历史行漏 Locale 列 → 语种串台（应 404 却 301）|S9 规则4 语种串台
v25|全站 404：/en 商品页连正向锚都不通|S9 规则4 正向锚
v26|301 指对了但落点 404（落点与锚同一 URL，故必与正向锚同时红）|S9 规则4 301 落点不可访问
CASES

  # ---------------------------------------------------------------------------
  # 规则 4 负向半边的两个 PRECONDITION 也要各自被证一次（同 v11/v12 的道理：
  # "缺 fixture 必须 rc=2"，不是"缺了就跳过这几条断言"）。它们**不占注入编号**：
  # 注入验的是"判据该红没红"，这里验的是"前置该退 2 有没有退 2"。
  # 用的还是同一个 ok 样本的 mock，各跑一次 run_matrix。
  # ---------------------------------------------------------------------------
  local prebad=0
  port=$(python3 -c 'import socket;s=socket.socket();s.bind(("127.0.0.1",0));print(s.getsockname()[1]);s.close()')
  MODE=ok PORT="$port" python3 "$tmp/mock.py" >/dev/null 2>&1 &
  pid=$!
  i=0
  until curl -fsS --max-time 1 "http://127.0.0.1:$port/robots.txt" >/dev/null 2>&1; do
    i=$((i+1)); [ "$i" -gt 60 ] && break; sleep 0.1
  done
  BASE="http://127.0.0.1:${port}"
  ARCH_MOVED="$ARCHM"; ARCH_GONE="$ARCHG"; ARCH_GONE_FROM="$ARCHGF"; OOS="$OOSF"
  MOVED_FROM="$MVDF"; MOVED_TO="$MVDT"
  SH_LOCALE="$SHL"; SH_FROM="$SHF"; SH_TO="$SHT"; P404_A="$PA"; P404_B="$PB"; P404_C="$PC"

  # (a) 语种相同 → 派生不出串台探针，必须 rc=2。
  #     若不退 2 而是"照派生"，把 $SH_FROM 的 en 换成 zh 得到的就是 $SH_FROM 自己，
  #     那条"期望 404"会对着一条**已知 301 的地址**要 404 —— 恒红，且病因被读成实现有缺陷。
  SH_LOCALE="$LOCALE"
  run_matrix > "$tmp/out.pre_locale" 2>&1 < /dev/null; rc=$?
  if [ "$rc" -eq 2 ] && grep -q '^PRECONDITION: --locale 与 --slug-history-locale 相同' "$tmp/out.pre_locale"; then
    echo "  ok    前置[语种相同] LOCALE=SH_LOCALE=$LOCALE → rc=2 且点名 PRECONDITION（不派生恒红的重复断言）"
  else
    echo "  FAIL  前置[语种相同] → 期望 rc=2 + PRECONDITION，实际 rc=${rc}"; prebad=$((prebad+1))
  fi
  SH_LOCALE="$SHL"

  # (b) 五条里缺任何一条 → rc=2。缺 fixture 是"未验证"，不是"这一格不跑就跳过"
  #     —— 后者正是"缺 fixture 变假绿"的同一条通道（与 v11/v12 同族）。
  P404_C=""
  run_matrix > "$tmp/out.pre_p404" 2>&1 < /dev/null; rc=$?
  if [ "$rc" -eq 2 ] && grep -q 'PRECONDITION: 缺未登记旧 slug / SlugHistory fixture' "$tmp/out.pre_p404"; then
    echo "  ok    前置[缺 --p404-c] → rc=2 且点名 PRECONDITION（缺 fixture 不被当成跳过）"
  else
    echo "  FAIL  前置[缺 --p404-c] → 期望 rc=2 + PRECONDITION，实际 rc=${rc}"; prebad=$((prebad+1))
  fi
  P404_C="$PC"
  kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
  sfail=$((sfail+prebad))

  rm -rf "$tmp"

  # 用法错误（rc=3）：这一码只能由进程自己给出 —— 上面的注入都跑在函数内，
  # 碰不到参数解析。两个入口都要验：不认识参数、没给站点地址。
  # 断言"正好是 3"而不是"非零"：非零里还有 1（站点真错）和 2（没测成），
  # 混在一起就分不清"我命令敲错了"和"站点坏了"。
  local rc_bogus rc_nobase
  bash "$0" --definitely-not-an-arg >/dev/null 2>&1; rc_bogus=$?
  bash "$0" >/dev/null 2>&1; rc_nobase=$?
  sfail_before=$sfail
  [ "$rc_bogus" = "3" ] || { echo "  WRONG 不认识参数 → 期望 rc=3 实得 rc=$rc_bogus"; sfail=$((sfail+1)); }
  [ "$rc_nobase" = "3" ] || { echo "  WRONG 没给站点地址 → 期望 rc=3 实得 rc=$rc_nobase"; sfail=$((sfail+1)); }
  [ "$sfail" = "$sfail_before" ] && echo "  ok    用法错误（不认识参数 / 没给地址）→ rc=3，与 1/2 区分开"

  # 这里原来有一条**结构断言**（数 S8 段里 `norm_url "$can"` 出现 >= 2 次、逐字比 0 次），
  # 用来防"两条分支口径不一致"。已经**删掉**，换成把判断抽成 `is_cross_url()` 单一出口：
  # 结构断言只能**监视**漂移，抽成函数则让漂移**不可能发生**（同一个问题只有一个出口）。
  # 而且结构断言有反作用：将来有人把两处比较抽成函数（这是更好的写法），那条断言会**把正确
  # 的重构判红** —— 一条把"改好"判红的判据，教出来的是"别改"。
  # 现在这两条分支的一致性由 v19（S6 与 S8 同时红）在行为层担保，不再需要形状断言。

  [ "$sfail" -eq 0 ] && { echo "== 自证通过：正样本绿；${ninj} 个注入各自红在预期断言上；用法错误 rc=3；规则4负向半边的两个 PRECONDITION 各自 rc=2；S6/S8 的判断共用 is_cross_url 单一出口"; return 0; }
  echo "== 自证失败：$sfail 项不符合预期"; return 1
}

HF=$(mktemp); BF=$(mktemp)
trap 'rm -f "$HF" "$BF"' EXIT

if [ "${SELF_TEST}" = "1" ]; then self_test; exit $?; fi

# 没给站点地址 = 你不知道要测谁 —— 这是用法错误（3），不是"未验证"（2）。
# 2 留给"给了地址但站点/夹具不全"，那种情况脚本能自己说清缺什么。
[ -n "$BASE" ] || { echo "用法: $0 https://shop.example.com [--locale en] [--archived-moved P --archived-gone P --archived-gone-from P --moved-from P --moved-to P --out-of-stock P] [--slug-history-locale L --slug-history-from P --slug-history-to P --p404-a P --p404-b P --p404-c P] | --self-test" >&2; exit 3; }
run_matrix; exit $?
