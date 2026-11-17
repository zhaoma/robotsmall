<script lang="ts">
  import type { Snippet } from 'svelte'

  type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'
  type Size = 'sm' | 'md' | 'lg'

  let {
    variant = 'primary',
    size = 'md',
    type = 'button',
    href = undefined,
    loading = false,
    disabled = false,
    block = false,
    title = undefined,
    class: klass = '',
    onclick = undefined,
    children,
  }: {
    variant?: Variant
    size?: Size
    type?: 'button' | 'submit' | 'reset'
    href?: string
    loading?: boolean
    disabled?: boolean
    block?: boolean
    title?: string
    class?: string
    onclick?: (e: MouseEvent) => void
    children: Snippet
  } = $props()

  const classes = $derived(
    `btn btn--${variant} btn--${size}${block ? ' btn--block' : ''}${loading ? ' is-loading' : ''}${klass ? ` ${klass}` : ''}`
  )
</script>

{#if href}
  <a {href} class={classes} {title} aria-disabled={disabled || loading}>
    {#if loading}<span class="btn__spin" aria-hidden="true"></span>{/if}
    <span class="btn__label">{@render children()}</span>
  </a>
{:else}
  <button {type} class={classes} {onclick} {title} disabled={disabled || loading}>
    {#if loading}<span class="btn__spin" aria-hidden="true"></span>{/if}
    <span class="btn__label">{@render children()}</span>
  </button>
{/if}

<style>
  .btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: var(--space-2);
    font-family: var(--font-sans);
    font-size: 0.875rem;
    font-weight: 500;
    line-height: 1.25rem;
    border-radius: var(--radius-md);
    border: 1px solid transparent;
    cursor: pointer;
    white-space: nowrap;
    transition: background-color 120ms ease, border-color 120ms ease;
  }
  .btn:focus-visible {
    outline: none;
    box-shadow: var(--focus-ring);
  }
  .btn:disabled,
  .btn[aria-disabled='true'] {
    opacity: 0.5;
    cursor: not-allowed;
    pointer-events: none;
  }

  .btn--sm { height: 2rem; padding: 0 var(--space-3); font-size: 0.8125rem; }
  .btn--md { height: 2.375rem; padding: 0 var(--space-4); }
  .btn--lg { height: 2.75rem; padding: 0 var(--space-5); }
  .btn--block { width: 100%; }

  .btn--primary {
    background: var(--color-primary);
    color: var(--color-primary-fg);
  }
  .btn--primary:hover:not(:disabled) { background: var(--color-primary-hover); }

  .btn--secondary {
    background: var(--color-surface-raised);
    color: var(--color-fg);
    border-color: var(--color-border-strong);
  }
  .btn--secondary:hover:not(:disabled) { background: var(--color-surface); }

  .btn--ghost {
    background: transparent;
    color: var(--color-fg);
  }
  .btn--ghost:hover:not(:disabled) { background: var(--color-surface); }

  .btn--danger {
    background: var(--color-danger);
    color: var(--color-danger-fg);
  }
  .btn--danger:hover:not(:disabled) { background: var(--color-danger-hover); }

  .btn__spin {
    width: 0.875rem;
    height: 0.875rem;
    border-radius: 50%;
    border: 2px solid currentColor;
    border-top-color: transparent;
    animation: os-spin 600ms linear infinite;
  }
  @keyframes os-spin {
    to { transform: rotate(360deg); }
  }
</style>
