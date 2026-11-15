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
#   ./scripts/verify-seo-matrix.sh --self-test     # fail-before-pass 自证（本地 mock，不连真站）
#
# 退出码语义（**必须写死，否则会有人用 `[ $? -le 2 ]` 把"未验证"当"通过"**）:
#   0 = 全绿（唯一通过态）
#   1 = 有断言失败
#   2 = 前置条件缺失（缺 sitemap / 缺可筛选属性 / 缺归档或缺货 fixture）→ **等价于不通过**
# CI 里只允许 0；退出码 2 必须报"未验证"，不得静默放过。
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
    -h|--help)         sed -n '2,30p' "$0"; exit 0 ;;
    -*)                echo "未知参数: $1" >&2; exit 2 ;;
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

  # ---- S6 站内搜索：noindex,follow + 无 canonical + 不 Disallow
  echo "-- S6 站内搜索 noindex,follow / 无 canonical / 不 Disallow"
  local su="$BASE/$LOCALE/search?q=robot"
  probe_chain "$su"
  [ "$P_STATUS" = "200" ] && pass "S6 搜索页允许抓取（200）" || fail "S6 搜索页不可达" "http=$P_STATUS"
  if [ "$P_NI" = "yes" ]; then pass "S6 搜索页带 noindex"; else fail "S6 搜索页缺 noindex" "b2 档：不该收录 → noindex,follow"; fi
  if [ "$P_FOFF" = "yes" ]; then fail "S6 搜索页关闭了 follow" "要求 noindex,follow"; else pass "S6 搜索页 follow 未关闭"; fi
  if [ -z "$P_CAN" ]; then pass "S6 搜索页未输出 canonical"
  else fail "S6 搜索页输出了 canonical" "noindex 与 canonical 并存时 noindex 优先、canonical 被忽略，还可能反向污染目标页；实际=$P_CAN"; fi
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
      if [ -n "$can" ] && ! same "$can" "$u"; then
        fail "S8 $path 同时 Disallow + 外指 canonical" "canonical 读不到 → 归并失效，白做"
      fi
    fi
    if [ "$ni" = "yes" ] && [ -n "$can" ]; then
      if [ "$(norm_url "$can")" = "$(norm_url "$u")" ]; then
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
    echo "PRECONDITION: 缺归档商品 fixture（--archived-moved / --archived-gone / --archived-gone-from 三个都要给）。缺 fixture = 未验证 = AC-11 不通过" >&2
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
    404) fail "S9 规则4 旧 slug 返回 404" "SlugHistory 未实现或未登记：改过 slug 的在售商品，旧外链全部断掉（外部站点与书签指向的地址一律 404，且不会自己恢复）。必须 301 到新 slug" ;;
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
NOI = ["/zh/cart", "/zh/checkout", "/zh/account", "/zh/orders/track",
       "/setup", "/zh/_preview", "/zh/search"]   # 前缀匹配，同 NOINDEX_PATTERNS

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
    if path == OOSP:
        if MODE == "v5":                           # 注入：库存 0 返 404
            return 404, "text/html", page(""), {}
        return 200, "text/html", page(canon(OOSP) + ld("OutOfStock")), {}
    if path == PRODP:
        return 200, "text/html", page(canon(PRODP) + ld("InStock")), {}
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
    if any(path.startswith(x) for x in NOI):
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
  local mode desc want port rc out pid i sfail=0 ninj=0 first
  # 用 heredoc 逐行读，不用空格分隔的字符串——描述里有空格会被 for 拆散
  while IFS='|' read -r mode desc want; do
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
      if [ "$rc" -eq 0 ]; then echo "  ok    样本[ok] 绿（$OK passed,$BAD failed）"
      else echo "  FAIL  样本[ok] 应为绿却红了（rc=${rc}）"; printf '%s\n' "$out" | grep 'FAIL' | head -5; sfail=$((sfail+1)); fi
    else
      ninj=$((ninj+1))
      first=$(printf '%s\n' "$out" | grep 'FAIL' | head -1 | sed 's/^  FAIL  //')
      if [ "$want" = "PRECONDITION" ]; then
        # 缺 fixture 必须是 2，不是 0 也不是 1：0 会让人以为"验过了且通过"，1 会被当成"真失败"去看红字，
        # 两者都错。只有 2 + 明确的 PRECONDITION 行才表示"这一档没验"。
        if [ "$rc" -eq 2 ] && printf '%s\n' "$out" | grep -q '^PRECONDITION'; then
          echo "  ok    注入[$mode] $desc → rc=2 且打印 PRECONDITION（未验证不被当成绿）"
        else
          echo "  FAIL  注入[$mode] $desc → 期望 rc=2 + PRECONDITION，实际 rc=${rc}，首条红：${first:-（无）}"
          sfail=$((sfail+1))
        fi
      elif [ "$rc" -eq 1 ] && printf '%s\n' "$out" | grep 'FAIL' | grep -q "$want"; then
        echo "  ok    注入[$mode] $desc → 红在 ${want}：$first"
      else
        echo "  FAIL  注入[$mode] $desc → 期望红在 ${want}，实际 rc=${rc}，首条红：${first:-（无）}"
        sfail=$((sfail+1))
      fi
    fi
    kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
  # 注入编号空间说明（防止下一个人误以为跨文件同名同义）：
  #   v1~v15 是**本文件自证空间的编号**，与 docs/03 §9.2 的 V1~V12（后端/架构门禁注入，
  #   例如 V9 = storefront DTO 不泄露内部字段）**毫无关系**。跨文件同名不同义是允许的，
  #   但两边都必须写明，否则会有人以为"V9 已经有人做了"。
  #   v9  = 归档 301 的落点返回 404（红在"落点可访问"那条，不是状态码那条）
  #   v10 = 归档商品的历史 slug 被 301 到新 slug（红在"不得带 Location"那条）
  #   v13~v15 = 规则 4 的三种坏法，各自红在不同的断言上（见 mock 里 MVDF 分支的注释）
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
CASES
  rm -rf "$tmp"
  [ "$sfail" -eq 0 ] && { echo "== 自证通过：正样本绿；${ninj} 个注入各自红在预期断言上"; return 0; }
  echo "== 自证失败：$sfail 项不符合预期"; return 1
}

HF=$(mktemp); BF=$(mktemp)
trap 'rm -f "$HF" "$BF"' EXIT

if [ "${SELF_TEST}" = "1" ]; then self_test; exit $?; fi

[ -n "$BASE" ] || { echo "用法: $0 https://shop.example.com [--locale en] [--archived-moved P --archived-gone P --archived-gone-from P --moved-from P --moved-to P --out-of-stock P] | --self-test" >&2; exit 2; }
run_matrix; exit $?
