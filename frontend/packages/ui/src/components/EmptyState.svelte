<script lang="ts">
  import type { Snippet } from 'svelte'

  let {
    title = '',
    description = undefined,
    action = undefined,
    secondaryAction = undefined,
  }: {
    title?: string
    description?: string
    action?: Snippet
    secondaryAction?: Snippet
  } = $props()
</script>

<div class="empty">
  <h3 class="empty__title">{title}</h3>
  {#if description}<p class="empty__desc">{description}</p>{/if}
  <div class="empty__actions">
    {#if action}{@render action()}{/if}
    {#if secondaryAction}{@render secondaryAction()}{/if}
  </div>
</div>

<style>
  .empty {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: var(--space-2);
    padding: var(--space-8);
    text-align: center;
  }
  .empty__title {
    margin: 0;
    font-size: 0.9375rem;
    font-weight: 600;
    color: var(--color-fg);
  }
  .empty__desc {
    margin: 0;
    font-size: 0.8125rem;
    color: var(--color-muted);
  }
  .empty__actions {
    display: flex;
    gap: var(--space-2);
    margin-top: var(--space-3);
  }
</style>
