<script lang="ts">
  let {
    checked = false,
    label = undefined,
    disabled = false,
    onchange = undefined,
  }: {
    checked?: boolean
    label?: string
    disabled?: boolean
    onchange?: (checked: boolean) => void
  } = $props()
</script>

<label class="switch" class:is-disabled={disabled}>
  <button
    type="button"
    role="switch"
    aria-checked={checked}
    aria-label={label ?? 'toggle'}
    {disabled}
    class="switch__track"
    onclick={() => onchange?.(!checked)}
  >
    <span class="switch__thumb"></span>
  </button>
  {#if label}<span class="switch__label">{label}</span>{/if}
</label>

<style>
  .switch {
    display: inline-flex;
    align-items: center;
    gap: var(--space-2);
    cursor: pointer;
  }
  .switch.is-disabled { opacity: 0.5; cursor: not-allowed; }
  .switch__track {
    position: relative;
    width: 2.25rem;
    height: 1.25rem;
    padding: 0;
    border: none;
    border-radius: 999px;
    background: var(--color-border-strong);
    cursor: pointer;
    transition: background-color 120ms ease;
  }
  .switch__track[aria-checked='true'] {
    background: var(--color-primary);
  }
  .switch__track:focus-visible {
    outline: none;
    box-shadow: var(--focus-ring);
  }
  .switch__thumb {
    position: absolute;
    top: 2px;
    left: 2px;
    width: 1rem;
    height: 1rem;
    border-radius: 50%;
    background: var(--color-surface-raised);
    transition: transform 120ms ease;
  }
  .switch__track[aria-checked='true'] .switch__thumb {
    transform: translateX(1rem);
  }
  .switch__label {
    font-size: 0.8125rem;
    color: var(--color-fg);
  }
</style>
