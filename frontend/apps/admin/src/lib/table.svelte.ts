import { goto } from '$app/navigation'
import { page } from '$app/state'

/**
 * URL 驱动的列表状态（前端设计 §5.4）：
 * 筛选 / 排序 / 分页 / 关键词全部进 URL search params，组件只读 page.url.searchParams。
 * 变更即 goto（replaceState + keepFocus），列表数据由 createQuery 以 key 变化触发 refetch。
 */
export function createTableState(opts: { pageSize?: number } = {}) {
  const pageSize = opts.pageSize ?? 25

  const params = $derived(page.url.searchParams)

  const q = $derived(params.get('q') ?? '')
  const status = $derived(params.get('status') ?? '')
  const sort = $derived(params.get('sort') ?? '')
  const pageNum = $derived(Math.max(1, Number(params.get('page') ?? '1') || 1))

  /** 序列化 key：参数变化 → 调用方据此 refetch。 */
  const key = $derived(`q=${q}|status=${status}|sort=${sort}|page=${pageNum}`)

  /** 拼接成 GET 查询串（不含 ?）。 */
  function queryString(): string {
    const next = new URLSearchParams()
    if (q) next.set('q', q)
    if (status) next.set('status', status)
    if (sort) next.set('sort', sort)
    next.set('page', String(pageNum))
    next.set('pageSize', String(pageSize))
    return next.toString()
  }

  function update(partial: Record<string, string | undefined>) {
    const next = new URLSearchParams(params)
    for (const [k, v] of Object.entries(partial)) {
      if (v) next.set(k, v)
      else next.delete(k)
    }
    // 除翻页外，任何筛选/排序变化都回到第 1 页
    if (!('page' in partial)) next.delete('page')

    const qs = next.toString()
    const path = page.url.pathname
    goto(qs ? `${path}?${qs}` : path, { replaceState: true, keepFocus: true, noScroll: true })
  }

  return {
    get q() {
      return q
    },
    get status() {
      return status
    },
    get sort() {
      return sort
    },
    get page() {
      return pageNum
    },
    get pageSize() {
      return pageSize
    },
    get key() {
      return key
    },
    setQ: (v: string) => update({ q: v || undefined }),
    setStatus: (v: string) => update({ status: v || undefined }),
    setSort: (v: string) => update({ sort: v || undefined }),
    setPage: (p: number) => update({ page: p > 1 ? String(p) : undefined }),
    queryString,
  }
}
