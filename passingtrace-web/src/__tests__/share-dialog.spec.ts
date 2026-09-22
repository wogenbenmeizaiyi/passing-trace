import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import ShareDialog from '@/components/ShareDialog.vue'
import { socialApi, type Friend, type SharedDocument } from '@/api/social'

vi.mock('@/api/social', () => ({
  socialApi: {
    friends: vi.fn<typeof socialApi.friends>(),
    preview: vi.fn<typeof socialApi.preview>(),
    shares: vi.fn<typeof socialApi.shares>(),
    open: vi.fn<typeof socialApi.open>(),
    send: vi.fn<typeof socialApi.send>(),
  },
}))
const friend: Friend = {
  id: 'friendship',
  person: { id: '2', nickname: '小林', bio: '', hasAvatar: false, friendCode: 'code' },
  remark: '',
  label: '',
  relationship: null,
  proposedRelationship: null,
  relationshipRequestedBy: null,
  version: '1',
}
const document: SharedDocument = {
  kind: 'storyline',
  title: '探店清单',
  description: null,
  authorId: '1',
  records: [],
  stages: [],
  nodes: [],
  edges: [],
  status: 'Ongoing',
  available: true,
}
let wrapper: VueWrapper
const originalShowModal = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'showModal')
beforeAll(() =>
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', {
    configurable: true,
    value: vi.fn<() => void>(),
  }),
)
afterAll(() => {
  if (originalShowModal)
    Object.defineProperty(HTMLDialogElement.prototype, 'showModal', originalShowModal)
  else Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal')
})
beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(socialApi.friends).mockResolvedValue([friend])
  vi.mocked(socialApi.preview).mockResolvedValue(document)
  vi.mocked(socialApi.shares).mockResolvedValue([])
  vi.mocked(socialApi.open).mockResolvedValue({ id: 'chat' })
})
afterEach(() => {
  wrapper?.unmount()
  vi.restoreAllMocks()
})
async function open() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/messages/:id', component: { template: '<div />' } },
    ],
  })
  await router.push('/')
  wrapper = mount(ShareDialog, {
    props: { storylineId: 'story', initialFriendId: friend.id },
    global: { plugins: [router] },
  })
  await flushPromises()
  return router
}
describe('分享预览', () => {
  it('预览失败时保留好友列表，不能发送，重试成功后可以分享', async () => {
    vi.mocked(socialApi.preview).mockRejectedValueOnce(new Error('technical error'))
    await open()
    expect(wrapper.get('select').text()).toContain('小林')
    expect(wrapper.get('.button-primary').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('分享内容暂时无法加载')
    expect(wrapper.text()).not.toContain('technical error')
    await wrapper.get('.load-error button').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('探店清单')
    expect(wrapper.get('.button-primary').attributes('disabled')).toBeUndefined()
    expect(socialApi.preview).toHaveBeenCalledTimes(2)
  })
  it('分享历史加载失败不丢弃成功预览，也不阻止发送', async () => {
    vi.mocked(socialApi.shares).mockRejectedValue(new Error('unavailable'))
    const router = await open()
    expect(wrapper.text()).toContain('探店清单')
    expect(wrapper.text()).toContain('不影响本次分享')
    const send = wrapper.get('.button-primary')
    await send.trigger('click')
    await send.trigger('click')
    await flushPromises()
    expect(socialApi.send).toHaveBeenCalledTimes(1)
    expect(socialApi.send).toHaveBeenCalledWith(
      'chat',
      expect.objectContaining({ kind: 'storyline', storylineId: 'story' }),
    )
    expect(router.currentRoute.value.path).toBe('/messages/chat')
  })
  it('好友加载失败时即使已有预选好友也不能发送', async () => {
    vi.mocked(socialApi.friends).mockRejectedValue(new Error('unavailable'))
    await open()
    expect(wrapper.text()).toContain('探店清单')
    expect(wrapper.text()).toContain('好友列表暂时无法加载')
    expect(wrapper.get('.button-primary').attributes('disabled')).toBeDefined()
    expect(socialApi.send).not.toHaveBeenCalled()
  })
})
