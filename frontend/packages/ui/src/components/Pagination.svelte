<script lang="ts">
  let {
    page = 1,
    pageCount = 1,
    total = 0,
    pageSize = 25,
    unit = '',
    onPage = undefined,
  }: {
    page?: number
    pageCount?: number
    total?: number
    pageSize?: number
    /** 总量单位（如「条」）：文案由调用方按当前语种传入，UI 层不硬编码 */
    unit?: string
    onPage?: (page: number) => void
  } = $props()

  const pages = $derived.by(() => {
    const p = Math.min(Math.max(1, page), Math.max(1, pageCount))
    const lo = Math.max(1, p - 2)
    const hi = Math.min(Math.max(1, pageCount), p + 2)
    const out: number[] = []
    for (let i = lo; i <= hi; i++) out.push(i)
    return out
  })

  const canPrev = $derived(page > 1)
  const canNext = $derived(page < pageCount)
</script>

<nav class="pagination" aria-label="pagination">
  <span class="pagination__total">{total}{unit ? ` ${unit}` : ''}</span>
  <button class="page-btn" disabled={!canPrev} onclick={() => onPage?.(page - 1)} aria-label="previous page">‹</button>
  {#each pages as p (p)}
    <button class="page-btn" class:is-current={p === page} onclick={() => onPage?.(p)}>{p}</button>
  {/each}
  <button class="page-btn" disabled={!canNext} onclick={() => onPage?.(page + 1)} aria-label="next page">›</button>
</nav>

<style>
  .pagination {
    display: flex;
    align-items: center;
    gap: var(--space-1);
    font-size: 0.8125rem;
  }
  .pagination__total {
    margin-right: var(--space-3);
    color: var(--color-muted);
  }
  .page-btn {
    min-width: 2rem;
    height: 2rem;
    padding: 0 var(--space-2);
    border: 1px solid var(--color-border);
    border-radius: var(--radius-md);
    background: var(--color-surface-raised);
    color: var(--color-fg);
    font-family: var(--font-sans);
    cursor: pointer;
  }
  .page-btn:hover:not(:disabled) { background: var(--color-surface); }
  .page-btn:disabled { opacity: 0.4; cursor: not-allowed; }
  .page-btn.is-current {
    background: var(--color-primary);
    border-color: var(--color-primary);
    color: var(--color-primary-fg);
  }
</style>
