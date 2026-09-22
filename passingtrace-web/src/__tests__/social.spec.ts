import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent, reactive } from 'vue'
import {
  socialApi,
  safeSocialPath,
  type DirectConversation,
  type DirectMessage,
} from '@/api/social'
import MessagesView from '@/views/MessagesView.vue'
import ParticipantPicker from '@/components/ParticipantPicker.vue'
import AssistantMessageContent from '@/components/AssistantMessageContent'

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ isAuthenticated: true, user: { profile: { sub: '1' } } }),
}))
const feed = reactive({
  change: 0,
  unread: 0,
  online: true,
  refresh: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
})
vi.mock('@/stores/social', () => ({ useSocialStore: () => feed }))
vi.mock('@/api/social', async (original) => ({
  ...(await original<typeof import('@/api/social')>()),
  socialApi: {
    conversations: vi.fn<typeof socialApi.conversations>(),
    conversation: vi.fn<typeof socialApi.conversation>(),
    messages: vi.fn<typeof socialApi.messages>(),
    read: vi.fn<typeof socialApi.read>(),
    send: vi.fn<typeof socialApi.send>(),
    friends: vi.fn<typeof socialApi.friends>(),
    shareStatuses: vi.fn<typeof socialApi.shareStatuses>(),
  },
}))
const id = '11111111-1111-4111-8111-111111111111'
const person = { id: '2', nickname: '小王', bio: '', hasAvatar: false, friendCode: 'A'.repeat(16) }
const conversation: DirectConversation = {
  id,
  friendshipId: id,
  person,
  preview: '最近消息',
  unreadCount: 1,
  peerReadThroughId: 0,
  clearedThroughId: 0,
  updatedAt: '2026-09-22',
  canSend: true,
}
const message = (n: number): DirectMessage => ({
  id: n,
  conversationId: id,
  senderId: '2',
  clientMessageId: id,
  kind: 'text',
  text: `第${n}条`,
  shareId: null,
  shareTitle: null,
  shareAvailable: false,
  createdAt: '2026-09-22',
})
let wrapper: VueWrapper
beforeEach(() => {
  vi.clearAllMocks()
  feed.change = 0
  vi.mocked(socialApi.conversations).mockResolvedValue({ items: [conversation], nextCursor: null })
  vi.mocked(socialApi.conversation).mockResolvedValue({ ...conversation })
  vi.mocked(socialApi.messages).mockResolvedValue({ items: [message(31)], nextCursor: '31' })
  vi.mocked(socialApi.read).mockResolvedValue(undefined)
})
afterEach(() => wrapper?.unmount())
async function open(path = '/messages') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/messages/:id?', component: MessagesView }],
  })
  await router.push(path)
  wrapper = mount(MessagesView, {
    global: {
      plugins: [router],
      stubs: { WebAppHeader: true, FriendsPanel: true, PersonAvatar: true, ShareDialog: true },
    },
  })
  await flushPromises()
  return router
}
describe('好友与聊天', () => {
  it('分享详情返回保留聊天、草稿和滚动位置，不把分享 ID 当成会话 ID', async () => {
    vi.mocked(socialApi.messages).mockImplementation(async (_id, _before, after) =>
      after === undefined
        ? { items: [message(31)], nextCursor: null }
        : { items: [], nextCursor: null },
    )
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/messages/:id?', component: MessagesView },
        { path: '/shares/:id', component: { template: '<p>只读分享</p>' } },
      ],
    })
    await router.push(`/messages/${id}`)
    wrapper = mount(
      defineComponent({
        template:
          '<RouterView v-slot="{ Component }"><KeepAlive include="MessagesView"><component :is="Component" /></KeepAlive></RouterView>',
      }),
      {
        global: {
          plugins: [router],
          stubs: { WebAppHeader: true, FriendsPanel: true, PersonAvatar: true, ShareDialog: true },
        },
      },
    )
    await flushPromises()
    await wrapper.get('textarea').setValue('回来继续聊')
    const pane = wrapper.get('.message-scroll').element
    Object.defineProperties(pane, { scrollHeight: { value: 2000 }, clientHeight: { value: 500 } })
    pane.scrollTop = 280
    await wrapper.get('.message-scroll').trigger('scroll')
    vi.mocked(socialApi.messages).mockClear()
    await router.push('/shares/22222222-2222-4222-8222-222222222222')
    await flushPromises()
    expect(socialApi.messages).not.toHaveBeenCalled()
    router.back()
    await flushPromises()
    expect((wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe('回来继续聊')
    expect(wrapper.get('.message-scroll').element.scrollTop).toBe(280)
    expect(wrapper.text()).toContain('第31条')
    expect(
      vi.mocked(socialApi.messages).mock.calls.every((call) => call[0] === id && call[2] === 31),
    ).toBe(true)
  })
  it('列表只获取摘要，打开某个聊天才加载消息，较早消息按需读取', async () => {
    const router = await open()
    expect(socialApi.messages).not.toHaveBeenCalled()
    await router.push(`/messages/${id}`)
    await flushPromises()
    expect(socialApi.messages).toHaveBeenCalledWith(id)
    expect(wrapper.text()).toContain('第31条')
    vi.mocked(socialApi.messages).mockResolvedValueOnce({ items: [message(1)], nextCursor: null })
    await wrapper.get('button.older').trigger('click')
    await flushPromises()
    expect(socialApi.messages).toHaveBeenLastCalledWith(id, 31)
    expect(wrapper.text()).toContain('第1条')
  })
  it('发送失败重试沿用同一幂等标识，保留输入', async () => {
    await open(`/messages/${id}`)
    vi.mocked(socialApi.send)
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce({ ...message(32), senderId: '1', text: '你好' })
    await wrapper.get('textarea').setValue('你好')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    const body = vi.mocked(socialApi.send).mock.calls[0]![1]
    expect((wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe('你好')
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('重试'))!
      .trigger('click')
    await flushPromises()
    expect(vi.mocked(socialApi.send).mock.calls[1]![1]).toEqual(body)
    expect((wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe('')
  })
  it('清除状态跨设备同步，分享卡片重新校验权限', async () => {
    vi.mocked(socialApi.messages).mockResolvedValueOnce({
      items: [
        { ...message(31), kind: 'record', shareId: id, shareTitle: '分享', shareAvailable: true },
      ],
      nextCursor: null,
    })
    await open(`/messages/${id}`)
    vi.mocked(socialApi.messages).mockResolvedValue({ items: [], nextCursor: null })
    vi.mocked(socialApi.shareStatuses).mockResolvedValue([
      { id, title: '内容已不可查看', available: false },
    ])
    feed.change++
    await flushPromises()
    expect(wrapper.text()).toContain('内容已不可查看')
    vi.mocked(socialApi.conversation).mockResolvedValue({ ...conversation, clearedThroughId: 31 })
    feed.change++
    await flushPromises()
    expect(wrapper.text()).not.toContain('内容已不可查看')
  })
  it('参与者选择保存用户标识，输入名字不会自动关联', async () => {
    vi.mocked(socialApi.friends).mockResolvedValue([
      {
        id,
        person,
        remark: '老朋友',
        label: '朋友',
        relationship: null,
        proposedRelationship: null,
        relationshipRequestedBy: null,
        version: id,
      },
    ])
    wrapper = mount(ParticipantPicker, { props: { modelValue: [] } })
    await flushPromises()
    await wrapper.setProps({ mentionTrigger: 1 })
    await wrapper.get('input:not([type=checkbox])').setValue('小王')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    await wrapper.get('input[type=checkbox]').setValue(true)
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([['2']])
  })
  it('AI 的好友及分享引用只使用证据白名单和应用内地址', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/:pathMatch(.*)*', component: { template: '<div />' } }],
    })
    await router.push('/assistant')
    wrapper = mount(AssistantMessageContent, {
      props: {
        content: `[Friend #${id}] [Share #${id}]`,
        friends: [
          {
            id,
            person,
            remark: '老朋友',
            label: '朋友',
            relationship: null,
            proposedRelationship: null,
            relationshipRequestedBy: null,
            version: id,
          },
        ],
        sharedContents: [{ shareId: id, title: '旅行', accessPath: `/shares/${id}` }],
      },
      global: { plugins: [router] },
    })
    expect(wrapper.findAll('a')).toHaveLength(2)
    expect(wrapper.text()).toContain('老朋友')
    expect(wrapper.get(`a[href^="/shares/"]`).text()).toBe('旅行')
    await wrapper.setProps({
      sharedContents: [{ shareId: id, title: '恶意地址', accessPath: 'https://evil.test' }],
    })
    expect(wrapper.findAll('a')).toHaveLength(1)
    expect(safeSocialPath('//evil.test')).toBeNull()
    expect(safeSocialPath('/shares/../../account')).toBeNull()
  })
})
