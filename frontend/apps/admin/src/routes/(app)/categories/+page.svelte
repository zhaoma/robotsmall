<script lang="ts">
  import { Button, EmptyState, Skeleton } from '@openstore/ui'
  import { apiFetch } from '#lib/api'
  import { createQuery } from '#lib/query.svelte'
  import { t } from '#lib/i18n'
  import type { AdminCategoryNodeDto } from '@openstore/sdk'

  // 后端契约：GET /api/admin/categories → AdminCategoryNodeDto[]（M0 返回空数组）。
  const categories = createQuery<AdminCategoryNodeDto[]>(
    (signal) => apiFetch('/api/admin/categories', { signal })
  )

  /** 树展平为带深度的行，便于简单列表渲染（M0 空数组时不会触发）。 */
  function flatten(
    nodes: AdminCategoryNodeDto[],
    depth = 0,
  ): { node: AdminCategoryNodeDto; depth: number }[] {
    return nodes.flatMap((n) => [{ node: n, depth }, ...flatten(n.children, depth + 1)])
  }

  const rows = $derived(categories.data ? flatten(categories.data) : [])
</script>

<div class="categories">
  <div class="pagehead">
    <h1>{t('admin.categories.title')}</h1>
    <Button variant="secondary" size="sm" onclick={() => categories.refetch()} loading={categories.loading}>
      {t('common.action.refresh')}
    </Button>
  </div>

  {#if categories.loading && !categories.data}
    <Skeleton lines={6} />
  {:else if categories.problem}
    <EmptyState
      title={categories.problem.status === 403 ? t('error.forbidden') : t('common.state.error')}
      description={categories.problem.detail ?? ''}
    >
      {#snippet action()}
        <Button variant="secondary" onclick={() => categories.refetch()}>{t('common.action.retry')}</Button>
      {/snippet}
    </EmptyState>
  {:else if rows.length === 0}
    <EmptyState title={t('admin.categories.empty')} />
  {:else}
    <ul class="tree">
      {#each rows as { node, depth } (node.id)}
        <li style={`padding-left:${0.5 + depth * 1.25}rem`}>
          <span class="tree__name">{node.name}</span>
          <code class="tree__slug">{node.slug}</code>
        </li>
      {/each}
    </ul>
  {/if}
</div>

<style>
  .categories {
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
  }
  .pagehead {
    display: flex;
    align-items: center;
    gap: var(--space-3);
  }
  .pagehead h1 {
    font-size: var(--text-xl);
    font-weight: 650;
    letter-spacing: var(--tracking-tight);
  }
  .tree {
    list-style: none;
    margin: 0;
    padding: var(--space-4);
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
    background: var(--color-surface);
  }
  .tree li {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    padding: 6px 0;
    border-bottom: 1px solid var(--color-border);
    font-size: var(--text-sm);
  }
  .tree li:last-child {
    border-bottom: 0;
  }
  .tree__name {
    font-weight: 560;
  }
  .tree__slug {
    font-family: var(--font-mono);
    font-size: var(--text-2xs);
    color: var(--color-subtle);
  }
</style>
