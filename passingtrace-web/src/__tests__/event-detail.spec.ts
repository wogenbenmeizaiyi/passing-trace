import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { User } from 'oidc-client-ts'

import { aiApi, type SemanticResult } from '@/api/ai'
import { eventsApi } from '@/api/events'
import { HttpError } from '@/api/http-client'
import { mediaApi } from '@/api/media'
import { EventKind, EventStatus, EventVisibility, type EventResponse } from '@/api/events-types'
import { useAuthStore } from '@/stores/auth'
import EventDetailView from '@/views/EventDetailView.vue'

const event: EventResponse = {
  id: 42,
  kind: EventKind.Trace,
  status: EventStatus.Completed,
  title: '西湖散步',
  rawContent: '傍晚沿着湖边走了一圈。',
  happenedAt: '2026-08-31T18:30:00+08:00',
  plannedAt: null,
  completedAt: null,
  timezone: 'Asia/Shanghai',
  visibility: EventVisibility.Private,
  sourceRevision: 1,
  version: 1,
  createdAt: '2026-08-31T18:30:00+08:00',
  updatedAt: '2026-08-31T18:30:00+08:00',
  media: [],
  semanticStatus: 'Completed',
  semanticSummary: '这是一次傍晚的轻松散步。',
  manualClassification: {
    primaryCategoryKey: null,
    tags: [],
    suppressedAiTagKeys: [],
  },
  effectiveClassification: {
    primaryCategory: null,
    tags: [],
    taxonomyVersion: 'life-v1',
  },
  locations: [],
}

const semantic: SemanticResult = {
  eventId: 42,
  sourceRevision: 1,
  status: 'Completed',
  summary: event.semanticSummary,
  semantic: {},
  model: 'qwen-test',
  pipelineVersion: 'v2',
  createdAt: '2026-08-31T18:31:00+08:00',
  completedAt: '2026-08-31T18:31:01+08:00',
  error: null,
}

describe('记录详情 AI 分析', () => {
  afterEach(() => vi.restoreAllMocks())

  it('默认隐藏并延迟加载，点击图标后展开且可再次收起', async () => {
    vi.spyOn(eventsApi, 'get').mockResolvedValue(event)
    const getSemantic = vi.spyOn(aiApi, 'getSemantic').mockResolvedValue(semantic)

    const pinia = createPinia()
    setActivePinia(pinia)
    const auth = useAuthStore()
    auth.user = {
      expired: false,
      access_token: 'test-token',
      profile: { sub: 'user-1' },
    } as unknown as User

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/events/:id', component: EventDetailView },
        { path: '/events', component: { template: '<div>记录列表</div>' } },
      ],
    })
    await router.push('/events/42')
    await router.isReady()

    const wrapper = mount(EventDetailView, {
      global: {
        plugins: [pinia, router],
        stubs: { WebAppHeader: true },
      },
    })
    await flushPromises()

    expect(getSemantic).not.toHaveBeenCalled()
    expect(wrapper.find('#event-semantic-panel').exists()).toBe(false)
    expect(wrapper.text()).not.toContain(event.semanticSummary!)

    const toggle = wrapper.get('.semantic-toggle')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    await toggle.trigger('click')
    await flushPromises()

    expect(getSemantic).toHaveBeenCalledTimes(1)
    expect(wrapper.get('#event-semantic-panel').text()).toContain(event.semanticSummary!)
    expect(toggle.attributes('aria-expanded')).toBe('true')

    await toggle.trigger('click')
    await flushPromises()

    expect(wrapper.find('#event-semantic-panel').exists()).toBe(false)
    expect(toggle.attributes('aria-expanded')).toBe('false')
    wrapper.unmount()
  })

  it('附件读取失败时仍展示记录，而不是误报记录不存在', async () => {
    vi.spyOn(eventsApi, 'get').mockResolvedValue({
      ...event,
      media: [
        {
          id: 'missing-media',
          fileName: '西湖.jpg',
          kind: 1,
          contentType: 'image/jpeg',
          size: 1024,
          status: 4,
          sortOrder: 0,
        },
      ],
    })
    vi.spyOn(mediaApi, 'access').mockRejectedValue(new HttpError(404, '附件不存在'))

    const pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().user = {
      expired: false,
      access_token: 'test-token',
      profile: { sub: 'user-1' },
    } as unknown as User
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/events/:id', component: EventDetailView },
        { path: '/events', component: { template: '<div>记录列表</div>' } },
      ],
    })
    await router.push('/events/42')
    await router.isReady()

    const wrapper = mount(EventDetailView, {
      global: { plugins: [pinia, router], stubs: { WebAppHeader: true } },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('西湖散步')
    expect(wrapper.text()).toContain('附件暂时无法加载')
    expect(wrapper.text()).not.toContain('记录不存在或已被删除')
    wrapper.unmount()
  })
})
