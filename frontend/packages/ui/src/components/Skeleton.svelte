<script lang="ts">
  let {
    width = undefined,
    height = undefined,
    circle = false,
    lines = undefined,
  }: {
    width?: string
    height?: string
    circle?: boolean
    lines?: number
  } = $props()
</script>

{#if lines != null}
  <div class="skeleton-lines">
    {#each Array(lines) as _, i (i)}
      <div class="skeleton skeleton--line" style:width={i === lines - 1 ? '60%' : '100%'}></div>
    {/each}
  </div>
{:else}
  <div
    class="skeleton"
    class:skeleton--circle={circle}
    style:width={width ?? '100%'}
    style:height={height ?? '1rem'}
  ></div>
{/if}

<style>
  .skeleton {
    border-radius: var(--radius-md);
    background: var(--color-surface);
    animation: os-pulse 1.4s ease-in-out infinite;
  }
  .skeleton--circle { border-radius: 50%; }
  .skeleton-lines {
    display: flex;
    flex-direction: column;
    gap: var(--space-2);
  }
  .skeleton--line { height: 0.875rem; }
  @keyframes os-pulse {
    0%, 100% { opacity: 1; }
    50% { opacity: 0.5; }
  }
</style>
