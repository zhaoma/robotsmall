<script lang="ts">
  import { goto } from '$app/navigation'
  import { base } from '$lib/paths'
  import { Button, Field, FormActions, Input, Select, Textarea, toast } from '@openstore/ui'
  import type { SelectOption } from '@openstore/ui'
  import { apiFetch, isStatus } from '$lib/api'
  import { t } from '$lib/i18n'
  import { majorToMinor } from '@openstore/sdk'
  import type { CreateProductRequest } from '@openstore/sdk'

  let slug = $state('')
  let title = $state('')
  let description = $state('')
  let sku = $state('')
  let priceMajor = $state('')
  let currency = $state('CNY')

  let loading = $state(false)

  const currencyOptions: SelectOption[] = [
    { value: 'CNY', label: 'CNY' },
    { value: 'USD', label: 'USD' },
    { value: 'JPY', label: 'JPY' },
  ]

  async function onSubmit(e: SubmitEvent) {
    e.preventDefault()
    if (loading) return
    loading = true
    try {
      // 契约：POST /api/admin/products（docs/03 §5.4）。字段名 slug/title 与 M0 领域实体对齐，
      // docs/03 用 skuRoot/names/descriptionHtml/categoryIds/attributes【待确认】。
      const body: CreateProductRequest = {
        slug: slug.trim(),
        title: title.trim(),
        descriptionHtml: description.trim() || undefined,
        variants: [
          {
            sku: sku.trim(),
            price:
              priceMajor.trim() !== ''
                ? { amountMinor: majorToMinor(Number(priceMajor), currency), currency }
                : undefined,
          },
        ],
      }
      const res = await apiFetch<{ id: string }>('/api/admin/products', { method: 'POST', body })
      toast('success', t('common.action.create'))
      await goto(`${base}/products/${res.id}`)
    } catch (err) {
      if (isStatus(err, 403)) toast('error', t('error.forbidden'))
      else toast('error', t('common.state.error'))
    } finally {
      loading = false
    }
  }
</script>

<div class="new-product">
  <div class="new-product__head">
    <Button variant="ghost" href={`${base}/products`}>{t('common.action.back')}</Button>
    <h1 class="new-product__title">{t('admin.products.new')}</h1>
  </div>

  <form class="form" onsubmit={onSubmit}>
    <Field label={t('admin.products.slug')} for="slug" required hint="^[a-z0-9]+(-[a-z0-9]+)*">
      <Input name="slug" bind:value={slug} required />
    </Field>
    <Field label={t('admin.products.titleLabel')} for="title" required>
      <Input name="title" bind:value={title} required />
    </Field>
    <Field label={t('admin.products.description')} for="description">
      <Textarea name="description" bind:value={description} rows={5} />
    </Field>

    <div class="variant">
      <div class="variant__head">{t('admin.products.variants')}</div>
      <div class="variant__row">
        <Field label={t('admin.products.sku')} for="sku" required>
          <Input name="sku" bind:value={sku} required />
        </Field>
        <Field label={t('admin.products.price')} for="price">
          <Input name="price" type="number" min="0" step="0.01" bind:value={priceMajor} />
        </Field>
        <Field label="Currency" for="currency">
          <Select name="currency" options={currencyOptions} bind:value={currency} />
        </Field>
      </div>
      <p class="variant__hint">{t('admin.products.placeholderVariant')}</p>
    </div>

    <FormActions>
      <Button variant="secondary" href={`${base}/products`}>{t('common.action.cancel')}</Button>
      <Button type="submit" {loading}>{t('common.action.create')}</Button>
    </FormActions>
  </form>
</div>

<style>
  .new-product {
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
  }
  .new-product__head {
    display: flex;
    align-items: center;
    gap: var(--space-3);
  }
  .new-product__title {
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
  .variant {
    padding: var(--space-4);
    border: 1px dashed var(--color-border-strong);
    border-radius: var(--radius-md);
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
  }
  .variant__head {
    font-weight: 600;
    font-size: 0.875rem;
  }
  .variant__row {
    display: grid;
    grid-template-columns: 1fr 1fr 8rem;
    gap: var(--space-3);
  }
  .variant__hint {
    margin: 0;
    font-size: 0.75rem;
    color: var(--color-muted);
  }
</style>
