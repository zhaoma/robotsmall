import { goto } from '$app/navigation'
import { base } from '$lib/paths'
import { ApiError, parseProblem } from '@openstore/sdk'
import type { Problem } from '@openstore/sdk'

const WRITE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

export interface ApiRequestInit extends Omit<RequestInit, 'body'> {
  body?: unknown
}

/** 读 os_admin_csrf cookie（CSRF 双提交：cookie 存原文，请求头回传原文）。 */
export function readCsrf(): string | undefined {
  const m = document.cookie.split('; ').find((c) => c.startsWith('os_admin_csrf='))
  return m ? decodeURIComponent(m.slice('os_admin_csrf='.length)) : undefined
}

/** 会话过期 → 回登录并带 ?next= 原路径。 */
export function redirectToLogin() {
  const next = encodeURIComponent(window.location.pathname + window.location.search)
  return goto(`${base}/login?next=${next}`)
}

export function isProblem(e: unknown): e is ApiError {
  return e instanceof ApiError
}

export function isStatus(e: unknown, status: number): boolean {
  return e instanceof ApiError && e.status === status
}

/**
 * 同源 /api fetch 封装（前端设计 §5.2 / 后端设计 §6.6）：
 * - credentials: 'include' 带 Cookie
 * - 写方法自动带 X-CSRF-Token（双提交，读 os_admin_csrf）
 * - Accept: application/json；非 2xx 统一解析 ProblemDetails 并抛 ApiError
 * - 401/403/409 分流：调用方按 status 决定行为（守卫跳登录 / 403 提示 / 409 刷新重试）
 */
export async function apiFetch<T>(path: string, init: ApiRequestInit = {}): Promise<T> {
  const method = (init.method ?? 'GET').toUpperCase()
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body != null) headers.set('Content-Type', 'application/json')
  if (WRITE_METHODS.has(method)) {
    const csrf = readCsrf()
    if (csrf) headers.set('X-CSRF-Token', csrf)
  }

  let res: Response
  try {
    res = await fetch(path, {
      ...init,
      method,
      headers,
      credentials: 'include',
      body: init.body != null ? JSON.stringify(init.body) : undefined,
    })
  } catch {
    throw new ApiError(0, { title: 'NETWORK_ERROR', detail: 'Network error' })
  }

  if (res.status === 204) return undefined as T

  if (!res.ok) {
    const problem: Problem = await parseProblem(res)
    throw new ApiError(res.status, problem)
  }

  return (await res.json()) as T
}
