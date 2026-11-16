<script lang="ts">
  import type { IconName } from './icons'

  let { name, size = 17 }: { name: IconName; size?: number } = $props()
</script>

<svg class="ico" width={size} height={size} aria-hidden="true"><use href="#i-{name}" /></svg>

<style>
  .ico {
    flex: none;
    stroke: currentColor;
    fill: none;
    stroke-width: 1.7;
    stroke-linecap: round;
    stroke-linejoin: round;
  }
</style>
