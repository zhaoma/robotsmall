<script lang="ts">
  import type { Snippet } from 'svelte'

  export interface TableColumn {
    key: string
    label?: string
    align?: 'left' | 'center' | 'right'
    width?: string
  }

  let {
    columns = [],
    selectable = false,
    variant = 'card',
    rowKey = undefined,
    empty = undefined,
    children,
  }: {
    columns?: TableColumn[]
    selectable?: boolean
    /** card = 白卡包裹（默认）；flat = 发丝线平铺，用于高密度列表页 */
    variant?: 'card' | 'flat'
    rowKey?: (row: unknown) => string | number
    empty?: Snippet
    children: Snippet
  } = $props()
</script>

<div class="table-wrap" class:table-wrap--flat={variant === 'flat'}>
  <table class="table">
    <thead>
      <tr>
        {#if selectable}<th class="table__th table__th--check" aria-hidden="true"></th>{/if}
        {#each columns as col (col.key)}
          <th class="table__th" style:width={col.width} class:align-center={col.align === 'center'} class:align-right={col.align === 'right'}>
            {col.label ?? ''}
          </th>
        {/each}
      </tr>
    </thead>
    <tbody>
      {@render children()}
    </tbody>
  </table>
  {#if empty}
    <div class="table__empty">
      {@render empty()}
    </div>
  {/if}
</div>

<style>
  .table-wrap {
    position: relative;
    border: 1px solid var(--color-border);
    border-radius: var(--radius-md);
    background: var(--color-surface-raised);
    overflow: auto;
  }
  .table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.8125rem;
  }
  .table__th {
    position: sticky;
    top: 0;
    z-index: 1;
    padding: var(--space-3) var(--space-4);
    background: var(--color-surface);
    color: var(--color-muted);
    font-weight: 500;
    text-align: left;
    white-space: nowrap;
    border-bottom: 1px solid var(--color-border);
  }
  .table__th--check {
    width: 2.5rem;
  }
  :global(.table td) {
    padding: var(--space-3) var(--space-4);
    border-bottom: 1px solid var(--color-border);
    color: var(--color-fg);
    vertical-align: middle;
  }
  :global(.table tbody tr:last-child td) {
    border-bottom: none;
  }
  :global(.table tbody tr:hover td) {
    background: var(--color-surface);
  }
  .align-center { text-align: center; }
  .align-right { text-align: right; }
  /* ── flat 变体：不套卡片，行靠 1px 分隔线，贴近工作台式布局 ── */
  .table-wrap--flat {
    border-radius: 0;
    background: var(--color-bg);
    overflow: auto;
  }
  .table-wrap--flat .table__th {
    background: var(--color-surface-sunken);
    padding: var(--space-2) var(--space-3);
    font-size: var(--text-2xs);
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: var(--tracking-wide);
    color: var(--color-subtle);
  }
  .table-wrap--flat :global(.table td) {
    height: var(--row-h);
    padding: 0 var(--space-3);
    font-size: var(--text-sm);
  }
  .table-wrap--flat :global(.table tbody tr:hover td) {
    background: var(--color-hover);
  }
  :global(.table tbody tr[aria-selected] td) {
    background: var(--color-selected);
  }
  :global(.table tbody tr[aria-selected] td:first-child) {
    box-shadow: inset 2.5px 0 0 var(--color-primary);
  }
  .table__empty {
    padding: var(--space-8);
    text-align: center;
    color: var(--color-muted);
  }
</style>
