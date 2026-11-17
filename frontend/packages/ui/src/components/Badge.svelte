<script lang="ts">
  import type { Snippet } from 'svelte'

  export type BadgeTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger'

  let {
    tone = 'neutral',
    dot = false,
    children,
  }: {
    tone?: BadgeTone
    /** 前置小圆点：状态类徽章加点上点更易扫读 */
    dot?: boolean
    children: Snippet
  } = $props()
</script>

<span class="badge badge--{tone}">{#if dot}<i class="badge__dot"></i>{/if}{@render children()}</span>

<style>
  .badge {
    display: inline-flex;
    align-items: center;
    gap: var(--space-1);
    height: 1.375rem;
    padding: 0 var(--space-2);
    border-radius: 999px;
    font-size: 0.75rem;
    font-weight: 500;
    line-height: 1;
    white-space: nowrap;
  }
  .badge__dot {
    width: 4px;
    height: 4px;
    border-radius: 50%;
    background: currentColor;
    flex: none;
  }
  .badge--neutral { background: var(--color-surface); color: var(--color-muted); }
  .badge--info { background: var(--color-info-bg); color: var(--color-info); }
  .badge--success { background: var(--color-success-bg); color: var(--color-success); }
  .badge--warning { background: var(--color-warning-bg); color: var(--color-warning); }
  .badge--danger { background: var(--color-danger-bg); color: var(--color-danger); }
</style>
