import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { User } from 'oidc-client-ts'

vi.mock('@/auth/oidc', () => ({
  oidc: {
    signinSilent: vi.fn<() => Promise<null>>(),
  },
}))

import { oidc } from '@/auth/oidc'
import { useAuthStore } from '@/stores/auth'
import { HttpError, httpClient } from '@/api/http-client'

function makeUser(overrides: Partial<User> = {}): User {
  return {
    access_token: 'access-abc',
    token_type: 'Bearer',
    session_state: null,
    profile: { sub: '1' } as User['profile'],
    expires_at: Math.floor(Date.now() / 1000) + 600,
    expired: false,
    refresh_token: 'refresh-xyz',
    id_token: 'id-123',
    scope: 'openid profile passingtrace.api',
    state: null,
    ...overrides,
  } as User
}

function jsonResponse(body: unknown, status = 200, contentType = 'application/json'): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': contentType },
  })
}

function problemResponse(status: number, detail: string): Response {
  return new Response(JSON.stringify({ status, title: 'Error', detail }), {
    status,
    headers: { 'content-type': 'application/problem+json' },
  })
}

describe('httpClient', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubEnv('VITE_EVENTS_API_BASE_URL', 'https://api.test')
    vi.mocked(oidc.signinSilent).mockReset()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  it('从 auth store 取最新 token 并注入 Authorization: Bearer', async () => {
    const auth = useAuthStore()
    auth.user = makeUser()
    const fetchMock = vi
      .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValue(jsonResponse({ ok: true }))
    vi.stubGlobal('fetch', fetchMock)

    await httpClient.get('/api/v1/events', { query: { limit: 10 } })

    expect(fetchMock).toHaveBeenCalledOnce()
    const [url, init] = fetchMock.mock.calls[0]!
    expect(url).toBe('https://api.test/api/v1/events?limit=10')
    const headers = (init!.headers ?? {}) as Record<string, string>
    expect(headers.Authorization).toBe('Bearer access-abc')
    expect(headers.Accept).toBe('application/json')
  })

  it('附带 If-Match 与 Idempotency-Key 头', async () => {
    const auth = useAuthStore()
    auth.user = makeUser()
    const fetchMock = vi
      .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValue(jsonResponse({}))
    vi.stubGlobal('fetch', fetchMock)

    await httpClient.post('/api/v1/events', {
      body: { kind: 0 },
      idempotencyKey: 'idem-123',
      ifMatch: 42,
    })

    const init = fetchMock.mock.calls[0]?.[1] as RequestInit
    const headers = (init.headers ?? {}) as Record<string, string>
    expect(headers['If-Match']).toBe('42')
    expect(headers['Idempotency-Key']).toBe('idem-123')
    expect(init.body).toBe(JSON.stringify({ kind: 0 }))
  })

  it('未登录时直接抛 401 不发请求', async () => {
    const fetchMock = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
    vi.stubGlobal('fetch', fetchMock)

    await expect(httpClient.get('/api/v1/events')).rejects.toMatchObject({
      name: 'HttpError',
      status: 401,
    })
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('401 触发 oidc.signinSilent 后重试一次', async () => {
    const auth = useAuthStore()
    auth.user = makeUser()
    const renewed = makeUser({ access_token: 'access-renewed' })
    vi.mocked(oidc.signinSilent).mockResolvedValueOnce(renewed)

    const fetchMock = vi
      .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValueOnce(problemResponse(401, 'expired'))
      .mockResolvedValueOnce(jsonResponse({ items: [] }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await httpClient.get('/api/v1/events')

    expect(fetchMock).toHaveBeenCalledTimes(2)
    const retryInit = fetchMock.mock.calls[1]?.[1] as RequestInit
    const retryHeaders = (retryInit.headers ?? {}) as Record<string, string>
    expect(retryHeaders.Authorization).toBe('Bearer access-renewed')
    expect(result).toEqual({ items: [] })
  })

  it('401 续期失败 → 直接抛 401', async () => {
    const auth = useAuthStore()
    auth.user = makeUser()
    vi.mocked(oidc.signinSilent).mockResolvedValueOnce(null)

    const fetchMock = vi
      .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValue(problemResponse(401, 'expired'))
    vi.stubGlobal('fetch', fetchMock)

    await expect(httpClient.get('/api/v1/events')).rejects.toBeInstanceOf(HttpError)
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('业务错误按 ProblemDetails 抛 HttpError', async () => {
    const auth = useAuthStore()
    auth.user = makeUser()
    vi.stubGlobal(
      'fetch',
      vi
        .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
        .mockResolvedValue(problemResponse(409, '内容已被修改')),
    )

    await expect(
      httpClient.patch('/api/v1/events/1', { body: { title: 'x' }, ifMatch: 7 }),
    ).rejects.toMatchObject({ status: 409, message: '内容已被修改' })
  })

  it('204 No Content 不解析 body', async () => {
    const auth = useAuthStore()
    auth.user = makeUser()
    vi.stubGlobal(
      'fetch',
      vi
        .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
        .mockResolvedValue(new Response(null, { status: 204 })),
    )

    await expect(httpClient.delete('/api/v1/events/1', { ifMatch: 3 })).resolves.toBeUndefined()
  })

  it('expired=true 时主动 signinSilent 续期', async () => {
    const auth = useAuthStore()
    auth.user = makeUser({ expired: true })
    const renewed = makeUser({ access_token: 'proactive', expired: false })
    vi.mocked(oidc.signinSilent).mockResolvedValueOnce(renewed)
    const fetchMock = vi
      .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValue(jsonResponse({ items: [] }))
    vi.stubGlobal('fetch', fetchMock)

    await httpClient.get('/api/v1/events')

    expect(oidc.signinSilent).toHaveBeenCalledOnce()
    const init = fetchMock.mock.calls[0]?.[1] as RequestInit
    expect(((init.headers ?? {}) as Record<string, string>).Authorization).toBe('Bearer proactive')
  })
})
