import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { User } from 'oidc-client-ts'

vi.mock('@/auth/oidc', () => ({
  oidc: { signinSilent: vi.fn<() => Promise<null>>().mockResolvedValue(null) },
}))

import { storylinesApi } from '@/api/storylines'
import { StorylineStatus, type SaveStorylineRequest } from '@/api/storylines-types'
import { useAuthStore } from '@/stores/auth'
import StorylinesListView from '@/views/StorylinesListView.vue'
import { mount } from '@vue/test-utils'

const body: SaveStorylineRequest = {
  title: '黄山旅行',
  description: null,
  categoryKey: 'trip',
  status: StorylineStatus.Ongoing,
  coverMediaAssetId: null,
  tags: [],
  stages: [],
  nodes: [],
  edges: [],
  webCanvasLayout: null,
}

describe('storylinesApi', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubEnv('VITE_EVENTS_API_BASE_URL', 'https://api.test')
    useAuthStore().user = {
      access_token: 'token',
      expired: false,
      profile: { sub: '7' },
      expires_at: 9999999999,
    } as User
  })
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  it('完整保存同时发送 If-Match 与 Idempotency-Key', async () => {
    const fetchMock = vi
      .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValue(
        new Response(JSON.stringify({ storyline: {} }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      )
    vi.stubGlobal('fetch', fetchMock)
    await storylinesApi.save('story-1', body, 128, 'save-key')
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('https://api.test/api/v1/storylines/story-1')
    expect(init.method).toBe('PUT')
    expect((init.headers as Record<string, string>)['If-Match']).toBe('128')
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe('save-key')
  })

  it('手机式增量变更使用统一 changes 入口', async () => {
    const fetchMock = vi
      .fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>()
      .mockResolvedValue(
        new Response(JSON.stringify({ storyline: {} }), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
      )
    vi.stubGlobal('fetch', fetchMock)
    await storylinesApi.change(
      'story-2',
      { operation: 'sync-node', nodeKey: 'node-1' },
      9,
      'change-key',
    )
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('https://api.test/api/v1/storylines/story-2/changes')
    expect(JSON.parse(init.body as string)).toEqual({ operation: 'sync-node', nodeKey: 'node-1' })
  })
})

describe('故事线筛选界面', () => {
  it('日期区间有明确标签并提供统一筛选分组', () => {
    const wrapper = mount(StorylinesListView, {
      global: {
        plugins: [createPinia()],
        stubs: {
          WebAppHeader: true,
          RouterLink: { template: '<a><slot /></a>' },
        },
      },
    })

    expect(wrapper.get('.story-filter__heading').text()).toContain('筛选故事线')
    expect(wrapper.get('.date-range').text()).toContain('日期范围')
    expect(wrapper.get('input[aria-label="开始日期"]').attributes('type')).toBe('date')
    expect(wrapper.get('input[aria-label="结束日期"]').attributes('type')).toBe('date')
  })
})
