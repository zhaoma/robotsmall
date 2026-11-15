<script lang="ts">
  import { goto } from '$app/navigation'
  import { base } from '$lib/paths'
  import { page } from '$app/state'
  import { Button } from '@openstore/ui'
  import { apiFetch } from '$lib/api'
  import { t } from '$lib/i18n'
  import type { AdminUserDto } from '@openstore/sdk'
  import type { Snippet } from 'svelte'

  let { data, children }: { data: { me: AdminUserDto | null }; children: Snippet } = $props()

  interface NavItem {
    href: string
    labelKey: Parameters<typeof t>[0]
  }
  interface NavGroup {
    label?: string
    items: NavItem[]
  }

  const groups: NavGroup[] = [
    { items: [{ href: `${base}/`, labelKey: 'admin.nav.dashboard' }] },
    {
      items: [
        { href: `${base}/products`, labelKey: 'admin.nav.products' },
        { href: `${base}/categories`, labelKey: 'admin.nav.categories' },
        { href: `${base}/attributes`, labelKey: 'admin.nav.attributes' },
        { href: `${base}/inventory`, labelKey: 'admin.nav.inventory' },
      ],
    },
    {
      items: [
        { href: `${base}/orders`, labelKey: 'admin.nav.orders' },
        { href: `${base}/returns`, labelKey: 'admin.nav.returns' },
        { href: `${base}/customers`, labelKey: 'admin.nav.customers' },
      ],
    },
    {
      label: t('admin.nav.content'),
      items: [
        { href: `${base}/content/themes`, labelKey: 'admin.nav.themes' },
        { href: `${base}/content/pages`, labelKey: 'admin.nav.pages' },
      ],
    },
    {
      label: t('admin.nav.i18n'),
      items: [
        { href: `${base}/i18n/ui-texts`, labelKey: 'admin.nav.uiTexts' },
        { href: `${base}/i18n/content`, labelKey: 'admin.nav.contentTranslations' },
        { href: `${base}/i18n/coverage`, labelKey: 'admin.nav.coverage' },
      ],
    },
    {
      label: t('admin.nav.settings'),
      items: [
        { href: `${base}/settings`, labelKey: 'admin.nav.settings' },
        { href: `${base}/settings/users`, labelKey: 'admin.nav.users' },
        { href: `${base}/settings/audit`, labelKey: 'admin.nav.audit' },
      ],
    },
  ]

  let loggingOut = $state(false)

  async function logout() {
    if (loggingOut) return
    loggingOut = true
    try {
      await apiFetch('/api/admin/auth/logout', { method: 'POST' })
    } finally {
      await goto(`${base}/login`)
    }
  }
</script>

<div class="shell">
  <aside class="sidebar">
    <div class="sidebar__brand">OpenStore</div>
    <nav class="sidebar__nav">
      {#each groups as group (group.label ?? group.items[0]?.href)}
        {#if group.label}<div class="sidebar__group">{group.label}</div>{/if}
        {#each group.items as item (item.href)}
          <a
            class="sidebar__link"
            class:is-active={page.url.pathname === item.href}
            href={item.href}
          >
            {t(item.labelKey)}
          </a>
        {/each}
      {/each}
    </nav>
  </aside>

  <div class="shell__main">
    <header class="topbar">
      <div class="topbar__title">OpenStore Admin</div>
      <div class="topbar__right">
        {#if data.me}<span class="topbar__email">{data.me.email}</span>{/if}
        <Button variant="ghost" size="sm" onclick={logout} loading={loggingOut}>
          {t('common.action.signOut')}
        </Button>
      </div>
    </header>

    <div class="demo-banner">
      {t('admin.dashboard.demoBanner')}
    </div>

    <main class="content">
      {@render children()}
    </main>
  </div>
</div>

<style>
  .shell {
    display: flex;
    min-height: 100vh;
  }
  .sidebar {
    width: 13rem;
    flex: none;
    display: flex;
    flex-direction: column;
    background: var(--color-surface-raised);
    border-right: 1px solid var(--color-border);
  }
  .sidebar__brand {
    padding: var(--space-4);
    font-size: 1rem;
    font-weight: 700;
    color: var(--color-fg);
    border-bottom: 1px solid var(--color-border);
  }
  .sidebar__nav {
    flex: 1;
    overflow-y: auto;
    padding: var(--space-3);
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .sidebar__group {
    padding: var(--space-3) var(--space-2) var(--space-1);
    font-size: 0.6875rem;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: var(--color-subtle);
  }
  .sidebar__link {
    display: block;
    padding: var(--space-2) var(--space-3);
    border-radius: var(--radius-md);
    color: var(--color-fg);
    font-size: 0.875rem;
  }
  .sidebar__link:hover {
    background: var(--color-surface);
    text-decoration: none;
  }
  .sidebar__link.is-active {
    background: var(--color-primary-soft);
    color: var(--color-primary);
    font-weight: 500;
  }

  .shell__main {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
  }
  .topbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    height: 3.25rem;
    padding: 0 var(--space-5);
    background: var(--color-surface-raised);
    border-bottom: 1px solid var(--color-border);
  }
  .topbar__title {
    font-weight: 600;
    color: var(--color-fg);
  }
  .topbar__right {
    display: flex;
    align-items: center;
    gap: var(--space-3);
  }
  .topbar__email {
    font-size: 0.8125rem;
    color: var(--color-muted);
  }

  .demo-banner {
    padding: var(--space-2) var(--space-5);
    background: var(--color-warning-bg);
    color: var(--color-warning);
    font-size: 0.8125rem;
    border-bottom: 1px solid var(--color-border);
  }

  .content {
    flex: 1;
    padding: var(--space-6);
  }
</style>
