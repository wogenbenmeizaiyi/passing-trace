import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import WebAppHeader from '@/components/WebAppHeader.vue'

const auth = vi.hoisted(() => ({
  isAuthenticated: true,
  busy: false,
  login: vi.fn<(destination: string) => Promise<void>>(),
  logout: vi.fn<() => Promise<void>>(),
}))
vi.mock('@/stores/auth', () => ({ useAuthStore: () => auth }))
vi.mock('@/stores/social', () => ({ useSocialStore: () => ({ unread: 0 }) }))
vi.mock('@/stores/profile', () => ({
  useProfileStore: () => ({ nickname: '我的昵称', avatarUrl: '' }),
}))

let wrapper: VueWrapper
beforeEach(() => {
  auth.isAuthenticated = true
  vi.clearAllMocks()
})
afterEach(() => wrapper?.unmount())

async function open(props = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: ['/', '/events', '/storylines', '/assistant', '/messages', '/account', '/product'].map(
      (path) => ({
        path,
        component: { template: '<div />' },
      }),
    ),
  })
  await router.push('/assistant?conversation=abc')
  wrapper = mount(WebAppHeader, {
    props,
    global: {
      plugins: [router],
      stubs: { AppearanceMenu: true, AccountAvatar: true, BrandMark: true },
    },
  })
  return router
}

describe('应用与产品导航层级', () => {
  it('应用主导航包含消息，头像仍能进入用户中心', async () => {
    const router = await open()
    const nav = wrapper.get('nav[aria-label="应用导航"]')
    expect(nav.findAll('a').map((link) => [link.text(), link.attributes('href')])).toEqual([
      ['我的记录', '/events'],
      ['故事线', '/storylines'],
      ['问问 AI', '/assistant'],
      ['消息', '/messages'],
    ])
    expect(nav.get('[href="/assistant"]').attributes('aria-current')).toBe('page')
    expect(wrapper.find('[href="/product"]').exists()).toBe(false)
    await wrapper.get('[aria-label="进入用户中心"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/account')
    expect(router.currentRoute.value.query.from).toBe('/assistant?conversation=abc')
  })
  it('未登录也能从右侧下载 App，不混入主导航或触发扫码登录', async () => {
    auth.isAuthenticated = false
    const router = await open()
    const nav = wrapper.get('nav')
    expect(nav.findAll('a')).toHaveLength(4)
    expect(nav.text()).not.toContain('下载')
    expect(wrapper.text()).toContain('扫码登录')
    const download = wrapper.get('.site-header__actions .app-download-link')
    expect(download.text()).toBe('下载 App')
    expect(download.attributes('href')).toBe('/product')
    await download.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/product')
    expect(auth.login).not.toHaveBeenCalled()
  })
  it('未登录产品页不重复显示应用内的下载引导', async () => {
    auth.isAuthenticated = false
    await open({ variant: 'marketing', downloadUrl: '/download/android' })
    expect(wrapper.find('.app-download-link').exists()).toBe(false)
    expect(wrapper.get('nav[aria-label="产品导航"] [href="/download/android"]').text()).toBe('下载')
  })
  it('产品介绍页保留下载入口及准备状态，避免重复下载', async () => {
    await open({ variant: 'marketing', downloadUrl: '/download/android' })
    const nav = wrapper.get('nav[aria-label="产品导航"]')
    const link = nav.get('[href="/download/android"]')
    expect(link.text()).toBe('下载')
    await link.trigger('click')
    expect(wrapper.emitted('download')).toHaveLength(1)
    await wrapper.setProps({ downloadBusy: true })
    expect(link.attributes('aria-disabled')).toBe('true')
    expect(link.text()).toBe('正在准备…')
    await link.trigger('click')
    expect(wrapper.emitted('download')).toHaveLength(1)
    expect(wrapper.text()).toContain('进入应用')
  })
})
