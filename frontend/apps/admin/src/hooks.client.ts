import type { HandleClientError } from '@sveltejs/kit'

// 401 → 跳登录的守卫放在 (app)/+layout.ts（读 /api/admin/auth/me 失败即 goto 登录）；
// 这里只做客户端错误统一上报点（本期 console，后续接前端监控）。
export const handleError: HandleClientError = ({ error, status, message, event }) => {
  console.error('[admin:client-error]', { status, message, url: event.url.toString(), error })
}
