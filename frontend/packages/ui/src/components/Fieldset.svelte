<script lang="ts">
  import type { Snippet } from 'svelte'

  let {
    legend = undefined,
    description = undefined,
    children,
  }: {
    legend?: string
    description?: string
    children: Snippet
  } = $props()
</script>

<fieldset class="fieldset">
  {#if legend}<legend class="fieldset__legend">{legend}</legend>{/if}
  {#if description}<p class="fieldset__desc">{description}</p>{/if}
  <div class="fieldset__body">
    {@render children()}
  </div>
</fieldset>

<style>
  .fieldset {
    margin: 0;
    padding: 0;
    border: none;
  }
  .fieldset__legend {
    padding: 0;
    font-size: 0.875rem;
    font-weight: 600;
    color: var(--color-fg);
  }
  .fieldset__desc {
    margin: var(--space-1) 0 0;
    font-size: 0.75rem;
    color: var(--color-muted);
  }
  .fieldset__body {
    margin-top: var(--space-3);
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
  }
</style>
