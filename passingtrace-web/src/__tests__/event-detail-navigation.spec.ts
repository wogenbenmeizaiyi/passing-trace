import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter, type LocationQueryRaw } from 'vue-router'

import EventDetailView from '@/views/EventDetailView.vue'

const conversationId = 'd2759c32-4fb0-4e4b-8d9e-0d7909fbbbc6'

async function openRecord(query: LocationQueryRaw = {}) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/assistant', component: { template: '<div>对话</div>' } },
      { path: '/events/:id', component: EventDetailView },
      { path: '/events', component: { template: '<div>记录列表</div>' } },
    ],
  })
  await router.push('/events')
  await router.push({ path: '/events/42', query })
  await router.isReady()
  const wrapper = mount(EventDetailView, {
    global: { plugins: [pinia, router], stubs: { WebAppHeader: true } },
  })
  await flushPromises()
  return { wrapper, router }
}

describe('记录详情返回对话', () => {
  it('从对话打开时提供明确的返回原对话入口，并保留记录列表入口', async () => {
    const { wrapper } = await openRecord({ conversation: conversationId })
    const back = wrapper.get('.conversation-return')

    expect(back.text()).toBe('返回原对话')
    expect(back.attributes('href')).toBe(`/assistant?conversation=${conversationId}`)
    expect(back.get('svg').attributes('aria-hidden')).toBe('true')
    expect(wrapper.get('.secondary-return').text()).toBe('查看记录列表')
    expect(wrapper.get('.secondary-return').attributes('href')).toBe('/events')
    wrapper.unmount()
  })

  it('点击返回原对话携带会话 ID，且不追加一个记录和对话循环的历史项', async () => {
    const { wrapper, router } = await openRecord({ conversation: conversationId })

    await wrapper.get('.conversation-return').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe(`/assistant?conversation=${conversationId}`)
    router.back()
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/events')
    wrapper.unmount()
  })

  it.each([
    undefined,
    null,
    '',
    'not-a-conversation',
    'https://example.com/steal',
    '/assistant?conversation=other',
    [conversationId, conversationId],
  ])('没有单个有效会话 ID 时仍返回记录列表：%j', async (conversation) => {
    const { wrapper, router } = await openRecord({ conversation })

    expect(wrapper.find('.conversation-return').exists()).toBe(false)
    const back = wrapper.get('.back-link a')
    expect(back.text()).toBe('返回记录列表')
    expect(back.attributes('href')).toBe('/events')
    await back.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/events')
    wrapper.unmount()
  })
})
