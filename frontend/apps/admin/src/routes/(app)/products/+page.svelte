<script lang="ts">
  import { base } from '$lib/paths'
  import { Button, EmptyState, Input, Pagination, Select, Skeleton, Table, toast } from '@openstore/ui'
  import type { SelectOption, TableColumn } from '@openstore/ui'
  import { apiFetch, isStatus } from '$lib/api'
  import { createQuery } from '$lib/query.svelte'
  import { createTableState } from '$lib/table.svelte'
  import { t } from '$lib/i18n'
  import StatusBadge from '$lib/components/StatusBadge.svelte'
  import type { AdminProductListDto, Paged, ProductStatus } from '@openstore/sdk'

  const table = createTableState({ pageSize: 25 })
  const list = createQuery<Paged<AdminProductListDto>>(
    (signal) => apiFetch(`/api/admin/products?${table.queryString()}`, { signal }),
    { immediate: false }
  )

  // 参数变化 → 重新请求（首次也由此触发）
  $effect(() => {
    table.key
    list.refetch()
  })

  const statusOptions: SelectOption[] = [
    { value: '', label: t('admin.products.status') },
    { value: 'Draft', label: t('admin.products.draft') },
    { value: 'Active', label: t('admin.products.active') },
    { value: 'Archived', label: t('admin.products.archived') },
  ]

  // 排序枚举【待确认】：后端 admin 列表 sort 取值未在 docs/03 §5.4 明确，先透传字符串。
  const sortOptions: SelectOption[] = [
    { value: '', label: t('admin.products.updatedAt') },
    { value: 'newest', label: 'Newest' },
    { value: 'title_asc', label: 'Title A-Z' },
    { value: 'title_desc', label: 'Title Z-A' },
  ]

  const columns: TableColumn[] = [
    { key: 'title', label: t('admin.products.titleLabel') },
    { key: 'status', label: t('admin.products.status') },
    { key: 'variantCount', label: t('admin.products.variantCount'), align: 'right' },
    { key: 'totalOnHand', label: t('admin.products.stock'), align: 'right' },
    { key: 'updatedAt', label: t('admin.products.updatedAt') },
  ]

  let searchInput = $state('')
  let selected = $state<Set<string>>(new Set())
  let batchLoading = $state(false)

  const rows = $derived(list.data?.items ?? [])
  const total = $derived(list.data?.total ?? 0)
  const pageCount = $derived(list.data?.pageCount ?? 1)

  const allChecked = $derived(rows.length > 0 && rows.every((r) => selected.has(r.id)))

  function submitSearch(e?: SubmitEvent) {
    e?.preventDefault()
    table.setQ(searchInput.trim())
  }

  function toggleAll() {
    selected = allChecked ? new Set() : new Set(rows.map((r) => r.id))
  }

  function toggleRow(id: string) {
    const next = new Set(selected)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    selected = next
  }

  async function batchChangeStatus(status: ProductStatus) {
    const ids = [...selected]
    if (ids.length === 0) return
    batchLoading = true
    try {
      // 批量改状态：逐个 PATCH /api/admin/products/{id}/status（既有端点，docs/03 §5.4 publish/archive 另计）
      await Promise.all(
        ids.map((id) => apiFetch(`/api/admin/products/${id}/status`, { method: 'PATCH', body: { status } }))
      )
      toast('success', status === 'Active' ? t('admin.products.batchActivate') : t('admin.products.batchArchive'))
      selected = new Set()
      list.refetch()
    } catch (err) {
      if (isStatus(err, 403)) toast('error', t('error.forbidden'))
      else toast('error', t('common.state.error'))
    } finally {
      batchLoading = false
    }
  }

  function formatDate(iso: string): string {
    const d = new Date(iso)
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString('zh-Hans')
  }

  const errorDetail = $derived(list.problem?.detail ?? list.problem?.title ?? t('common.state.error'))
</script>

<div class="products">
  <div class="products__head">
    <h1 class="products__title">{t('admin.products.title')}</h1>
    <Button href={`${base}/products/new`}>{t('admin.products.new')}</Button>
  </div>

  <form class="toolbar" onsubmit={submitSearch}>
    <div class="toolbar__search">
      <Input placeholder={t('admin.products.search')} bind:value={searchInput} />
    </div>
    <Button variant="secondary" type="submit">{t('common.action.search')}</Button>
    <Select options={statusOptions} value={table.status} onchange={(e) => table.setStatus(e.currentTarget.value)} />
    <Select options={sortOptions} value={table.sort} onchange={(e) => table.setSort(e.currentTarget.value)} />
    {#if selected.size > 0}
      <span class="toolbar__selected">{selected.size} {t('admin.table.selected')}</span>
      <Button size="sm" loading={batchLoading} onclick={() => batchChangeStatus('Active')}>{t('admin.products.batchActivate')}</Button>
      <Button size="sm" variant="danger" loading={batchLoading} onclick={() => batchChangeStatus('Archived')}>{t('admin.products.batchArchive')}</Button>
    {/if}
  </form>

  {#if list.loading && rows.length === 0}
    <Skeleton lines={10} />
  {:else if list.problem}
    <EmptyState
      title={list.problem?.status === 403 ? t('error.forbidden') : t('common.state.error')}
      description={errorDetail}
    >
      {#snippet action()}
        <Button variant="secondary" onclick={() => list.refetch()}>{t('common.action.retry')}</Button>
      {/snippet}
    </EmptyState>
  {:else if rows.length === 0}
    <EmptyState title={t('admin.table.empty')} />
  {:else}
    <Table {columns} selectable>
      {#snippet children()}
        {#each rows as row (row.id)}
          <tr>
            <td>
              <input
                type="checkbox"
                checked={selected.has(row.id)}
                onchange={() => toggleRow(row.id)}
                aria-label={row.title}
              />
            </td>
            <td>
              <a href={`${base}/products/${row.id}`}>{row.title}</a>
              <div class="slug">{row.slug}</div>
            </td>
            <td><StatusBadge status={row.status} /></td>
            <td class="num">{row.variantCount}</td>
            <td class="num">{row.totalOnHand}</td>
            <td>{formatDate(row.updatedAt)}</td>
          </tr>
        {/each}
      {/snippet}
    </Table>

    <div class="products__pagination">
      <Pagination page={table.page} {pageCount} {total} pageSize={table.pageSize} onPage={table.setPage} />
    </div>
  {/if}
</div>

<style>
  .products {
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
  }
  .products__head {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }
  .products__title {
    font-size: 1.25rem;
    font-weight: 600;
  }
  .toolbar {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: var(--space-2);
  }
  .toolbar__search {
    width: 16rem;
  }
  .toolbar__selected {
    font-size: 0.8125rem;
    color: var(--color-muted);
  }
  .slug {
    font-size: 0.75rem;
    color: var(--color-subtle);
  }
  .num {
    text-align: right;
    font-variant-numeric: tabular-nums;
  }
  .products__pagination {
    display: flex;
    justify-content: flex-end;
  }
</style>
