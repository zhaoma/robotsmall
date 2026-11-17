<script lang="ts">
  import { goto } from '$app/navigation'
  import { base } from '#lib/paths'
  import { Button, Field, FormActions, Input, Select, toast } from '@openstore/ui'
  import type { SelectOption } from '@openstore/ui'
  import { apiFetch, isStatus } from '#lib/api'
  import { t } from '#lib/i18n'
  import { majorToMinor } from '@openstore/sdk'
  import type { CreateProductRequest } from '@openstore/sdk'

  type AxisDraft = { code: string; options: string }

  let slug = $state('')
  let title = $state('')
  let currency = $state('CNY')

  // 轴草稿：code 是变体签名的键（与后端 VariantMatrix 一致，如 color），
  // options 是逗号分隔的可选值（如 red,blue）。
  // M1 阶段轴与取值为手工输入：属性定义表已建（AttributeDefinition/Option），
  // 但属性管理 UI 属后续批次，此处不假装已有"从属性集选轴"的能力。
  let axes = $state<AxisDraft[]>([
    { code: 'color', options: 'red,blue' },
    { code: 'size', options: 's,m,l' },
  ])

  // 每个变体行的输入，键是签名（如 color=red|size=s）。
  // 用 Record + 手动 oninput 而不是 $bindable 嵌套绑定：签名集合随轴变化增删，
  // 提前初始化所有键会让"删掉轴后又加回来"残留旧值。
  let skuValues = $state<Record<string, string>>({})
  let priceValues = $state<Record<string, string>>({})

  let loading = $state(false)

  const currencyOptions: SelectOption[] = [
    { value: 'CNY', label: 'CNY' },
    { value: 'USD', label: 'USD' },
    { value: 'JPY', label: 'JPY' },
  ]

  // 归一化轴输入：去空白、按逗号/空白切分、丢空值。
  // 过滤掉"有 code 没值"或"有值没 code"的半成品轴——否则会生成一个空维度。
  const parsedAxes = $derived(
    axes
      .map((a) => ({
        code: a.code.trim(),
        options: a.options
          .split(/[,，\s]+/)
          .map((s) => s.trim())
          .filter(Boolean),
      }))
      .filter((a) => a.code.length > 0 && a.options.length > 0)
  )

  // 笛卡尔积：签名的键排序与后端一致（Ordinal 升序，即 Object.keys().sort()），
  // 否则同一个变体在前后端会算出两个不同签名，唯一索引拦不住。
  const combos = $derived.by(() => {
    if (parsedAxes.length === 0) {
      return [{ axes: {} as Record<string, string>, signature: '' }]
    }

    let accumulated: Record<string, string>[] = [{}]
    for (const axis of parsedAxes) {
      const next: Record<string, string>[] = []
      for (const combo of accumulated) {
        for (const option of axis.options) {
          next.push({ ...combo, [axis.code]: option })
        }
      }
      accumulated = next
    }

    return accumulated.map((combo) => ({
      axes: combo,
      signature: Object.keys(combo)
        .sort()
        .map((key) => `${key}=${combo[key]}`)
        .join('|'),
    }))
  })

  function addAxis() {
    if (axes.length >= 3) return
    axes = [...axes, { code: '', options: '' }]
  }

  function removeAxis(index: number) {
    axes = axes.filter((_, i) => i !== index)
  }

  // SKU 留空时的默认值：slug + 各轴取值大写。让"只想快速建一批"的场景不必逐行敲 SKU，
  // 同时又保证后端 SKU 唯一（同一商品内各组合的取值不同，拼出来天然不同）。
  function autoSku(comboAxes: Record<string, string>): string {
    const values = Object.keys(comboAxes)
      .sort()
      .map((key) => comboAxes[key].toUpperCase())
    const root = slug.trim().toUpperCase()
    return values.length === 0 ? root : `${root}-${values.join('-')}`
  }

  async function onSubmit(e: SubmitEvent) {
    e.preventDefault()
    if (loading) return

    const trimmedSlug = slug.trim()
    if (!trimmedSlug || !title.trim()) {
      toast('error', t('common.state.error'))
      return
    }

    const variants: CreateProductRequest['variants'] = []
    for (const combo of combos) {
      const rawPrice = (priceValues[combo.signature] ?? '').trim()
      const amount = Number(rawPrice)

      // 价格必须 > 0：后端 CK-3 会拦 AmountMinor <= 0，这里提前给出可读提示，
      // 否则用户看到的是"保存失败"而不知道是哪一行。
      if (!rawPrice || Number.isNaN(amount) || amount <= 0) {
        toast('error', `${t('admin.products.price')}: ${combo.signature || t('admin.products.matrixEmpty')}`)
        return
      }

      const hasAxes = Object.keys(combo.axes).length > 0
      variants.push({
        sku: (skuValues[combo.signature] ?? '').trim() || autoSku(combo.axes),
        ...(hasAxes ? { axes: combo.axes } : {}),
        price: { amountMinor: majorToMinor(amount, currency), currency },
      })
    }

    loading = true
    try {
      // 契约：POST /api/admin/products。variants[].axes 可空 = 单规格商品。
      const body: CreateProductRequest = {
        slug: trimmedSlug,
        title: title.trim(),
        variants,
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

    <div class="panel">
      <div class="panel__head">
        <span class="panel__title">{t('admin.products.axes')}</span>
        <Button variant="secondary" size="sm" onclick={addAxis} disabled={axes.length >= 3}>
          {t('admin.products.addAxis')}
        </Button>
      </div>

      {#each axes as axis, index (index)}
        <div class="axis-row">
          <Field label={t('admin.products.axisCode')} for={`axis-code-${index}`}>
            <Input name={`axis-code-${index}`} bind:value={axis.code} placeholder="color" />
          </Field>
          <Field label={t('admin.products.axisOptions')} for={`axis-opt-${index}`}>
            <Input name={`axis-opt-${index}`} bind:value={axis.options} placeholder="red,blue" />
          </Field>
          <Button variant="ghost" size="sm" onclick={() => removeAxis(index)}>
            {t('admin.products.removeAxis')}
          </Button>
        </div>
      {/each}

      {#if parsedAxes.length === 0}
        <p class="hint">{t('admin.products.matrixEmpty')}</p>
      {/if}
    </div>

    <div class="panel">
      <div class="panel__head">
        <span class="panel__title">{t('admin.products.matrix')}</span>
        <span class="count">{combos.length}</span>
      </div>

      <div class="matrix">
        <table>
          <thead>
            <tr>
              <th class="col-sig">#</th>
              <th>{t('admin.products.sku')}</th>
              <th class="col-price">{t('admin.products.price')}</th>
            </tr>
          </thead>
          <tbody>
            {#each combos as combo, index (combo.signature)}
              <tr>
                <td class="col-sig">
                  <span class="sig__index">{index + 1}</span>
                  <code class="sig__text">{combo.signature || '—'}</code>
                </td>
                <td>
                  <Input
                    name={`sku-${index}`}
                    value={skuValues[combo.signature] ?? ''}
                    placeholder={autoSku(combo.axes)}
                    oninput={(e) => (skuValues[combo.signature] = e.currentTarget.value)}
                  />
                </td>
                <td class="col-price">
                  <Input
                    name={`price-${index}`}
                    type="number"
                    min="0"
                    step="0.01"
                    value={priceValues[combo.signature] ?? ''}
                    oninput={(e) => (priceValues[combo.signature] = e.currentTarget.value)}
                  />
                </td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>

      <p class="hint">{t('admin.products.autoSku')}</p>
    </div>

    <Field label="Currency" for="currency">
      <Select name="currency" options={currencyOptions} bind:value={currency} />
    </Field>

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
    max-width: 52rem;
    padding: var(--space-5);
    background: var(--color-surface-raised);
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
  }
  .panel {
    padding: var(--space-4);
    border: 1px dashed var(--color-border-strong);
    border-radius: var(--radius-md);
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
  }
  .panel__head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: var(--space-3);
  }
  .panel__title {
    font-weight: 600;
    font-size: 0.875rem;
  }
  .count {
    font-size: 0.75rem;
    padding: 0.125rem var(--space-2);
    border-radius: var(--radius-sm, 999px);
    background: var(--color-surface);
    color: var(--color-muted);
  }
  .axis-row {
    display: grid;
    grid-template-columns: 1fr 1fr auto;
    gap: var(--space-3);
    align-items: end;
  }
  .matrix {
    overflow-x: auto;
  }
  table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.875rem;
  }
  th {
    text-align: left;
    font-weight: 500;
    font-size: 0.75rem;
    color: var(--color-muted);
    padding: var(--space-2);
    border-bottom: 1px solid var(--color-border);
    white-space: nowrap;
  }
  td {
    padding: var(--space-2);
    border-bottom: 1px solid var(--color-border);
    vertical-align: middle;
  }
  .col-sig {
    width: 12rem;
  }
  .col-price {
    width: 10rem;
  }
  .sig__index {
    display: inline-block;
    min-width: 1.25rem;
    color: var(--color-muted);
    font-size: 0.75rem;
  }
  .sig__text {
    font-family: var(--font-mono, monospace);
    font-size: 0.75rem;
    color: var(--color-muted);
  }
  .hint {
    margin: 0;
    font-size: 0.75rem;
    color: var(--color-muted);
  }
</style>
