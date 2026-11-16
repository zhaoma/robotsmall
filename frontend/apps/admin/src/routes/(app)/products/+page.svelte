<script lang="ts">
  import { base } from '#lib/paths'
  import { Button, EmptyState, Input, Pagination, Select, Skeleton, Table, toast } from '@openstore/ui'
  import type { SelectOption, TableColumn } from '@openstore/ui'
  import { apiFetch, isStatus } from '#lib/api'
  import { createQuery } from '#lib/query.svelte'
  import { createTableState } from '#lib/table.svelte'
  import { t } from '#lib/i18n'
  import Icon from '#lib/components/Icon.svelte'
  import StatusBadge from '#lib/components/StatusBadge.svelte'
  import type { AdminProductDto, AdminProductListDto, Paged, ProductStatus } from '@openstore/sdk'

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

  /** 状态筛选做成 chips：比下拉少一次点击，且当前筛选项常驻可见。 */
  const statusChips: { value: string; labelKey: string }[] = [
    { value: '', labelKey: 'admin.products.all' },
    { value: 'Active', labelKey: 'admin.products.active' },
    { value: 'Draft', labelKey: 'admin.products.draft' },
    { value: 'Archived', labelKey: 'admin.products.archived' },
  ]

  // 排序白名单（后端权威契约）：slug/title/status 各 asc/desc。
  const sortOptions: SelectOption[] = [
    { value: '', label: t('admin.products.sortDefault') },
    { value: 'slug_asc', label: t('admin.products.sortSlugAsc') },
    { value: 'slug_desc', label: t('admin.products.sortSlugDesc') },
    { value: 'title_asc', label: t('admin.products.sortTitleAsc') },
    { value: 'title_desc', label: t('admin.products.sortTitleDesc') },
    { value: 'status_asc', label: t('admin.products.sortStatusAsc') },
    { value: 'status_desc', label: t('admin.products.sortStatusDesc') },
  ]

  const columns: TableColumn[] = [
    { key: 'title', label: t('admin.products.titleLabel') },
    { key: 'status', label: t('admin.products.status') },
    { key: 'variantCount', label: t('admin.products.variantCount'), align: 'right', width: '5rem' },
  ]

  let searchInput = $state('')
  let selected = $state<Set<string>>(new Set())
  let batchLoading = $state(false)
  let selectedId = $state<string | null>(null)

  const rows = $derived(list.data?.items ?? [])
  const total = $derived(list.data?.total ?? 0)
  const pageCount = $derived(list.data?.pageCount ?? 1)

  // 详情面板数据：仅在选中某行时请求，切换选中即重新取
  const detail = createQuery<AdminProductDto>(
    (signal) => apiFetch(`/api/admin/products/${selectedId}`, { signal }),
    { immediate: false }
  )
  $effect(() => {
    if (selectedId) detail.refetch()
  })

  const selectedRow = $derived(rows.find((r) => r.id === selectedId))

  function submitSearch(e?: SubmitEvent) {
    e?.preventDefault()
    table.setQ(searchInput.trim())
  }

  function toggleRow(id: string) {
    const next = new Set(selected)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    selected = next
  }

  function pick(row: AdminProductListDto) {
    selectedId = selectedId === row.id ? null : row.id
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

  /** minor units + 币种，统一两位小数；整数分换算避免浮点误差。 */
  function money(amountMinor: number, currency: string): string {
    const symbol = currency === 'CNY' ? '¥' : currency === 'USD' ? '$' : currency === 'EUR' ? '€' : ''
    const v = (amountMinor / 100).toLocaleString('zh-Hans', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    })
    return symbol ? `${symbol}${v}` : `${v} ${currency}`
  }

  const errorDetail = $derived(list.problem?.detail ?? list.problem?.title ?? t('common.state.error'))
</script>

<div class="products">
  <div class="list">
    <div class="pagehead">
      <div>
        <h1>
          {t('admin.products.title')}
          {#if total > 0}<span class="cnt">{total.toLocaleString('zh-Hans')}</span>{/if}
        </h1>
        <p>
          {t('common.pagination.of')} {total.toLocaleString('zh-Hans')} {t('common.pagination.items')}
          <span class="dot">·</span>
          {t('common.pagination.page')} {table.page} / {pageCount}
        </p>
      </div>
      <div class="actions">
        <Button variant="secondary" size="sm" onclick={() => list.refetch()} loading={list.loading}>
          {t('common.action.refresh')}
        </Button>
        <Button href={`${base}/products/new`}>{t('admin.products.new')}</Button>
      </div>
    </div>

    <div class="filters">
      <div class="chips" role="group" aria-label={t('admin.products.status')}>
        {#each statusChips as chip (chip.value)}
          <button class="chip" aria-pressed={table.status === chip.value} onclick={() => table.setStatus(chip.value)}>
            {t(chip.labelKey)}
          </button>
        {/each}
      </div>

      <div class="filters__right">
        <form class="search" onsubmit={submitSearch}>
          <Input placeholder={t('admin.products.search')} bind:value={searchInput} />
        </form>
        <Select
          options={sortOptions}
          value={table.sort}
          onchange={(e) => table.setSort(e.currentTarget.value)}
        />
      </div>
    </div>

    {#if selected.size > 0}
      <div class="batch">
        <span>{selected.size} {t('admin.table.selected')}</span>
        <Button size="sm" loading={batchLoading} onclick={() => batchChangeStatus('Active')}>
          {t('admin.products.batchActivate')}
        </Button>
        <Button size="sm" variant="danger" loading={batchLoading} onclick={() => batchChangeStatus('Archived')}>
          {t('admin.products.batchArchive')}
        </Button>
        <button class="linkbtn" onclick={() => (selected = new Set())}>{t('common.action.cancel')}</button>
      </div>
    {/if}

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
      <div class="tbl">
        <Table {columns} variant="flat" selectable>
          {#snippet children()}
            {#each rows as row (row.id)}
              <tr
                aria-selected={selectedId === row.id ? 'true' : undefined}
                tabindex="0"
                onclick={() => pick(row)}
                onkeydown={(e) => {
                  if (e.key === 'Enter') pick(row)
                }}
              >
                <td>
                  <input
                    type="checkbox"
                    checked={selected.has(row.id)}
                    onclick={(e) => e.stopPropagation()}
                    onchange={() => toggleRow(row.id)}
                    aria-label={row.title}
                  />
                </td>
                <td>
                  <div class="prod">
                    <div class="prod__thumb">{row.title.slice(0, 1)}</div>
                    <div class="prod__text">
                      <b>{row.title}</b>
                      <code>{row.slug}</code>
                    </div>
                  </div>
                </td>
                <td><StatusBadge status={row.status} /></td>
                <td class="num">{row.variantCount}</td>
              </tr>
            {/each}
          {/snippet}
        </Table>
      </div>

      <div class="pagination">
        <Pagination
          page={table.page}
          {pageCount}
          {total}
          pageSize={table.pageSize}
          unit={t('common.pagination.items')}
          onPage={table.setPage}
        />
      </div>
    {/if}
  </div>

  <!-- 常驻详情面板：与列表并存，改完一个可以直接点下一个，不必开合抽屉 -->
  <aside class="insp" aria-label={t('admin.shell.detailPanel')}>
    {#if !selectedId}
      <div class="insp__hint">
        <Icon name="panel" size={20} />
        <p>{t('admin.products.selectRow')}</p>
      </div>
    {:else if detail.problem}
      <div class="insp__hint">
        <Icon name="alert" size={20} />
        <p>{detail.problem?.detail ?? t('common.state.error')}</p>
      </div>
    {:else}
      <div class="insp__head">
        <div class="insp__top">
          <div class="prod__thumb">{selectedRow?.title.slice(0, 1) ?? '·'}</div>
          <div class="insp__name">
            <b>{selectedRow?.title ?? '—'}</b>
            <code>{selectedRow?.slug ?? ''}</code>
          </div>
          <button class="iconbtn" aria-label={t('common.action.close')} onclick={() => (selectedId = null)}>
            <Icon name="close" size={14} />
          </button>
        </div>
      </div>

      <div class="insp__body">
        {#if detail.loading && !detail.data}
          <Skeleton lines={6} />
        {:else if detail.data}
          <section>
            <h2>{t('admin.products.status')}</h2>
            <dl class="kv">
              <dt>{t('admin.products.status')}</dt>
              <dd><StatusBadge status={detail.data.status} /></dd>
              <dt>{t('admin.products.slug')}</dt>
              <dd><code>{detail.data.slug}</code></dd>
            </dl>
          </section>

          <section>
            <h2>{t('admin.products.variants')} <span>{detail.data.variants.length}</span></h2>
            {#each detail.data.variants as v (v.id)}
              <div class="var">
                <code>{v.sku}</code>
                {#if v.axisSignature}<span class="var__axis">{v.axisSignature}</span>{/if}
                <div class="var__meta">
                  <b>
                    {#if v.price}
                      {money(v.price.amountMinor, v.price.currency)}
                    {:else}
                      —
                    {/if}
                  </b>
                </div>
              </div>
            {/each}
          </section>
        {/if}
      </div>

      <div class="insp__foot">
        <Button href={`${base}/products/${selectedId}`}>{t('admin.products.openEditor')}</Button>
      </div>
    {/if}
  </aside>
</div>

<style>
  .products {
    display: flex;
    align-items: flex-start;
    gap: var(--space-4);
  }
  .list {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
  }

  /* 页头 */
  .pagehead {
    display: flex;
    align-items: flex-start;
    gap: var(--space-4);
  }
  .pagehead h1 {
    display: flex;
    align-items: center;
    gap: 9px;
    font-size: var(--text-xl);
    font-weight: 650;
    letter-spacing: var(--tracking-tight);
  }
  .cnt {
    padding: 2px 7px;
    border-radius: 20px;
    background: var(--color-primary-soft);
    color: var(--color-primary);
    font-size: var(--text-2xs);
    font-weight: 600;
    font-variant-numeric: tabular-nums;
  }
  .pagehead p {
    margin-top: var(--space-1);
    font-size: var(--text-xs);
    color: var(--color-muted);
  }
  .dot {
    color: var(--color-border-strong);
    padding: 0 4px;
  }
  .actions {
    margin-left: auto;
    display: flex;
    align-items: center;
    gap: var(--space-2);
  }

  /* 筛选条 */
  .filters {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    padding: var(--space-2) var(--space-3);
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
    background: var(--color-surface);
  }
  .chips {
    display: flex;
    gap: 3px;
  }
  .chip {
    padding: 4px 9px;
    border: 0;
    border-radius: var(--radius-sm);
    background: none;
    color: var(--color-muted);
    font: inherit;
    font-size: var(--text-xs);
    cursor: pointer;
  }
  .chip:hover {
    background: var(--color-hover);
    color: var(--color-fg);
  }
  .chip[aria-pressed='true'] {
    background: var(--color-bg);
    color: var(--color-fg);
    font-weight: 600;
    box-shadow: var(--shadow-sm), 0 0 0 1px var(--color-border);
  }
  .filters__right {
    margin-left: auto;
    display: flex;
    align-items: center;
    gap: var(--space-2);
  }
  .search {
    width: 15rem;
  }

  .batch {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    padding: var(--space-2) var(--space-3);
    border: 1px solid var(--color-primary-border);
    border-radius: var(--radius-md);
    background: var(--color-primary-soft);
    font-size: var(--text-xs);
    color: var(--color-fg);
  }
  .linkbtn {
    padding: 0;
    border: 0;
    background: none;
    font: inherit;
    font-size: var(--text-xs);
    color: var(--color-muted);
    text-decoration: underline;
    cursor: pointer;
  }

  /* 表格 */
  .tbl {
    border-radius: var(--radius-lg);
    overflow: hidden;
  }
  .prod {
    display: flex;
    align-items: center;
    gap: 10px;
    min-width: 0;
  }
  .prod__thumb {
    width: 32px;
    height: 32px;
    flex: none;
    border-radius: 7px;
    display: grid;
    place-items: center;
    background: color-mix(in srgb, var(--color-primary) 9%, var(--color-surface));
    color: color-mix(in srgb, var(--color-primary) 78%, var(--color-fg));
    border: 1px solid var(--color-primary-border);
    font-size: var(--text-sm);
    font-weight: 600;
  }
  .prod__text {
    min-width: 0;
  }
  .prod__text b {
    display: block;
    font-size: var(--text-sm);
    font-weight: 560;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .prod__text code {
    display: block;
    margin-top: 1px;
    font-family: var(--font-mono);
    font-size: var(--text-2xs);
    color: var(--color-subtle);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .num {
    text-align: right;
    font-variant-numeric: tabular-nums;
  }
  .pagination {
    display: flex;
    justify-content: flex-end;
  }

  /* 详情面板 */
  .insp {
    width: var(--insp-w);
    flex: none;
    position: sticky;
    top: 0;
    /* 与列表等高：面板是工作区的一部分，不该缩成一小截 */
    align-self: stretch;
    min-height: calc(100vh - var(--topbar-h) - 6.5rem);
    max-height: calc(100vh - var(--topbar-h) - 4.75rem);
    display: flex;
    flex-direction: column;
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
    background: var(--color-bg);
    overflow: hidden;
  }
  .insp__hint {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: var(--space-2);
    padding: var(--space-8) var(--space-6);
    color: var(--color-subtle);
    text-align: center;
  }
  .insp__hint p {
    font-size: var(--text-xs);
    color: var(--color-muted);
  }
  .insp__head {
    flex: none;
    padding: var(--space-3) var(--space-4);
    border-bottom: 1px solid var(--color-border);
  }
  .insp__top {
    display: flex;
    align-items: flex-start;
    gap: 10px;
  }
  .insp__name {
    flex: 1;
    min-width: 0;
  }
  .insp__name b {
    display: block;
    font-size: var(--text-md);
    font-weight: 640;
    letter-spacing: -0.01em;
    line-height: 1.35;
  }
  .insp__name code {
    display: block;
    margin-top: 2px;
    font-family: var(--font-mono);
    font-size: var(--text-2xs);
    color: var(--color-subtle);
  }
  .iconbtn {
    width: 24px;
    height: 24px;
    flex: none;
    border: 0;
    border-radius: var(--radius-sm);
    display: grid;
    place-items: center;
    background: none;
    color: var(--color-subtle);
    cursor: pointer;
  }
  .iconbtn:hover {
    background: var(--color-hover);
    color: var(--color-fg);
  }
  .insp__body {
    flex: 1;
    overflow-y: auto;
    padding: var(--space-4);
  }
  section {
    margin-bottom: var(--space-5);
  }
  section h2 {
    display: flex;
    align-items: center;
    gap: 5px;
    margin-bottom: var(--space-2);
    font-size: var(--text-2xs);
    font-weight: 600;
    letter-spacing: var(--tracking-wide);
    text-transform: uppercase;
    color: var(--color-subtle);
  }
  section h2 span {
    font-weight: 500;
  }
  .kv {
    display: grid;
    grid-template-columns: 4.5rem 1fr;
    gap: 7px 10px;
    margin: 0;
    font-size: var(--text-xs);
  }
  .kv dt {
    color: var(--color-muted);
  }
  .kv dd {
    margin: 0;
    color: var(--color-fg);
  }
  .kv code {
    padding: 1px 4px;
    border-radius: var(--radius-sm);
    background: var(--color-neutral-bg);
    font-family: var(--font-mono);
    font-size: var(--text-2xs);
  }
  .var {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    padding: 7px 9px;
    margin-bottom: 5px;
    border: 1px solid var(--color-border);
    border-radius: 7px;
    font-size: var(--text-xs);
  }
  .var code {
    font-family: var(--font-mono);
    font-size: var(--text-2xs);
    color: var(--color-fg);
  }
  .var__axis {
    color: var(--color-muted);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .var__meta {
    margin-left: auto;
    text-align: right;
    line-height: 1.3;
  }
  .var__meta b {
    font-size: var(--text-xs);
    font-weight: 600;
    font-variant-numeric: tabular-nums;
  }
  .insp__foot {
    flex: none;
    padding: var(--space-2) var(--space-4);
    border-top: 1px solid var(--color-border);
    background: var(--color-surface);
  }
  .insp__foot :global(a) {
    width: 100%;
    justify-content: center;
  }
</style>
