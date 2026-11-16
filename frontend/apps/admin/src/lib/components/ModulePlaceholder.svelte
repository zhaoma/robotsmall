<script lang="ts">
  import { EmptyState } from '@openstore/ui'
  import { t } from '#lib/i18n'

  let { title }: { title: string } = $props()
</script>

<div class="module-placeholder">
  <h1 class="module-placeholder__title">{title}</h1>
  <EmptyState title={t('common.placeholder.notImplemented')} description={t('common.placeholder.comingSoon')} />
</div>

<style>
  .module-placeholder {
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
  }
  .module-placeholder__title {
    font-size: 1.25rem;
    font-weight: 600;
  }
</style>
