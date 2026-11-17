/**
 * RFC 7807 Problem Details 形状（后端 AddProblemDetails 统一输出）。
 * 字段级校验错误放在 `errors`（path → 消息数组），admin 直接映射到控件。
 */
export interface Problem {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  /** 业务错误码（如 AUTH_INVALID_CREDENTIALS、STOCK_CHANGED_RETRY）。 */
  code?: string
  /** 字段路径 → 错误消息。 */
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly problem: Problem

  constructor(status: number, problem: Problem) {
    super(problem.detail ?? problem.title ?? `HTTP ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.code = problem.code
    this.problem = problem
  }
}

/** 从 Response 解析 Problem，非 JSON 响应降级为 status 兜底。 */
export async function parseProblem(res: Response): Promise<Problem> {
  try {
    const ct = res.headers.get('content-type') ?? ''
    if (ct.includes('application/problem+json') || ct.includes('application/json')) {
      return (await res.json()) as Problem
    }
  } catch {
    // 忽略：返回兜底 Problem
  }
  return { status: res.status, title: res.statusText }
}
