<script lang="ts">
  let {
    value = $bindable(''),
    rows = 3,
    placeholder = undefined,
    invalid = false,
    disabled = false,
    required = false,
    name = undefined,
    maxlength = undefined,
    oninput = undefined,
  }: {
    value?: string
    rows?: number
    placeholder?: string
    invalid?: boolean
    disabled?: boolean
    required?: boolean
    name?: string
    maxlength?: number
    oninput?: (e: Event & { currentTarget: HTMLTextAreaElement }) => void
  } = $props()

  const classes = $derived(`field__control${invalid ? ' is-invalid' : ''}`)

  function handleInput(e: Event & { currentTarget: HTMLTextAreaElement }) {
    value = e.currentTarget.value
    oninput?.(e)
  }
</script>

<textarea
  {name}
  {rows}
  {placeholder}
  {maxlength}
  {disabled}
  {required}
  oninput={handleInput}
  value={value}
  class={classes}
  aria-invalid={invalid || undefined}
></textarea>

<style>
  .field__control {
    width: 100%;
    padding: var(--space-3);
    border: 1px solid var(--color-border-strong);
    border-radius: var(--radius-md);
    background: var(--color-surface-raised);
    color: var(--color-fg);
    font-family: var(--font-sans);
    font-size: 0.875rem;
    line-height: 1.5;
    resize: vertical;
    transition: border-color 120ms ease, box-shadow 120ms ease;
  }
  .field__control:focus {
    outline: none;
    border-color: var(--color-primary);
    box-shadow: var(--focus-ring);
  }
  .field__control::placeholder {
    color: var(--color-subtle);
  }
  .field__control.is-invalid {
    border-color: var(--color-danger);
  }
</style>
