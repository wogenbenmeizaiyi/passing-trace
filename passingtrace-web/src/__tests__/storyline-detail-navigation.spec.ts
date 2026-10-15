import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import { storylinesApi } from '@/api/storylines'
import { StorylineStatus, type StorylineRevisionResponse } from '@/api/storylines-types'
import StorylineDetailView from '@/views/StorylineDetailView.vue'

vi.mock('@/api/storylines', () => ({
  storylinesApi: {
    get: vi.fn<typeof storylinesApi.get>(),
    revision: vi.fn<typeof storylinesApi.revision>(),
  },
}))

const conversationId = 'd2759c32-4fb0-4e4b-8d9e-0d7909fbbbc6'
const storylineId = '9b31d202-7ad3-4b76-8677-5a711f245da0'
const storyline: StorylineRevisionResponse = {
  id: storylineId,
  title: '从三公里慢慢跑到十公里',
  description: null,
  categoryKey: 'challenge',
  categoryLabel: '目标挑战',
  status: StorylineStatus.Ongoing,
  revision: 1,
  version: 1,
  coverMediaAssetId: null,
  rangeStart: null,
  rangeEnd: null,
  layoutState: 1,
  tags: [],
  stages: [],
  nodes: [],
  edges: [],
  outline: [],
  webCanvasLayout: null,
  updatedAt: '2026-09-14T08:00:00Z',
}

async function openStoryline(query = '') {
  vi.mocked(storylinesApi.get).mockResolvedValue(storyline)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/assistant', component: { template: '<div>原对话</div>' } },
      { path: '/storylines', component: { template: '<div>故事线列表</div>' } },
      { path: '/storylines/:id', component: StorylineDetailView },
    ],
  })
  await router.push(`/storylines/${storylineId}${query}`)
  await router.isReady()
  const wrapper = mount(StorylineDetailView, {
    global: { plugins: [router], stubs: { WebAppHeader: true } },
  })
  await flushPromises()
  return { wrapper, router }
}

describe('故事线详情返回对话', () => {
  it('从 AI 回答打开时显示返回原对话，并保留故事线列表入口', async () => {
    const { wrapper, router } = await openStoryline(`?conversation=${conversationId}`)

    const back = wrapper.get('.conversation-return')
    expect(back.text()).toBe('返回原对话')
    expect(back.attributes('href')).toBe(`/assistant?conversation=${conversationId}`)
    expect(wrapper.get('.secondary-return').text()).toBe('查看故事线列表')

    await back.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe(`/assistant?conversation=${conversationId}`)
    wrapper.unmount()
  })

  it('没有有效会话编号时只返回故事线列表', async () => {
    const { wrapper } = await openStoryline('?conversation=https%3A%2F%2Fexample.com')

    expect(wrapper.find('.conversation-return').exists()).toBe(false)
    expect(wrapper.get('.back-link a').text()).toBe('返回故事线列表')
    expect(wrapper.get('.back-link a').attributes('href')).toBe('/storylines')
    wrapper.unmount()
  })
})
