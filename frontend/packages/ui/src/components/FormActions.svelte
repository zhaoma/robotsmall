<script lang="ts">
  import type { Snippet } from 'svelte'

  let {
    sticky = false,
    children,
  }: {
    sticky?: boolean
    children: Snippet
  } = $props()
</script>

<div class="form-actions" class:is-sticky={sticky}>
  {@render children()}
</div>

<style>
  .form-actions {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: var(--space-3);
    padding: var(--space-4) 0;
  }
  .form-actions.is-sticky {
    position: sticky;
    bottom: 0;
    z-index: var(--z-sticky);
    background: var(--color-surface-raised);
    border-top: 1px solid var(--color-border);
  }
</style>
