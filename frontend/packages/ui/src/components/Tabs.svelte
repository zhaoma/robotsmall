<script lang="ts">
  export interface TabItem {
    key: string
    label: string
    badge?: number
    disabled?: boolean
  }

  let {
    tabs = [],
    value = '',
    onChange = undefined,
  }: {
    tabs?: TabItem[]
    value?: string
    onChange?: (key: string) => void
  } = $props()

  let listEl: HTMLElement | undefined = $state()

  function onKeydown(e: KeyboardEvent) {
    const enabled = tabs.filter((t) => !t.disabled)
    const idx = enabled.findIndex((t) => t.key === value)
    let next: number | null = null
    if (e.key === 'ArrowRight') next = (idx + 1) % enabled.length
    else if (e.key === 'ArrowLeft') next = (idx - 1 + enabled.length) % enabled.length
    else if (e.key === 'Home') next = 0
    else if (e.key === 'End') next = enabled.length - 1
    if (next != null) {
      e.preventDefault()
      const target = enabled[next]
      if (target) onChange?.(target.key)
      listEl?.querySelectorAll<HTMLElement>('[role="tab"]')[next ?? 0]?.focus()
    }
  }
</script>

<div class="tabs" role="tablist" tabindex="0" bind:this={listEl} onkeydown={onKeydown}>
  {#each tabs as tab (tab.key)}
    <button
      class="tab"
      class:is-active={tab.key === value}
      role="tab"
      aria-selected={tab.key === value}
      tabindex={tab.key === value ? 0 : -1}
      disabled={tab.disabled ?? false}
      onclick={() => onChange?.(tab.key)}
    >
      {tab.label}
      {#if tab.badge != null}<span class="tab__badge">{tab.badge}</span>{/if}
    </button>
  {/each}
</div>

<style>
  .tabs {
    display: flex;
    gap: var(--space-1);
    border-bottom: 1px solid var(--color-border);
  }
  .tab {
    position: relative;
    padding: var(--space-2) var(--space-3);
    border: none;
    background: transparent;
    color: var(--color-muted);
    font-family: var(--font-sans);
    font-size: 0.875rem;
    font-weight: 500;
    cursor: pointer;
  }
  .tab:hover:not(:disabled) { color: var(--color-fg); }
  .tab:disabled { opacity: 0.4; cursor: not-allowed; }
  .tab.is-active {
    color: var(--color-primary);
  }
  .tab.is-active::after {
    content: '';
    position: absolute;
    left: 0;
    right: 0;
    bottom: -1px;
    height: 2px;
    background: var(--color-primary);
  }
  .tab:focus-visible {
    outline: none;
    box-shadow: var(--focus-ring);
    border-radius: var(--radius-sm);
  }
  .tab__badge {
    margin-left: var(--space-1);
    padding: 0 var(--space-1);
    border-radius: 999px;
    background: var(--color-danger-bg);
    color: var(--color-danger);
    font-size: 0.6875rem;
  }
</style>
