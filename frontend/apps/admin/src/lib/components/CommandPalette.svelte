<script lang="ts">
  import { goto } from '$app/navigation'
  import { t } from '#lib/i18n'
  import { apiFetch } from '#lib/api'
  import { domains } from '#lib/nav'
  import { base } from '#lib/paths'
  import Icon from './Icon.svelte'
  import type { AdminProductListDto, Paged } from '@openstore/sdk'

  let { open = $bindable(false) }: { open?: boolean } = $props()

  interface Entry {
    kind: 'nav' | 'product'
    label: string
    href: string
    meta?: string
  }

  /** 导航项在打开时就固定（域内页面是静态清单），不做实时过滤以外的计算。 */
  const navEntries: Entry[] = domains.flatMap((d) =>
    d.items.length > 0
      ? d.items.map((i) => ({ kind: 'nav' as const, label: t(i.labelKey), href: i.href, meta: t(d.labelKey) }))
      : [{ kind: 'nav' as const, label: t(d.labelKey), href: d.href ?? '', meta: '' }]
  )

  let q = $state('')
  let cursor = $state(0)
  let hits = $state<AdminProductListDto[]>([])
  let searching = $state(false)
  let inputEl = $state<HTMLInputElement | null>(null)

  const navFiltered = $derived(
    q.trim() ? navEntries.filter((e) => e.label.toLowerCase().includes(q.trim().toLowerCase())) : navEntries
  )

  const entries = $derived<Entry[]>([
    ...navFiltered,
    ...hits.map((p) => ({ kind: 'product' as const, label: p.title, href: `${base}/products/${p.id}`, meta: p.slug })),
  ])

  // 打开时聚焦输入；关闭时清空，避免下次打开还留着上一次的查询
  $effect(() => {
    if (open) inputEl?.focus()
    else {
      q = ''
      hits = []
      cursor = 0
    }
  })

  // 输防抖 220ms：只有关键词才打后端，命令走本地过滤
  $effect(() => {
    const term = q.trim()
    if (term.length === 0) {
      hits = []
      searching = false
      return
    }
    const ctrl = new AbortController()
    searching = true
    const timer = setTimeout(async () => {
      try {
        const res = await apiFetch<Paged<AdminProductListDto>>(
          `/api/admin/products?q=${encodeURIComponent(term)}&pageSize=5&page=1`,
          { signal: ctrl.signal }
        )
        hits = res.items
      } catch {
        hits = []
      } finally {
        searching = false
      }
    }, 220)
    return () => {
      clearTimeout(timer)
      ctrl.abort()
    }
  })

  // 结果变短时把游标收回界内，否则回车会落到不存在的行
  $effect(() => {
    if (cursor > entries.length - 1) cursor = Math.max(0, entries.length - 1)
  })

  function go(e: Entry) {
    open = false
    goto(e.href)
  }

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') {
      e.preventDefault()
      open = false
    } else if (e.key === 'ArrowDown') {
      e.preventDefault()
      if (entries.length) cursor = (cursor + 1) % entries.length
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      if (entries.length) cursor = (cursor - 1 + entries.length) % entries.length
    } else if (e.key === 'Enter') {
      e.preventDefault()
      const pick = entries[cursor]
      if (pick) go(pick)
    }
  }
</script>

{#if open}
  <div class="palette" role="dialog" aria-modal="true" aria-label={t('admin.shell.searchPlaceholder')}>
    <button class="palette__scrim" aria-label={t('common.action.close')} onclick={() => (open = false)}></button>
    <div class="pal">
      <div class="pal__in">
        <Icon name="search" />
        <input
          bind:this={inputEl}
          bind:value={q}
          onkeydown={onKeydown}
          placeholder={t('admin.shell.searchPlaceholder')}
          autocomplete="off"
          spellcheck="false"
        />
        {#if searching}<span class="pal__spin">{t('common.state.loading')}</span>{/if}
      </div>

      <div class="pal__body">
        {#if entries.length === 0}
          <div class="pal__empty">{t('admin.shell.noResults')}</div>
        {:else}
          {#each entries as e, i (e.kind + e.href)}
            <button class="pal__row" class:is-on={i === cursor} onclick={() => go(e)} onmouseenter={() => (cursor = i)}>
              <Icon name={e.kind === 'product' ? 'box' : 'chevron-right'} size={14} />
              <span class="pal__label">{e.label}</span>
              {#if e.meta}<span class="pal__meta">{e.meta}</span>{/if}
            </button>
          {/each}
        {/if}
      </div>

      <div class="pal__foot">
        <span><kbd>↑</kbd><kbd>↓</kbd></span>
        <span><kbd>↵</kbd>{t('common.action.confirm')}</span>
        <span><kbd>esc</kbd>{t('common.action.close')}</span>
      </div>
    </div>
  </div>
{/if}

<style>
  .palette {
    position: fixed;
    inset: 0;
    z-index: var(--z-modal);
    display: grid;
    place-items: start center;
    padding-top: 14vh;
  }
  .palette__scrim {
    position: absolute;
    inset: 0;
    border: 0;
    padding: 0;
    background: color-mix(in srgb, var(--color-fg) 34%, transparent);
    backdrop-filter: blur(3px);
    cursor: default;
  }
  .pal {
    position: relative;
    width: min(560px, 92vw);
    border-radius: var(--radius-lg);
    border: 1px solid var(--color-border);
    background: var(--color-bg);
    box-shadow: var(--shadow-pop);
    overflow: hidden;
  }
  .pal__in {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    padding: var(--space-3) var(--space-4);
    border-bottom: 1px solid var(--color-border);
    color: var(--color-muted);
  }
  .pal__in input {
    flex: 1;
    border: 0;
    outline: none;
    background: none;
    font: inherit;
    font-size: var(--text-md);
    color: var(--color-fg);
  }
  .pal__in input:focus-visible {
    box-shadow: none;
  }
  .pal__in input::placeholder {
    color: var(--color-subtle);
  }
  .pal__spin {
    font-size: var(--text-2xs);
    color: var(--color-subtle);
  }
  .pal__body {
    max-height: 46vh;
    overflow-y: auto;
    padding: var(--space-1);
  }
  .pal__row {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    width: 100%;
    padding: var(--space-2) var(--space-3);
    border: 0;
    border-radius: var(--radius-sm);
    background: none;
    color: var(--color-fg);
    font: inherit;
    font-size: var(--text-sm);
    text-align: left;
    cursor: pointer;
  }
  .pal__row :global(svg) {
    color: var(--color-muted);
  }
  .pal__row.is-on {
    background: var(--color-hover);
    box-shadow: inset 2px 0 0 var(--color-primary);
  }
  .pal__label {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .pal__meta {
    margin-left: auto;
    font-family: var(--font-mono);
    font-size: var(--text-2xs);
    color: var(--color-subtle);
  }
  .pal__empty {
    padding: var(--space-6);
    text-align: center;
    font-size: var(--text-sm);
    color: var(--color-muted);
  }
  .pal__foot {
    display: flex;
    gap: var(--space-4);
    padding: var(--space-2) var(--space-4);
    border-top: 1px solid var(--color-border);
    background: var(--color-surface);
    font-size: var(--text-2xs);
    color: var(--color-muted);
  }
  .pal__foot kbd {
    font-family: var(--font-mono);
    font-size: var(--text-2xs);
    padding: 1px 4px;
    margin-right: 3px;
    border-radius: var(--radius-sm);
    background: var(--color-bg);
    border: 1px solid var(--color-border);
  }
</style>
