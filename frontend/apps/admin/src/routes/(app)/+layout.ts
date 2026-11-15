import { goto } from '$app/navigation'
import { base } from '$lib/paths'
import { apiFetch } from '$lib/api'
import type { AdminUserDto } from '@openstore/sdk'

/**
 * 已登录壳的客户端守卫（前端设计 §1.1 树）：
 * 读 /api/admin/auth/me，失败（未登录/会话过期/网络错）→ goto 登录页。
 * 注意：ssr=false（根 +layout.ts），本 load 只在浏览器执行。
 */
export const load = async () => {
  try {
    const me = await apiFetch<AdminUserDto>('/api/admin/auth/me')
    return { me }
  } catch {
    const next = encodeURIComponent(window.location.pathname + window.location.search)
    await goto(`${base}/login?next=${next}`)
    return { me: null }
  }
}
