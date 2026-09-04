import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { User } from 'oidc-client-ts'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { eventsApi } from '@/api/events'
import { EventKind, EventStatus, EventVisibility, type EventResponse } from '@/api/events-types'
import { useAuthStore } from '@/stores/auth'
import EventsListView from '@/views/EventsListView.vue'

vi.mock('@/api/events', () => ({
  eventsApi: { list: vi.fn<typeof eventsApi.list>() },
}))

function event(id: number, title: string, happenedAt: string): EventResponse {
  return {
    id,
    kind: EventKind.Trace,
    status: EventStatus.Completed,
    title,
    rawContent: '记录正文',
    happenedAt,
    plannedAt: null,
    completedAt: null,
    timezone: 'Asia/Shanghai',
    visibility: EventVisibility.Private,
    sourceRevision: 1,
    version: 1,
    createdAt: happenedAt,
    updatedAt: happenedAt,
    media: [],
    semanticStatus: 'Completed',
    semanticSummary: null,
    manualClassification: { primaryCategoryKey: null, tags: [], suppressedAiTagKeys: [] },
    effectiveClassification: { primaryCategory: null, tags: [], taxonomyVersion: 'v1' },
    locations: [],
  }
}

describe('记录归档层级', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useAuthStore().user = {
      access_token: 'token',
      profile: { sub: '1' },
      expired: false,
    } as User
    vi.mocked(eventsApi.list).mockResolvedValue({
      items: [
        event(1, '九月记录', '2026-09-01T18:30:00+08:00'),
        event(2, '八月记录', '2026-08-20T18:30:00+08:00'),
        event(3, '去年记录', '2025-12-20T18:30:00+08:00'),
      ],
      nextCursor: null,
    })
  })

  it('默认展开最新月份，旧年份和旧月份可以逐级展开', async () => {
    const wrapper = mount(EventsListView, {
      global: {
        stubs: {
          WebAppHeader: true,
          RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' },
        },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('九月记录')
    expect(wrapper.text()).not.toContain('八月记录')
    expect(wrapper.text()).not.toContain('去年记录')

    const olderYear = wrapper
      .findAll('button.year-heading')
      .find((button) => button.text().includes('2025 年'))!
    expect(olderYear.attributes('aria-expanded')).toBe('false')
    await olderYear.trigger('click')

    const olderMonth = wrapper
      .findAll('button.month-heading')
      .find((button) => button.text().includes('12 月'))!
    expect(olderMonth.attributes('aria-expanded')).toBe('false')
    await olderMonth.trigger('click')
    expect(wrapper.text()).toContain('去年记录')
  })
})
