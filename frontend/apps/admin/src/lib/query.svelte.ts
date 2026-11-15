import type { Problem } from '@openstore/sdk'
import { ApiError } from '@openstore/sdk'

type Fetcher<T> = (signal: AbortSignal) => Promise<T>

/**
 * 自建数据获取原语（前端设计 §5.2，约 25 行）：
 * - AbortController：每次 refetch 取消上一请求（防"筛选快速切换导致旧响应覆盖新响应"）
 * - seq 防乱序
 * - 文件必须是 .svelte.ts 才能在模块里用 runes
 */
export function createQuery<T>(fn: Fetcher<T>, opts: { immediate?: boolean } = {}) {
  let data = $state<T | null>(null)
  let problem = $state<Problem | null>(null)
  let loading = $state(false)
  let ctrl: AbortController | null = null
  let seq = 0

  async function run() {
    ctrl?.abort()
    ctrl = new AbortController()
    const mine = ++seq
    loading = true
    problem = null
    try {
      const r = await fn(ctrl.signal)
      if (mine === seq) data = r
    } catch (e: unknown) {
      if ((e as Error)?.name !== 'AbortError' && mine === seq) {
        problem = e instanceof ApiError ? e.problem : (e as Problem)
      }
    } finally {
      if (mine === seq) loading = false
    }
  }

  if (opts.immediate !== false) queueMicrotask(run)

  return {
    get data() {
      return data
    },
    get problem() {
      return problem
    },
    get loading() {
      return loading
    },
    refetch: run,
    cancel: () => ctrl?.abort(),
  }
}

export function createMutation<I, O>(fn: (input: I, s: AbortSignal) => Promise<O>) {
  let loading = $state(false)
  let problem = $state<Problem | null>(null)

  async function mutate(input: I) {
    loading = true
    problem = null
    try {
      return await fn(input, new AbortController().signal)
    } catch (e: unknown) {
      problem = e instanceof ApiError ? e.problem : (e as Problem)
      throw e
    } finally {
      loading = false
    }
  }

  return {
    get loading() {
      return loading
    },
    get problem() {
      return problem
    },
    mutate,
  }
}
