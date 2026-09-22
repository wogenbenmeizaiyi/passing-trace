import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import FriendsPanel from '@/components/FriendsPanel.vue'
import { socialApi, type Friend } from '@/api/social'
import { friendGroups, showMessageTime } from '@/utils/social-presentation'
vi.mock('@/stores/auth', () => ({ useAuthStore: () => ({ user: { profile: { sub: '1' } } }) }))
vi.mock('@/stores/social', () => ({ useSocialStore: () => ({ change: 0 }) }))
vi.mock('@/api/social', () => ({
  socialApi: {
    friends: vi.fn<typeof socialApi.friends>(),
    requests: vi.fn<typeof socialApi.requests>(),
    blocks: vi.fn<typeof socialApi.blocks>(),
    request: vi.fn<typeof socialApi.request>(),
    profiles: vi.fn<typeof socialApi.profiles>(),
    preference: vi.fn<typeof socialApi.preference>(),
  },
}))
const friends: Friend[] = [
  {
    id: 'a',
    person: { id: '2', nickname: '王小明', bio: '', hasAvatar: false, friendCode: 'a' },
    remark: '小王',
    label: '同事',
    relationship: '恋人',
    proposedRelationship: null,
    relationshipRequestedBy: null,
    version: '1',
  },
  {
    id: 'b',
    person: { id: '3', nickname: '小李', bio: '', hasAvatar: false, friendCode: 'b' },
    remark: '',
    label: '',
    relationship: null,
    proposedRelationship: null,
    relationshipRequestedBy: null,
    version: '1',
  },
]
let wrapper: VueWrapper
beforeEach(() => {
  localStorage.clear()
  vi.clearAllMocks()
  vi.mocked(socialApi.friends).mockResolvedValue(friends)
  vi.mocked(socialApi.requests).mockResolvedValue([])
  vi.mocked(socialApi.blocks).mockResolvedValue([])
})
afterEach(() => wrapper?.unmount())
async function open() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/messages', component: FriendsPanel }],
  })
  await router.push('/messages?tab=friends')
  wrapper = mount(FriendsPanel, {
    global: { plugins: [router], stubs: { PersonAvatar: true, FriendCodeCard: true } },
  })
  await flushPromises()
  return router
}
it('按私人标签分组、搜索含确认关系但不改变分组', () => {
  expect(
    friendGroups(friends, '')
      .map((g) => g.label)
      .sort(),
  ).toEqual(['同事', '朋友'])
  expect(friendGroups(friends, '恋人')[0]?.label).toBe('同事')
  expect(friendGroups(friends, '王小明')[0]?.items).toHaveLength(1)
  expect(friendGroups(friends, '不存在')).toHaveLength(0)
  expect(showMessageTime('2026-09-22T10:05:00', '2026-09-22T10:00:00')).toBe(false)
  expect(showMessageTime('2026-09-22T10:05:01', '2026-09-22T10:00:00')).toBe(true)
  expect(showMessageTime('2026-09-23T00:01:00', '2026-09-22T23:59:00')).toBe(true)
})
it('默认展开，折叠不渲染好友，搜索临时展开并恢复账号偏好', async () => {
  await open()
  const group = wrapper.findAll('.group-heading').find((g) => g.text().includes('同事'))!
  await group.trigger('click')
  expect(wrapper.findAll('.friend-select')).toHaveLength(1)
  expect(localStorage.getItem('passingtrace:friend-groups:1')).toContain('同事')
  await wrapper.get('input[type=search]').setValue('小王')
  expect(wrapper.find('.friend-select').text()).toContain('小王')
  await wrapper.get('input[type=search]').setValue('')
  expect(wrapper.findAll('.friend-select')).toHaveLength(1)
  wrapper.unmount()
  await open()
  expect(wrapper.findAll('.friend-select')).toHaveLength(1)
})
it('添加在独立页面，提交防重复，返回保留好友列表', async () => {
  const router = await open()
  expect(wrapper.find('.add-friend').exists()).toBe(false)
  await wrapper.get('[aria-label="添加好友"]').trigger('click')
  await flushPromises()
  expect(router.currentRoute.value.query.panel).toBe('add')
  await wrapper.get('input[placeholder="输入好友码"]').setValue('CODE')
  let done!: () => void
  vi.mocked(socialApi.request).mockImplementation(
    () =>
      new Promise((resolve) => {
        done = () => resolve({ id: 'request' })
      }),
  )
  await wrapper.get('form').trigger('submit')
  await wrapper.get('form').trigger('submit')
  expect(socialApi.request).toHaveBeenCalledTimes(1)
  expect(wrapper.get('form button').attributes('disabled')).toBeDefined()
  done()
  await flushPromises()
  expect(wrapper.text()).toContain('好友申请已发送')
  router.back()
  await flushPromises()
  expect(wrapper.find('.add-friend').exists()).toBe(false)
  expect(wrapper.findAll('.friend-select')).toHaveLength(2)
})

it('左侧保留联系人，右侧只在编辑时展开设置，切换好友取消编辑状态', async () => {
  const router = await open()
  await wrapper.findAll('.friend-select')[0]!.trigger('click')
  await flushPromises()
  expect(router.currentRoute.value.query.friend).toBeTruthy()
  expect(wrapper.find('.contacts-pane').exists()).toBe(true)
  expect(wrapper.find('.friend-detail').exists()).toBe(true)
  expect(wrapper.find('.friend-detail input').exists()).toBe(false)
  expect(wrapper.find('.relationship-form').exists()).toBe(false)
  expect(wrapper.get('.profile-facts').text()).toContain('备注')
  await wrapper.findAll('.section-heading button')[0]!.trigger('click')
  expect(wrapper.find('.preference-form').exists()).toBe(true)
  await wrapper.get('input[maxlength="100"]').setValue('新备注')
  vi.mocked(socialApi.preference).mockResolvedValue(undefined)
  await wrapper.get('.preference-form').trigger('submit')
  await flushPromises()
  expect(socialApi.preference).toHaveBeenCalledWith(
    expect.any(String),
    '新备注',
    expect.any(String),
  )
  expect(wrapper.find('.preference-form').exists()).toBe(false)
  await wrapper.findAll('.section-heading button')[1]!.trigger('click')
  expect(wrapper.find('.relationship-form').exists()).toBe(true)
  await wrapper.findAll('.friend-select')[1]!.trigger('click')
  await flushPromises()
  expect(wrapper.find('.relationship-form').exists()).toBe(false)
  expect(wrapper.get('.profile-more').text()).toContain('删除好友')
  expect(wrapper.get('.profile-actions').text()).toBe('发消息')
  router.back()
  await flushPromises()
  expect(wrapper.find('.friend-detail').exists()).toBe(true)
})
