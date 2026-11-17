<script lang="ts">
  import type { Snippet } from 'svelte'
  import { onMount } from 'svelte'

  let {
    open = false,
    title = '',
    size = 'md',
    dismissable = true,
    onclose = undefined,
    children,
  }: {
    open?: boolean
    title?: string
    size?: 'sm' | 'md' | 'lg'
    dismissable?: boolean
    onclose?: () => void
    children: Snippet
  } = $props()

  let rootEl: HTMLElement | undefined = $state()

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape' && dismissable) onclose?.()
  }

  function onBackdropClick(e: MouseEvent) {
    if (e.target === rootEl && dismissable) onclose?.()
  }

  $effect(() => {
    if (open && rootEl) {
      // 焦点陷阱的最小实现：把焦点移入对话框，Esc 关闭（见前端设计 §10.2）
      rootEl.querySelector<HTMLElement>('input, select, textarea, button')?.focus()
    }
  })
</script>

{#if open}
  <div class="modal-backdrop" role="presentation" onclick={onBackdropClick} onkeydown={onKeydown} bind:this={rootEl}>
    <div class="modal modal--{size}" role="dialog" aria-modal="true" aria-label={title || undefined}>
      {#if title}<header class="modal__head"><h2 class="modal__title">{title}</h2></header>{/if}
      <div class="modal__body">
        {@render children()}
      </div>
    </div>
  </div>
{/if}

<style>
  .modal-backdrop {
    position: fixed;
    inset: 0;
    z-index: var(--z-modal);
    display: flex;
    align-items: flex-start;
    justify-content: center;
    padding: var(--space-8) var(--space-4);
    background: color-mix(in srgb, var(--color-fg) 40%, transparent);
  }
  .modal {
    width: 100%;
    max-height: 100%;
    overflow: auto;
    background: var(--color-surface-raised);
    border-radius: var(--radius-lg);
    box-shadow: var(--shadow-lg);
  }
  .modal--sm { max-width: 24rem; }
  .modal--md { max-width: 32rem; }
  .modal--lg { max-width: 48rem; }
  .modal__head {
    padding: var(--space-4) var(--space-5);
    border-bottom: 1px solid var(--color-border);
  }
  .modal__title {
    margin: 0;
    font-size: 1rem;
    font-weight: 600;
    color: var(--color-fg);
  }
  .modal__body {
    padding: var(--space-5);
  }
</style>
