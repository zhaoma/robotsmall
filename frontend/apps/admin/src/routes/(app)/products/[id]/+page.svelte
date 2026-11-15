<script lang="ts">
  import { base } from '$lib/paths'
  import { page } from '$app/state'
  import { Button, EmptyState, Field, FormActions, Input, Skeleton, Tabs, Textarea, toast } from '@openstore/ui'
  import type { TabItem } from '@openstore/ui'
  import { apiFetch, isStatus } from '$lib/api'
  import { createMutation, createQuery } from '$lib/query.svelte'
  import { t } from '$lib/i18n'
  import StatusBadge from '$lib/components/StatusBadge.svelte'
  import { formatMoney } from '@openstore/sdk'
  import type { AdminProductDto } from '@openstore/sdk'

  const id = page.params.id

  const product = createQuery<AdminProductDto>(
    (signal) => apiFetch(`/api/admin/products/${id}`, { signal })
  )

  // 基本信息 draft（与实体解耦，取消即丢弃）
  let slug = $state('')
  let title = $state('')
  let description = $state('')

  // product.data 到位后回填 draft（只回填一次，避免覆盖用户正在编辑的内容）
  $effect(() => {
    const d = product.data
    if (d && slug === '' && title === '') {
      slug = d.slug
      title = d.title
      description = d.descriptionHtml ?? ''
    }
  })

  const save = createMutation<{ slug: string; title: string; descriptionHtml?: string }, AdminProductDto>(
    (input) =>
      apiFetch(`/api/admin/products/${id}`, {
        method: 'PUT', // docs/03 §5.4 为 PUT；团队简报写 PATCH【待确认】
        body: input,
      })
  )

  let activeTab = $state('basic')

  const tabs: TabItem[] = [
    { key: 'basic', label: t('admin.products.basic') },
    { key: 'variants', label: t('admin.products.variants') },
    { key: 'price', label: t('admin.products.price') },
    { key: 'images', label: t('admin.products.images') },
    { key: 'attributes', label: t('admin.products.attributesTab') },
    { key: 'seo', label: t('admin.products.seo') },
  ]

  async function onSave() {
    try {
      const saved = await save.mutate({ slug, title, descriptionHtml: description || undefined })
      toast('success', t('common.action.save'))
      if (saved) {
        slug = saved.slug
        title = saved.title
        description = saved.descriptionHtml ?? ''
      }
    } catch (err) {
      if (isStatus(err, 403)) toast('error', t('error.forbidden'))
      else if (isStatus(err, 409)) toast('error', t('error.conflict'))
      else toast('error', t('common.state.error'))
    }
  }

  const errorDetail = $derived(product.problem?.detail ?? product.problem?.title ?? t('common.state.error'))
</script>

<div class="editor">
  <div class="editor__head">
    <Button variant="ghost" href={`${base}/products`}>{t('common.action.back')}</Button>
    <h1 class="editor__title">{t('admin.products.edit')}</h1>
    {#if product.data}<StatusBadge status={product.data.status} />{/if}
  </div>

  {#if product.loading}
    <Skeleton lines={8} />
  {:else if product.problem}
    <EmptyState
      title={product.problem.status === 403 ? t('error.forbidden') : product.problem.status === 404 ? t('common.state.notFound') : t('common.state.error')}
      description={errorDetail}
    >
      {#snippet action()}
        <Button variant="secondary" onclick={() => product.refetch()}>{t('common.action.retry')}</Button>
      {/snippet}
    </EmptyState>
  {:else}
    <Tabs {tabs} value={activeTab} onChange={(k) => (activeTab = k)} />

    {#if activeTab === 'basic'}
      <form class="form" onsubmit={(e) => { e.preventDefault(); onSave() }}>
        <Field label={t('admin.products.slug')} for="slug" required hint="^[a-z0-9]+(-[a-z0-9]+)*">
          <Input name="slug" bind:value={slug} required />
        </Field>
        <Field label={t('admin.products.titleLabel')} for="title" required>
          <Input name="title" bind:value={title} required />
        </Field>
        <Field label={t('admin.products.description')} for="description">
          <Textarea name="description" bind:value={description} rows={6} />
        </Field>
        <FormActions>
          <Button variant="secondary" href={`${base}/products`}>{t('common.action.cancel')}</Button>
          <Button type="submit" loading={save.loading}>{t('common.action.save')}</Button>
        </FormActions>
      </form>
    {:else if activeTab === 'variants'}
      <div class="variants">
        {#if product.data.variants.length === 0}
          <EmptyState title={t('admin.products.noVariants')} />
        {:else}
          <table class="vtable">
            <thead>
              <tr><th>{t('admin.products.sku')}</th><th>AxisSignature</th></tr>
            </thead>
            <tbody>
              {#each product.data.variants as v (v.id)}
                <tr><td>{v.sku}</td><td class="mono">{v.axisSignature}</td></tr>
              {/each}
            </tbody>
          </table>
        {/if}
      </div>
    {:else if activeTab === 'price'}
      <div class="variants">
        <table class="vtable">
          <thead>
            <tr><th>{t('admin.products.sku')}</th><th>{t('admin.products.price')}</th></tr>
          </thead>
          <tbody>
            {#each product.data.variants as v (v.id)}
              <tr>
                <td>{v.sku}</td>
                <td>
                  {#if v.prices.length > 0}
                    {formatMoney(v.prices[0].amountMinor, v.prices[0].currency)}
                  {:else}
                    —
                  {/if}
                </td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {:else}
      <EmptyState title={t('common.placeholder.notImplemented')} description={t('common.placeholder.comingSoon')} />
    {/if}
  {/if}
</div>

<style>
  .editor {
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
  }
  .editor__head {
    display: flex;
    align-items: center;
    gap: var(--space-3);
  }
  .editor__title {
    font-size: 1.25rem;
    font-weight: 600;
  }
  .form {
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
    max-width: 40rem;
    padding: var(--space-5);
    background: var(--color-surface-raised);
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
  }
  .variants {
    padding: var(--space-5);
    background: var(--color-surface-raised);
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
  }
  .vtable {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.8125rem;
  }
  .vtable th {
    padding: var(--space-2) var(--space-3);
    text-align: left;
    color: var(--color-muted);
    font-weight: 500;
    border-bottom: 1px solid var(--color-border);
  }
  .vtable td {
    padding: var(--space-2) var(--space-3);
    border-bottom: 1px solid var(--color-border);
  }
  .mono {
    font-family: var(--font-mono);
    font-size: 0.75rem;
  }
</style>
