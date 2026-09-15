import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { User } from 'oidc-client-ts'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import App from '@/App.vue'
import { aiApi, type ConversationMessagePage, type ConversationSummary } from '@/api/ai'
import { useAuthStore } from '@/stores/auth'
import AssistantView from '@/views/AssistantView.vue'

vi.mock('@/api/ai', () => ({
  aiApi: {
    listConversations: vi.fn<typeof aiApi.listConversations>(),
    getConversation: vi.fn<typeof aiApi.getConversation>(),
    listConversationsPage: vi.fn<typeof aiApi.listConversationsPage>(),
    getConversationMessagesPage: vi.fn<typeof aiApi.getConversationMessagesPage>(),
    getConversationSummary: vi.fn<typeof aiApi.getConversationSummary>(),
    createConversation: vi.fn<typeof aiApi.createConversation>(),
    deleteConversation: vi.fn<typeof aiApi.deleteConversation>(),
    listMemories: vi.fn<typeof aiApi.listMemories>(),
    updateMemory: vi.fn<typeof aiApi.updateMemory>(),
    deleteMemory: vi.fn<typeof aiApi.deleteMemory>(),
    sendMessage: vi.fn<typeof aiApi.sendMessage>(),
  },
}))

const first: ConversationSummary = {
  id: '11111111-1111-4111-8111-111111111111',
  title: '九月散步回顾',
  createdAt: '2026-09-01T09:00:00Z',
  updatedAt: '2026-09-14T09:00:00Z',
}
const second: ConversationSummary = {
  ...first,
  id: '22222222-2222-4222-8222-222222222222',
  title: '周末出游安排',
}
const created: ConversationSummary = {
  ...first,
  id: '33333333-3333-4333-8333-333333333333',
  title: '新对话',
}
const storylineId = '9b31d202-7ad3-4b76-8677-5a711f245da0'

function signedInUser(subject = 'first-user') {
  return {
    access_token: 'test-token',
    profile: { sub: subject },
    expired: false,
  } as User
}

function page(conversation: ConversationSummary, earlier = false): ConversationMessagePage {
  return {
    items: [
      {
        id: earlier ? 1 : 21,
        role: 'User',
        content: earlier ? '更早的问题' : `${conversation.title}的问题`,
        createdAt: conversation.createdAt,
        evidence: null,
      },
      {
        id: earlier ? 2 : 22,
        role: 'Assistant',
        content: earlier
          ? '更早的回答'
          : `${conversation.title}的回答 [Event #42] [Storyline #${storylineId}]`,
        createdAt: conversation.updatedAt,
        evidence: earlier
          ? null
          : {
              records: [{ eventId: 42, title: '傍晚河边散步' }],
              memories: [],
              aggregate: null,
              storylines: [
                {
                  storylineId,
                  revision: 1,
                  title: '从三公里慢慢跑到十公里',
                  category: '目标挑战',
                  status: 'Ongoing',
                  snippet: '本月跑步故事线',
                  rangeStart: null,
                  rangeEnd: null,
                  stages: [],
                  eventIds: [42],
                  score: 1,
                },
              ],
            },
      },
    ],
    hasMore: !earlier,
    nextBeforeId: earlier ? null : 21,
  }
}

const wrappers: VueWrapper[] = []

async function mountApp(path = '/assistant') {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore()
  auth.user = signedInUser()
  vi.spyOn(auth, 'restore').mockResolvedValue(undefined)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/assistant', component: AssistantView },
      {
        path: '/events/:id',
        component: defineComponent({
          name: 'RecordPageFixture',
          setup: () => () => h('main', { class: 'record-page-fixture' }, '记录详情'),
        }),
      },
      {
        path: '/storylines/:id',
        component: defineComponent({
          name: 'StorylinePageFixture',
          setup: () => () => h('main', { class: 'storyline-page-fixture' }, '故事线详情'),
        }),
      },
    ],
  })
  await router.push(path)
  await router.isReady()
  const wrapper = mount(App, {
    attachTo: document.body,
    global: {
      plugins: [pinia, router],
      stubs: { WebAppHeader: true, BrandMark: true, AmapActionCards: true },
    },
  })
  wrappers.push(wrapper)
  await flushPromises()
  return { wrapper, router, auth }
}

async function browserBack(router: Router) {
  await new Promise<void>((resolve) => {
    const stop = router.afterEach(() => {
      stop()
      resolve()
    })
    router.back()
  })
  await flushPromises()
}

describe('问答与记录之间的浏览器导航', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(aiApi.listConversationsPage).mockResolvedValue({
      items: [{ ...first }],
      nextCursor: null,
    })
    vi.mocked(aiApi.getConversationMessagesPage).mockImplementation(async (id, beforeId) =>
      page(id === first.id ? first : second, Boolean(beforeId)),
    )
    vi.mocked(aiApi.getConversationSummary).mockResolvedValue(first)
    vi.mocked(aiApi.createConversation).mockResolvedValue({ ...created })
    vi.mocked(aiApi.listMemories).mockResolvedValue([])
    vi.mocked(aiApi.sendMessage).mockResolvedValue(undefined)
  })

  afterEach(() => {
    for (const wrapper of wrappers.splice(0)) wrapper.unmount()
    vi.restoreAllMocks()
  })

  it.each(['.record-citation', '.evidence-list a'])(
    '从 %s 打开记录后，浏览器返回恢复原会话、草稿、阅读位置和已展开证据',
    async (linkSelector) => {
      const { wrapper, router } = await mountApp()
      expect(aiApi.getConversationMessagesPage).not.toHaveBeenCalled()
      await wrapper.get('.conversation-open').trigger('click')
      await flushPromises()
      expect(router.currentRoute.value.query.conversation).toBe(first.id)

      await wrapper.get('.earlier-messages button').trigger('click')
      await flushPromises()
      expect(wrapper.findAll('article.message')).toHaveLength(4)
      expect(aiApi.getConversationMessagesPage).toHaveBeenCalledTimes(2)
      const composer = wrapper.get<HTMLTextAreaElement>('.composer textarea')
      await composer.setValue('这条记录我还想继续问，但尚未发送')
      const messages = wrapper.get<HTMLElement>('.messages').element
      const history = wrapper.get<HTMLElement>('.conversation-list').element
      messages.scrollTop = 173
      history.scrollTop = 47
      await wrapper.get('.messages').trigger('scroll')
      const evidence = wrapper.get<HTMLDetailsElement>('.evidence-disclosure').element
      evidence.open = true

      const link = wrapper.get<HTMLAnchorElement>(linkSelector)
      expect(link.attributes('href')).toBe(`/events/42?conversation=${first.id}`)
      await link.trigger('click')
      await flushPromises()
      expect(router.currentRoute.value.path).toBe('/events/42')
      expect(wrapper.find('.record-page-fixture').exists()).toBe(true)
      expect(wrapper.find('.messages').exists()).toBe(false)

      await browserBack(router)
      expect(router.currentRoute.value.fullPath).toBe(`/assistant?conversation=${first.id}`)
      expect(wrapper.get('.conversation.active').text()).toContain(first.title)
      expect(wrapper.get('.messages').element).toBe(messages)
      expect(wrapper.get('.messages').text()).toContain('更早的回答')
      expect(wrapper.get('.messages').text()).toContain(`${first.title}的回答`)
      expect(wrapper.findAll('article.message')).toHaveLength(4)
      expect(wrapper.get<HTMLTextAreaElement>('.composer textarea').element.value).toBe(
        '这条记录我还想继续问，但尚未发送',
      )
      expect(messages.scrollTop).toBe(173)
      expect(history.scrollTop).toBe(47)
      expect(wrapper.get<HTMLDetailsElement>('.evidence-disclosure').element.open).toBe(true)
      expect(aiApi.getConversationMessagesPage).toHaveBeenCalledTimes(2)
      expect(aiApi.listConversationsPage).toHaveBeenCalledTimes(1)
      expect(aiApi.getConversation).not.toHaveBeenCalled()
      expect(aiApi.sendMessage).not.toHaveBeenCalled()
    },
  )

  it('从回答里的故事线标题打开详情后，浏览器返回仍恢复原会话', async () => {
    const { wrapper, router } = await mountApp()
    await wrapper.get('.conversation-open').trigger('click')
    await flushPromises()

    const link = wrapper.get<HTMLAnchorElement>('.storyline-citation')
    expect(link.text()).toBe('从三公里慢慢跑到十公里')
    expect(link.attributes('href')).toBe(`/storylines/${storylineId}?conversation=${first.id}`)
    await link.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe(`/storylines/${storylineId}`)
    expect(wrapper.find('.storyline-page-fixture').exists()).toBe(true)

    await browserBack(router)
    expect(router.currentRoute.value.fullPath).toBe(`/assistant?conversation=${first.id}`)
    expect(wrapper.get('.messages').text()).toContain(`${first.title}的回答`)
    expect(aiApi.getConversationMessagesPage).toHaveBeenCalledTimes(1)
  })

  it('刷新带会话编号的链接时，仅按需加载指定会话的一页正文', async () => {
    // The requested conversation need not be present in the first page of titles.
    const { wrapper, router } = await mountApp(`/assistant?conversation=${second.id}`)
    expect(router.currentRoute.value.query.conversation).toBe(second.id)
    expect(aiApi.listConversationsPage).toHaveBeenCalledTimes(1)
    expect(aiApi.getConversationMessagesPage).toHaveBeenCalledExactlyOnceWith(second.id)
    expect(wrapper.get('.messages').text()).toContain(`${second.title}的回答`)
    expect(wrapper.get('.messages').text()).not.toContain(`${first.title}的回答`)
    expect(wrapper.find('.earlier-messages').exists()).toBe(true)
    expect(aiApi.getConversation).not.toHaveBeenCalled()
    expect(aiApi.listConversations).not.toHaveBeenCalled()
  })

  it('新建会话更新地址，随后从记录返回不会恢复被替换的旧会话', async () => {
    const { wrapper, router } = await mountApp(`/assistant?conversation=${first.id}`)
    await wrapper.get('.new-chat').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query.conversation).toBe(created.id)
    expect(aiApi.createConversation).toHaveBeenCalledTimes(1)
    expect(wrapper.find('.assistant-empty').exists()).toBe(true)
    expect(wrapper.findAll('article.message')).toHaveLength(0)
    await wrapper.get('.composer textarea').setValue('新会话的草稿')
    await router.push(`/events/42?conversation=${created.id}`)
    await browserBack(router)
    expect(router.currentRoute.value.query.conversation).toBe(created.id)
    expect(wrapper.findAll('article.message')).toHaveLength(0)
    expect(wrapper.get<HTMLTextAreaElement>('.composer textarea').element.value).toBe(
      '新会话的草稿',
    )
    expect(aiApi.getConversationMessagesPage).toHaveBeenCalledExactlyOnceWith(first.id)
  })

  it.each(['switch-account', 'logout-and-login'])(
    '%s 后不复用上次登录保留的聊天正文或草稿',
    async (change) => {
      const { wrapper, router, auth } = await mountApp(`/assistant?conversation=${first.id}`)
      await wrapper.get('.composer textarea').setValue('上次登录的私密草稿')
      await wrapper.get('.record-citation').trigger('click')
      await flushPromises()
      expect(wrapper.find('.record-page-fixture').exists()).toBe(true)
      vi.mocked(aiApi.listConversationsPage).mockResolvedValue({ items: [], nextCursor: null })
      vi.mocked(aiApi.getConversationMessagesPage).mockRejectedValue(new Error('Not found'))

      if (change === 'logout-and-login') {
        auth.user = null
        await flushPromises()
      }
      auth.user = signedInUser(change === 'switch-account' ? 'another-user' : 'first-user')
      await flushPromises()
      await browserBack(router)

      expect(wrapper.text()).not.toContain(`${first.title}的回答`)
      expect(wrapper.get<HTMLTextAreaElement>('.composer textarea').element.value).toBe('')
      expect(wrapper.findAll('article.message')).toHaveLength(0)
      expect(aiApi.getConversationMessagesPage).toHaveBeenCalledTimes(2)
      expect(wrapper.text()).toContain('这段对话暂时打不开，请重试。')
    },
  )

  it('无效会话引用不发送正文请求，也不被当成任意返回地址', async () => {
    const { wrapper } = await mountApp('/assistant?conversation=https%3A%2F%2Fexample.com')
    expect(aiApi.getConversationMessagesPage).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('对话链接无效')
  })

  it('原会话加载失败时不把追问悄悄发送到新会话', async () => {
    vi.mocked(aiApi.getConversationMessagesPage).mockRejectedValueOnce(new Error('Not found'))
    const { wrapper, router } = await mountApp(`/assistant?conversation=${first.id}`)
    await wrapper.get('.composer textarea').setValue('继续刚才的话题')
    await wrapper.get('.composer').trigger('submit')
    await flushPromises()
    expect(router.currentRoute.value.query.conversation).toBe(first.id)
    expect(aiApi.createConversation).not.toHaveBeenCalled()
    expect(aiApi.sendMessage).not.toHaveBeenCalled()
    expect(wrapper.get<HTMLTextAreaElement>('.composer textarea').element.value).toBe(
      '继续刚才的话题',
    )
    expect(wrapper.text()).toContain('重新打开对话')
  })
})
