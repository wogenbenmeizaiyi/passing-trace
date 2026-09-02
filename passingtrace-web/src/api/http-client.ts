// Events API 调用的统一入口。
//
// 责任：
//   1. 从 auth store 取最新的 access_token，注入 `Authorization: Bearer`。
//   2. 401 时调用 oidc 的静默续期，续期成功后重试一次；再次 401 抛出 `unauthenticated`。
//   3. 把 `application/problem+json` 错误体解析为统一的 `HttpError`，
//      同时把业务状态码（400/401/403/404/409/428/500）原样保留给上层处理。
//   4. 把 `version` 头（后端乐观并发令牌）原样回传给上层，不在这里做并发控制。

import { oidc } from '@/auth/oidc'
import { useAuthStore } from '@/stores/auth'
import type { ProblemDetails } from '@/api/events-types'

const CONTENT_TYPE = 'application/json'
const PROBLEM_TYPE = 'application/problem+json'

/** 业务侧统一抛出的错误。`status` 透传后端 HTTP 状态码。 */
export class HttpError extends Error {
  public readonly status: number
  public readonly problem: ProblemDetails | null

  constructor(status: number, message: string, problem: ProblemDetails | null = null) {
    super(message)
    this.name = 'HttpError'
    this.status = status
    this.problem = problem
  }
}

interface RequestOptions {
  body?: unknown
  query?: Record<string, string | number | null | undefined>
  headers?: Record<string, string>
  signal?: AbortSignal
  /** `If-Match` 头：值是后端 `version`，传 `0/undefined/null` 会跳过该头。 */
  ifMatch?: number
  /** `Idempotency-Key` 头：用于创建请求。 */
  idempotencyKey?: string
  /** 401 时是否尝试静默续期后重试一次（默认 true）。 */
  retryOn401?: boolean
}

function buildUrl(path: string, query: RequestOptions['query']): string {
  const base = (import.meta.env.VITE_EVENTS_API_BASE_URL ?? '').replace(/\/$/, '')
  if (!path.startsWith('/')) path = `/${path}`
  if (!query) return `${base}${path}`
  const search = new URLSearchParams()
  for (const [k, v] of Object.entries(query)) {
    if (v === null || v === undefined) continue
    search.set(k, String(v))
  }
  const qs = search.toString()
  return qs ? `${base}${path}?${qs}` : `${base}${path}`
}

function authHeader(token: string | undefined): Record<string, string> {
  return token ? { Authorization: `Bearer ${token}` } : {}
}

async function readAccessToken(): Promise<string | undefined> {
  const auth = useAuthStore()
  let user = auth.user
  if (!user) return undefined
  // OIDC 用户对象暴露 `expired`：true 表示 access_token 已过期。
  // 静默续期由 oidc-client-ts 在后台调度（automaticSilentRenew），但兜底我们再显式触发一次。
  if (user.expired) {
    try {
      const renewed = await oidc.signinSilent()
      if (renewed) {
        auth.user = renewed
        user = renewed
      }
    } catch {
      // 静默续期失败通常意味着会话已彻底失效；调用方会拿到 401。
    }
  }
  return user?.access_token
}

async function readProblem(res: Response): Promise<ProblemDetails | null> {
  const ct = res.headers.get('content-type') ?? ''
  if (!ct.includes(PROBLEM_TYPE) && !ct.includes('json')) return null
  try {
    const text = await res.text()
    if (!text) return null
    return JSON.parse(text) as ProblemDetails
  } catch {
    return null
  }
}

function describe(problem: ProblemDetails | null, fallback: string): string {
  if (problem?.detail) return problem.detail
  if (problem?.title) return problem.title
  return fallback
}

async function parseResponse<T>(res: Response): Promise<T> {
  if (res.status === 204) return undefined as T
  const text = await res.text()
  if (!text) return undefined as T
  return JSON.parse(text) as T
}

/** 拼好 headers，附带 JSON body。 */
function buildHeaders(opts: RequestOptions, token: string | undefined): Record<string, string> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    ...authHeader(token),
    ...opts.headers,
  }
  if (opts.body !== undefined) headers['Content-Type'] = CONTENT_TYPE
  if (typeof opts.ifMatch === 'number' && Number.isFinite(opts.ifMatch) && opts.ifMatch >= 0) {
    headers['If-Match'] = String(opts.ifMatch)
  }
  if (opts.idempotencyKey) headers['Idempotency-Key'] = opts.idempotencyKey
  return headers
}

async function send<T>(method: string, path: string, opts: RequestOptions): Promise<T> {
  const url = buildUrl(path, opts.query)
  const token = await readAccessToken()
  if (!token) {
    throw new HttpError(401, '未登录或会话已失效，请重新登录。')
  }
  const init: RequestInit = {
    method,
    headers: buildHeaders(opts, token),
    body: opts.body === undefined ? undefined : JSON.stringify(opts.body),
    signal: opts.signal ?? null,
    credentials: 'omit',
  }
  let res = await fetch(url, init)

  // 401 兜底：尝试静默续期 → 重试一次。
  if (res.status === 401 && opts.retryOn401 !== false) {
    try {
      const renewed = await oidc.signinSilent()
      if (renewed) {
        const auth = useAuthStore()
        auth.user = renewed
        if (renewed.access_token) {
          res = await fetch(url, {
            ...init,
            headers: buildHeaders(opts, renewed.access_token),
          })
        }
      }
    } catch {
      // 续期失败：继续按 401 上抛。
    }
  }

  if (!res.ok) {
    const problem = await readProblem(res)
    throw new HttpError(res.status, describe(problem, `请求失败 (${res.status})`), problem)
  }

  return parseResponse<T>(res)
}

export const httpClient = {
  get<T>(path: string, opts: RequestOptions = {}): Promise<T> {
    return send<T>('GET', path, opts)
  },
  post<T>(path: string, opts: RequestOptions = {}): Promise<T> {
    return send<T>('POST', path, opts)
  },
  patch<T>(path: string, opts: RequestOptions = {}): Promise<T> {
    return send<T>('PATCH', path, opts)
  },
  put<T>(path: string, opts: RequestOptions = {}): Promise<T> {
    return send<T>('PUT', path, opts)
  },
  delete<T = void>(path: string, opts: RequestOptions = {}): Promise<T> {
    return send<T>('DELETE', path, opts)
  },
}

export type { RequestOptions }
