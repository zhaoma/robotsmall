<script lang="ts">
  import { goto } from '$app/navigation'
  import { base } from '#lib/paths'
  import { page } from '$app/state'
  import { Button } from '@openstore/ui'
  import { apiFetch } from '#lib/api'
  import { t } from '#lib/i18n'
  import { activeDomain, activeItem, domains, isItemActive } from '#lib/nav'
  import Icon from '#lib/components/Icon.svelte'
  import IconSprite from '#lib/components/IconSprite.svelte'
  import CommandPalette from '#lib/components/CommandPalette.svelte'
  import type { AdminAuthResponse } from '@openstore/sdk'
  import type { Snippet } from 'svelte'

  let { data, children }: { data: { me: AdminAuthResponse | null }; children: Snippet } = $props()

  let loggingOut = $state(false)
  let paletteOpen = $state(false)

  const domain = $derived(activeDomain(page.url.pathname))
  const item = $derived(activeItem(page.url.pathname))

  /** 主题 / 密度都落在 <html data-*> 上：token 层只认属性，不认组件状态。 */
  let theme = $state<'light' | 'dark'>('light')
  let density = $state<'comfortable' | 'compact'>('comfortable')

  // 首次挂载读取偏好：$effect 只在浏览器执行，不存在 SSR 访问 localStorage 的问题
  $effect(() => {
    try {
      if (localStorage.getItem('os_admin_theme') === 'dark') theme = 'dark'
      if (localStorage.getItem('os_admin_density') === 'compact') density = 'compact'
    } catch {
      // 隐私模式禁用存储时保持默认偏好
    }
  })

  $effect(() => {
    const root = document.documentElement
    if (theme === 'dark') root.dataset.theme = 'dark'
    else delete root.dataset.theme
    root.dataset.density = density
    try {
      localStorage.setItem('os_admin_theme', theme)
      localStorage.setItem('os_admin_density', density)
    } catch {
      // 隐私模式下 localStorage 可能不可写：偏好只在本次会话生效，不影响功能
    }
  })

  // ⌘K / Ctrl+K 打开命令面板
  $effect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        paletteOpen = true
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  const initials = $derived(
    (data.me?.displayName ?? data.me?.email ?? 'os').slice(0, 2).toUpperCase().replace(/[^A-Z0-9]/g, '') || 'OS'
  )

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

<IconSprite />

<div class="shell">
  <!-- 一级：业务域 -->
  <aside class="rail">
    <div class="rail__mark">OS</div>
    {#each domains as d (d.key)}
      <a
        class="rail__btn"
        href={d.items.length === 0 ? (d.href ?? base) : d.items[0].href}
        aria-current={domain?.key === d.key ? 'true' : undefined}
        title={t(d.labelKey)}
      >
        <Icon name={d.icon} size={19} />
      </a>
    {/each}
    <div class="rail__spacer"></div>
    <div class="rail__ava" title={data.me?.email ?? ''}>{initials}</div>
  </aside>

  <!-- 二级：当前域内的页面 -->
  <nav class="col" aria-label={domain ? t(domain.labelKey) : ''}>
    <div class="col__head">
      <div class="col__title">{domain ? t(domain.labelKey) : t('admin.nav.dashboard')}</div>
      <div class="col__sub">OpenStore · 简体中文</div>
    </div>
    <div class="col__body">
      {#if domain && domain.items.length > 0}
        {#each domain.items as it (it.href)}
          <a class="navi" class:is-active={isItemActive(page.url.pathname, it.href)} href={it.href}>
            <Icon name={it.icon} size={15} />
            {t(it.labelKey)}
          </a>
        {/each}
      {:else}
        <a class="navi" class:is-active={true} href={base}>
          <Icon name="dashboard" size={15} />
          {t('admin.nav.dashboard')}
        </a>
      {/if}
    </div>
    <div class="col__foot">
      <div class="user">
        <span class="user__mail">{data.me?.displayName ?? data.me?.email ?? ''}</span>
        <Button variant="ghost" size="sm" onclick={logout} loading={loggingOut}>
          {t('common.action.signOut')}
        </Button>
      </div>
    </div>
  </nav>

  <div class="main">
    <header class="topbar">
      <div class="crumb">
        {#if domain}<span>{t(domain.labelKey)}</span>{/if}
        {#if item}
          <Icon name="chevron-right" size={12} />
          <b>{t(item.labelKey)}</b>
        {/if}
      </div>

      <div class="topbar__spacer"></div>

      <button class="searchbtn" onclick={() => (paletteOpen = true)}>
        <Icon name="search" size={14} />
        <span>{t('admin.shell.searchPlaceholder')}</span>
        <kbd>⌘K</kbd>
      </button>

      <div class="seg" role="group" aria-label={t('admin.shell.density')}>
        <button aria-pressed={density === 'comfortable'} onclick={() => (density = 'comfortable')}>
          {t('admin.shell.comfortable')}
        </button>
        <button aria-pressed={density === 'compact'} onclick={() => (density = 'compact')}>
          {t('admin.shell.compact')}
        </button>
      </div>

      <button
        class="iconbtn"
        title={t('admin.shell.toggleTheme')}
        aria-label={t('admin.shell.toggleTheme')}
        onclick={() => (theme = theme === 'dark' ? 'light' : 'dark')}
      >
        <Icon name={theme === 'dark' ? 'sun' : 'moon'} />
      </button>
    </header>

    <div class="banner">{t('admin.dashboard.demoBanner')}</div>

    <main class="content">
      {@render children()}
    </main>
  </div>
</div>

<CommandPalette bind:open={paletteOpen} />

<style>
  .shell {
    display: grid;
    grid-template-columns: var(--rail-w) var(--col-w) minmax(0, 1fr);
    height: 100vh;
    overflow: hidden;
  }

  /* ── 图标轨 ── */
  .rail {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 2px;
    padding: 10px 0;
    background: var(--color-rail-bg);
    border-right: 1px solid var(--color-rail-divider);
  }
  .rail__mark {
    width: 32px;
    height: 32px;
    margin-bottom: 10px;
    border-radius: 9px;
    background: var(--color-primary);
    color: var(--color-primary-fg);
    font-size: var(--text-base);
    font-weight: 700;
    letter-spacing: -0.02em;
    display: grid;
    place-items: center;
  }
  .rail__btn {
    position: relative;
    width: 38px;
    height: 38px;
    border-radius: 9px;
    display: grid;
    place-items: center;
    color: var(--color-rail-fg);
  }
  .rail__btn:hover {
    background: var(--color-rail-hover);
    color: var(--color-rail-fg-active);
    text-decoration: none;
  }
  .rail__btn[aria-current] {
    background: var(--color-rail-active);
    color: var(--color-rail-fg-active);
  }
  .rail__btn[aria-current]::before {
    content: '';
    position: absolute;
    left: -9px;
    top: 9px;
    bottom: 9px;
    width: 2.5px;
    border-radius: 0 3px 3px 0;
    background: var(--color-primary);
  }
  .rail__spacer {
    flex: 1;
  }
  .rail__ava {
    width: 30px;
    height: 30px;
    border-radius: 50%;
    display: grid;
    place-items: center;
    font-size: var(--text-2xs);
    font-weight: 600;
    color: var(--color-rail-fg-active);
    background: var(--color-rail-hover);
    border: 1px solid var(--color-rail-divider);
  }

  /* ── 上下文列 ── */
  .col {
    display: flex;
    flex-direction: column;
    min-height: 0;
    background: var(--color-surface);
    border-right: 1px solid var(--color-border);
  }
  .col__head {
    padding: var(--space-4) var(--space-4) var(--space-3);
  }
  .col__title {
    font-size: var(--text-lg);
    font-weight: 600;
    letter-spacing: -0.01em;
  }
  .col__sub {
    margin-top: 3px;
    font-size: var(--text-2xs);
    color: var(--color-muted);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .col__body {
    flex: 1;
    overflow-y: auto;
    padding: 0 var(--space-2) var(--space-3);
  }
  .navi {
    display: flex;
    align-items: center;
    gap: 9px;
    padding: 7px 9px;
    border-radius: var(--radius-md);
    color: var(--color-fg);
    font-size: var(--text-sm);
  }
  .navi:hover {
    background: var(--color-hover);
    text-decoration: none;
  }
  .navi.is-active {
    background: var(--color-bg);
    font-weight: 600;
    box-shadow: 0 1px 2px var(--color-hover), 0 0 0 1px var(--color-border);
  }
  .navi.is-active :global(svg) {
    color: var(--color-primary);
  }
  .col__foot {
    border-top: 1px solid var(--color-border);
    padding: var(--space-2) var(--space-3);
  }
  .user {
    display: flex;
    align-items: center;
    gap: var(--space-2);
  }
  .user__mail {
    flex: 1;
    min-width: 0;
    font-size: var(--text-2xs);
    color: var(--color-muted);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  /* ── 工作区 ── */
  .main {
    display: flex;
    flex-direction: column;
    min-width: 0;
    background: var(--color-bg);
  }
  .topbar {
    flex: none;
    display: flex;
    align-items: center;
    gap: var(--space-2);
    height: var(--topbar-h);
    padding: 0 var(--space-5);
    border-bottom: 1px solid var(--color-border);
  }
  .crumb {
    display: flex;
    align-items: center;
    gap: 7px;
    font-size: var(--text-sm);
    color: var(--color-muted);
  }
  .crumb b {
    color: var(--color-fg);
    font-weight: 600;
  }
  .topbar__spacer {
    flex: 1;
  }
  .searchbtn {
    display: flex;
    align-items: center;
    gap: 7px;
    height: 30px;
    min-width: 230px;
    padding: 0 10px;
    border: 1px solid var(--color-border);
    border-radius: 7px;
    background: var(--color-bg);
    color: var(--color-muted);
    font: inherit;
    font-size: var(--text-xs);
    cursor: pointer;
  }
  .searchbtn:hover {
    border-color: var(--color-border-strong);
    color: var(--color-fg);
  }
  .searchbtn span {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .searchbtn kbd {
    margin-left: auto;
    padding: 1px 4px;
    border-radius: var(--radius-sm);
    background: var(--color-neutral-bg);
    font-family: var(--font-mono);
    font-size: var(--text-2xs);
  }
  .seg {
    display: flex;
    padding: 2px;
    border: 1px solid var(--color-border);
    border-radius: 7px;
    background: var(--color-surface);
  }
  .seg button {
    padding: 3px 9px;
    border: 0;
    border-radius: 5px;
    background: none;
    color: var(--color-muted);
    font: inherit;
    font-size: var(--text-xs);
    cursor: pointer;
  }
  .seg button[aria-pressed='true'] {
    background: var(--color-bg);
    color: var(--color-fg);
    font-weight: 600;
    box-shadow: var(--shadow-sm);
  }
  .iconbtn {
    width: 30px;
    height: 30px;
    border: 0;
    border-radius: 7px;
    display: grid;
    place-items: center;
    background: none;
    color: var(--color-muted);
    cursor: pointer;
  }
  .iconbtn:hover {
    background: var(--color-hover);
    color: var(--color-fg);
  }
  .banner {
    flex: none;
    padding: 6px var(--space-5);
    background: var(--color-warning-bg);
    color: var(--color-warning);
    font-size: var(--text-2xs);
    border-bottom: 1px solid var(--color-border);
  }
  .content {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    padding: var(--space-5);
  }
</style>
