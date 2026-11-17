<script lang="ts">
  import { toastItems, dismissToast } from '../toast.svelte.js'
</script>

<div class="toast-region" aria-live="polite" role="status">
  {#each toastItems.value as item (item.id)}
    <div class="toast toast--{item.kind}">
      <span class="toast__msg">{item.message}</span>
      <button class="toast__close" onclick={() => dismissToast(item.id)} aria-label="close">×</button>
    </div>
  {/each}
</div>

<style>
  .toast-region {
    position: fixed;
    right: var(--space-4);
    bottom: var(--space-4);
    z-index: var(--z-toast);
    display: flex;
    flex-direction: column;
    gap: var(--space-2);
    max-width: 22rem;
  }
  .toast {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    padding: var(--space-3) var(--space-4);
    border-radius: var(--radius-md);
    background: var(--color-surface-raised);
    border: 1px solid var(--color-border);
    box-shadow: var(--shadow-md);
    font-size: 0.8125rem;
    color: var(--color-fg);
  }
  .toast--success { border-left: 3px solid var(--color-success); }
  .toast--error { border-left: 3px solid var(--color-danger); }
  .toast--info { border-left: 3px solid var(--color-info); }
  .toast__msg { flex: 1; }
  .toast__close {
    border: none;
    background: transparent;
    color: var(--color-muted);
    cursor: pointer;
    font-size: 1rem;
    line-height: 1;
  }
  .toast__close:hover { color: var(--color-fg); }
</style>
