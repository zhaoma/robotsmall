<script lang="ts">
  import { Badge } from '@openstore/ui'
  import type { ProductStatus } from '@openstore/sdk'
  import { t } from '$lib/i18n'

  let { status }: { status: ProductStatus } = $props()

  const tone = $derived(
    status === 'Active' ? 'success' : status === 'Archived' ? 'danger' : 'neutral'
  )
  const label = $derived(
    status === 'Active'
      ? t('admin.products.active')
      : status === 'Archived'
        ? t('admin.products.archived')
        : t('admin.products.draft')
  )
</script>

<Badge {tone}>{label}</Badge>
