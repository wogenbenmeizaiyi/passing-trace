import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { User } from 'oidc-client-ts'

vi.mock('@/auth/oidc', () => ({
  oidc: {
    signinSilent: vi.fn<() => Promise<null>>().mockResolvedValue(null),
  },
}))

import { eventsApi } from '@/api/events'
import { useAuthStore } from '@/stores/auth'
import { EventKind, EventStatus } from '@/api/events-types'

function makeUser(): User {
  return {
    access_token: 't',
    token_type: 'Bearer',
    session_state: null,
    profile: { sub: '1' } as User['profile'],
    expires_at: Math.floor(Date.now() / 1000) + 600,
    expired: false,
    refresh_token: 'r',
    id_token: 'i',
    scope: 'openid passingtrace.api',
    state: null,
  } as User
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  })
}

describe('eventsApi', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubEnv('VITE_EVENTS_API_BASE_URL', 'https://api.test')
    const auth = useAuthStore()
    auth.user = makeUser()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  it('list 拼接查询参数并跳过 null/undefined', async () => {
    const fetchMock = vi
      .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValue(jsonResponse({ items: [], nextCursor: null }))
    vi.stubGlobal('fetch', fetchMock)

    await eventsApi.list({
      limit: 20,
      cursor: 5,
      kind: EventKind.Trace,
      status: undefined,
      from: '2026-01-01T00:00:00+00:00',
    })

    const url = fetchMock.mock.calls[0]?.[0] as string
    expect(url).toContain('/api/v1/events?')
    expect(url).toContain('limit=20')
    expect(url).toContain('cursor=5')
    expect(url).toContain('kind=0')
    expect(url).not.toContain('status=')
    expect(url).toContain('from=2026-01-01T00')
  })

  it('get 拒绝非法 id（不发请求）', async () => {
    const fetchMock = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
    vi.stubGlobal('fetch', fetchMock)
    await expect(eventsApi.get(NaN)).rejects.toThrow('无效的事件 id')
    await expect(eventsApi.get(0)).rejects.toThrow('无效的事件 id')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('create 必传 Idempotency-Key，并把 kind 转回数字', async () => {
    const fetchMock = vi
      .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValue(jsonResponse({ id: 1 }))
    vi.stubGlobal('fetch', fetchMock)

    await eventsApi.create(
      {
        kind: EventKind.Plan,
        title: '下周东京',
        rawContent: '去看樱花',
        plannedAt: '2026-08-25T10:00:00+09:00',
        timezone: 'Asia/Tokyo',
      },
      'idem-fixed',
    )

    const [url, init] = fetchMock.mock.calls[0]!
    expect(url).toBe('https://api.test/api/v1/events')
    const headers = (init!.headers ?? {}) as Record<string, string>
    expect(headers['Idempotency-Key']).toBe('idem-fixed')
    expect(init!.body).toBe(
      JSON.stringify({
        kind: 1,
        title: '下周东京',
        rawContent: '去看樱花',
        plannedAt: '2026-08-25T10:00:00+09:00',
        timezone: 'Asia/Tokyo',
      }),
    )
  })

  it('update 必传 If-Match', async () => {
    const fetchMock = vi
      .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValue(jsonResponse({ id: 9 }))
    vi.stubGlobal('fetch', fetchMock)

    await eventsApi.update(9, { title: 'new', rawContent: null, timezone: 'Asia/Tokyo' }, 1284)

    const [url, init] = fetchMock.mock.calls[0]!
    expect(url).toBe('https://api.test/api/v1/events/9')
    const headers = (init!.headers ?? {}) as Record<string, string>
    expect(headers['If-Match']).toBe('1284')
  })

  it('remove 必传 If-Match，返回 undefined', async () => {
    const fetchMock = vi
      .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(eventsApi.remove(9, 7)).resolves.toBeUndefined()
    const [url, init] = fetchMock.mock.calls[0]!
    expect(url).toBe('https://api.test/api/v1/events/9')
    const headers = (init!.headers ?? {}) as Record<string, string>
    expect(headers['If-Match']).toBe('7')
  })

  it('枚举在请求中以数字发送', async () => {
    const fetchMock = vi
      .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValue(jsonResponse({}))
    vi.stubGlobal('fetch', fetchMock)

    await eventsApi.create({ kind: EventKind.Plan, timezone: 'UTC' }, 'k')

    const init = fetchMock.mock.calls[0]?.[1] as RequestInit
    expect(JSON.parse(init.body as string)).toMatchObject({ kind: 1 })

    // 验证本地枚举常量值是数字，且与文档契约一致
    expect(EventStatus.Planned).toBe(0)
    expect(EventStatus.Completed).toBe(1)
    expect(EventStatus.Cancelled).toBe(2)
    expect(EventKind.Trace).toBe(0)
    expect(EventKind.Plan).toBe(1)
  })
})
