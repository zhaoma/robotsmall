import type { HandleClientError } from '@sveltejs/kit/hooks'

// 401 → 跳登录的守卫放在 (app)/+layout.ts（读 /api/admin/auth/me 失败即 goto 登录）；
// 这里只做客户端错误统一上报点（本期 console，后续接前端监控）。
// SvelteKit 3：客户端 handleError 输入为 { kind, error, event }（CaughtError + NavigationEvent）。
export const handleError: HandleClientError = ({ kind, error, event }) => {
  console.error('[admin:client-error]', { kind, error, url: event.url.toString() })
}
