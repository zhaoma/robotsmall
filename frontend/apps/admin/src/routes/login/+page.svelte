<script lang="ts">
  import { goto } from '$app/navigation'
  import { base } from '$lib/paths'
  import { Button, Field, Input } from '@openstore/ui'
  import { apiFetch, isStatus } from '$lib/api'
  import { t } from '$lib/i18n'

  let email = $state('')
  let password = $state('')
  let loading = $state(false)
  let errorMsg = $state('')

  async function onSubmit(e: SubmitEvent) {
    e.preventDefault()
    if (loading) return
    loading = true
    errorMsg = ''
    try {
      // 契约：POST /api/admin/auth/login（docs/03 §5.4）。返回管理员 + 下发 os_admin_sid / os_admin_csrf Cookie。
      await apiFetch('/api/admin/auth/login', { method: 'POST', body: { email, password } })

      const raw = new URLSearchParams(window.location.search).get('next')
      const next = raw && raw.startsWith(`${base}/`) ? raw : `${base}/`
      await goto(next)
    } catch (err) {
      if (isStatus(err, 401)) errorMsg = t('auth.login.failed')
      else if (isStatus(err, 423)) errorMsg = t('auth.login.locked')
      else errorMsg = t('common.state.error')
    } finally {
      loading = false
    }
  }
</script>

<div class="login">
  <form class="login__card" onsubmit={onSubmit}>
    <h1 class="login__title">{t('auth.login.title')}</h1>

    <Field label={t('auth.login.email')} for="email">
      <Input
        name="email"
        type="email"
        autocomplete="username"
        placeholder="admin@example.com"
        bind:value={email}
        required
      />
    </Field>

    <Field label={t('auth.login.password')} for="password">
      <Input
        name="password"
        type="password"
        autocomplete="current-password"
        bind:value={password}
        required
      />
    </Field>

    {#if errorMsg}<p class="login__error" role="alert">{errorMsg}</p>{/if}

    <Button type="submit" {loading} block>
      {t('auth.login.submit')}
    </Button>
  </form>
</div>

<style>
  .login {
    display: flex;
    align-items: center;
    justify-content: center;
    min-height: 100vh;
    padding: var(--space-4);
    background: var(--color-surface);
  }
  .login__card {
    width: 100%;
    max-width: 22rem;
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
    padding: var(--space-6);
    background: var(--color-surface-raised);
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
    box-shadow: var(--shadow-md);
  }
  .login__title {
    font-size: 1.25rem;
    font-weight: 600;
    text-align: center;
    margin-bottom: var(--space-2);
  }
  .login__error {
    margin: 0;
    font-size: 0.8125rem;
    color: var(--color-danger);
  }
</style>
