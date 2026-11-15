<script lang="ts">
  import '@openstore/ui/styles.css'
  import '../app.css'
  import { ToastRegion } from '@openstore/ui'
  import type { Snippet } from 'svelte'

  let { children }: { children: Snippet } = $props()
</script>

{@render children()}
<ToastRegion />
