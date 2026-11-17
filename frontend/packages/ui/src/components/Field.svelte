<script lang="ts">
  import type { Snippet } from 'svelte'

  let {
    label = undefined,
    required = false,
    hint = undefined,
    error = undefined,
    for: htmlFor = undefined,
    children,
  }: {
    label?: string
    required?: boolean
    hint?: string
    error?: string
    for?: string
    children: Snippet
  } = $props()

  const describedBy = $derived(
    [hint ? 'field__hint' : null, error ? 'field__error' : null].filter(Boolean).join(' ') || undefined
  )
</script>

<div class="field">
  {#if label}
    <label class="field__label" for={htmlFor}>
      {label}
      {#if required}<span class="field__req" aria-hidden="true">*</span>{/if}
    </label>
  {/if}
  {@render children()}
  {#if hint}<p class="field__hint" id="field__hint">{hint}</p>{/if}
  {#if error}<p class="field__error" id="field__error" role="alert">{error}</p>{/if}
</div>

<style>
  .field {
    display: flex;
    flex-direction: column;
    gap: var(--space-2);
  }
  .field__label {
    font-size: 0.8125rem;
    font-weight: 500;
    color: var(--color-fg);
  }
  .field__req {
    margin-left: 2px;
    color: var(--color-danger);
  }
  .field__hint {
    margin: 0;
    font-size: 0.75rem;
    color: var(--color-muted);
  }
  .field__error {
    margin: 0;
    font-size: 0.75rem;
    color: var(--color-danger);
  }
</style>
