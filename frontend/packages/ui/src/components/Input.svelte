<script lang="ts">
  import type { HTMLInputAttributes } from 'svelte/elements'

  let {
    value = $bindable(''),
    type = 'text',
    placeholder = undefined,
    invalid = false,
    disabled = false,
    required = false,
    name = undefined,
    autocomplete = undefined,
    maxlength = undefined,
    min = undefined,
    step = undefined,
    prefix = undefined,
    suffix = undefined,
    class: klass = '',
    oninput = undefined,
    onchange = undefined,
    onkeydown = undefined,
  }: {
    value?: string
    type?: string
    placeholder?: string
    invalid?: boolean
    disabled?: boolean
    required?: boolean
    name?: string
    autocomplete?: HTMLInputAttributes['autocomplete']
    maxlength?: number
    min?: string | number
    step?: string | number
    prefix?: string
    suffix?: string
    class?: string
    oninput?: (e: Event & { currentTarget: HTMLInputElement }) => void
    onchange?: (e: Event) => void
    onkeydown?: (e: KeyboardEvent) => void
  } = $props()

  const classes = $derived(`field__control${invalid ? ' is-invalid' : ''}${klass ? ` ${klass}` : ''}`)

  function handleInput(e: Event & { currentTarget: HTMLInputElement }) {
    value = e.currentTarget.value
    oninput?.(e)
  }
</script>

<div class="input" class:has-prefix={prefix != null} class:has-suffix={suffix != null}>
  {#if prefix}<span class="input__affix" aria-hidden="true">{prefix}</span>{/if}
  <input
    {type}
    {name}
    {placeholder}
    {autocomplete}
    {maxlength}
    {min}
    {step}
    {disabled}
    {required}
    value={value}
    class={classes}
    aria-invalid={invalid || undefined}
    oninput={handleInput}
    {onchange}
    {onkeydown}
  />
  {#if suffix}<span class="input__affix" aria-hidden="true">{suffix}</span>{/if}
</div>

<style>
  .input {
    display: flex;
    align-items: center;
    border: 1px solid var(--color-border-strong);
    border-radius: var(--radius-md);
    background: var(--color-surface-raised);
    transition: border-color 120ms ease, box-shadow 120ms ease;
  }
  .input:focus-within {
    border-color: var(--color-primary);
    box-shadow: var(--focus-ring);
  }
  .input__affix {
    padding: 0 var(--space-3);
    color: var(--color-muted);
    font-size: 0.875rem;
  }
  .field__control {
    flex: 1;
    min-width: 0;
    height: 2.375rem;
    padding: 0 var(--space-3);
    border: none;
    background: transparent;
    color: var(--color-fg);
    font-family: var(--font-sans);
    font-size: 0.875rem;
  }
  .field__control:focus {
    outline: none;
  }
  .field__control::placeholder {
    color: var(--color-subtle);
  }
  .field__control.is-invalid {
    color: var(--color-danger);
  }
</style>
