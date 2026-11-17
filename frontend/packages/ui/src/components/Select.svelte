<script lang="ts">
  export interface SelectOption {
    value: string
    label: string
    disabled?: boolean
  }

  let {
    options = [],
    value = $bindable(''),
    placeholder = undefined,
    disabled = false,
    invalid = false,
    required = false,
    name = undefined,
    class: klass = '',
    onchange = undefined,
  }: {
    options?: SelectOption[]
    value?: string
    placeholder?: string
    disabled?: boolean
    invalid?: boolean
    required?: boolean
    name?: string
    class?: string
    onchange?: (e: Event & { currentTarget: HTMLSelectElement }) => void
  } = $props()

  const classes = $derived(`select__control${invalid ? ' is-invalid' : ''}${klass ? ` ${klass}` : ''}`)

  function handleChange(e: Event & { currentTarget: HTMLSelectElement }) {
    value = e.currentTarget.value
    onchange?.(e)
  }
</script>

<select
  {name}
  {disabled}
  {required}
  onchange={handleChange}
  value={value}
  class={classes}
  aria-invalid={invalid || undefined}
>
  {#if placeholder}
    <option value="" disabled>{placeholder}</option>
  {/if}
  {#each options as opt (opt.value)}
    <option value={opt.value} disabled={opt.disabled ?? false}>{opt.label}</option>
  {/each}
</select>

<style>
  .select__control {
    height: 2.375rem;
    padding: 0 var(--space-3);
    border: 1px solid var(--color-border-strong);
    border-radius: var(--radius-md);
    background: var(--color-surface-raised);
    color: var(--color-fg);
    font-family: var(--font-sans);
    font-size: 0.875rem;
    transition: border-color 120ms ease, box-shadow 120ms ease;
  }
  .select__control:focus {
    outline: none;
    border-color: var(--color-primary);
    box-shadow: var(--focus-ring);
  }
  .select__control.is-invalid {
    border-color: var(--color-danger);
  }
</style>
