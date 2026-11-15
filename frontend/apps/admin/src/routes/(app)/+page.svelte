<script lang="ts">
  import { Badge, Skeleton } from '@openstore/ui'
  import { apiFetch } from '$lib/api'
  import { createQuery } from '$lib/query.svelte'
  import { t } from '$lib/i18n'
  import type { DashboardSummaryDto } from '@openstore/sdk'

  // 本期后端可能未实现 dashboard/summary 聚合端点：失败即回退占位 0 + "待后端"标记，不造假数据。
  const dash = createQuery<DashboardSummaryDto>(() =>
    apiFetch('/api/admin/dashboard/summary')
  )

  const pending = $derived(dash.data == null)

  interface Card {
    key: string
    label: string
    value: number
  }

  const cards = $derived.by(() => {
    const c: Card[] = [
      { key: 'todayOrders', label: t('admin.dashboard.todayOrders'), value: dash.data?.ordersToday ?? 0 },
      { key: 'pendingShipment', label: t('admin.dashboard.pendingShipment'), value: dash.data?.pendingShipmentCount ?? 0 },
      { key: 'pendingRefund', label: t('admin.dashboard.pendingRefund'), value: dash.data?.pendingRefundCount ?? 0 },
      { key: 'lowStock', label: t('admin.dashboard.lowStock'), value: dash.data?.lowStockCount ?? 0 },
      { key: 'coverage', label: t('admin.dashboard.translationCoverage'), value: 0 },
    ]
    return c
  })

  const steps = $derived([
    t('admin.dashboard.stepPayment'),
    t('admin.dashboard.stepShipping'),
    t('admin.dashboard.stepProducts'),
  ])
</script>

<div class="dashboard">
  <h1 class="dashboard__title">{t('admin.dashboard.title')}</h1>

  <section class="cards">
    {#each cards as card (card.key)}
      <div class="card">
        <div class="card__label">{card.label}</div>
        {#if dash.loading}
          <Skeleton width="3rem" height="1.75rem" />
        {:else}
          <div class="card__value">{card.value}</div>
        {/if}
        {#if pending}<Badge tone="neutral">{t('admin.dashboard.pendingBackend')}</Badge>{/if}
      </div>
    {/each}
  </section>

  <section class="panel">
    <h2 class="panel__title">{t('admin.dashboard.nextSteps')}</h2>
    <ol class="steps">
      {#each steps as step, i (i)}
        <li class="steps__item"><span class="steps__num">{i + 1}</span>{step}</li>
      {/each}
    </ol>
  </section>
</div>

<style>
  .dashboard {
    display: flex;
    flex-direction: column;
    gap: var(--space-6);
  }
  .dashboard__title {
    font-size: 1.25rem;
    font-weight: 600;
  }
  .cards {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(11rem, 1fr));
    gap: var(--space-4);
  }
  .card {
    display: flex;
    flex-direction: column;
    gap: var(--space-2);
    padding: var(--space-4);
    background: var(--color-surface-raised);
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
  }
  .card__label {
    font-size: 0.8125rem;
    color: var(--color-muted);
  }
  .card__value {
    font-size: 1.75rem;
    font-weight: 700;
    color: var(--color-fg);
  }
  .panel {
    padding: var(--space-5);
    background: var(--color-surface-raised);
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
  }
  .panel__title {
    font-size: 1rem;
    font-weight: 600;
    margin-bottom: var(--space-4);
  }
  .steps {
    margin: 0;
    padding: 0;
    list-style: none;
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
  }
  .steps__item {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    color: var(--color-fg);
  }
  .steps__num {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 1.5rem;
    height: 1.5rem;
    border-radius: 50%;
    background: var(--color-primary-soft);
    color: var(--color-primary);
    font-size: 0.75rem;
    font-weight: 600;
  }
</style>
