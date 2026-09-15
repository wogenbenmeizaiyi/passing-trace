import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createRouter, createMemoryHistory, RouterView } from 'vue-router'
import { defineComponent, h } from 'vue'
import AccountView from '@/views/AccountView.vue'
import { profileApi, type AccountProfile } from '@/api/profile'
import { HttpError } from '@/api/http-client'
import { accountReturnPath, avatarCropRect, profileTextLength } from '@/utils/avatar-crop'

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    isAuthenticated: true,
    username: 'login_user',
    user: { profile: { sub: 'one' } },
  }),
}))
vi.mock('@/api/profile', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/api/profile')>()
  return {
    ...original,
    profileApi: {
      get: vi.fn<typeof original.profileApi.get>(),
      save: vi.fn<typeof original.profileApi.save>(),
      avatar: vi.fn<typeof original.profileApi.avatar>(),
    },
  }
})
const profile: AccountProfile = {
  username: 'login_user',
  nickname: '旧昵称',
  bio: '',
  version: 'v1',
  hasAvatar: false,
  createdAt: '2026-09-01T00:00:00Z',
}
let wrapper: VueWrapper
beforeEach(() => {
  vi.mocked(profileApi.get).mockResolvedValue({ ...profile })
  vi.mocked(profileApi.save).mockReset()
  vi.spyOn(window, 'confirm').mockReturnValue(false)
})
afterEach(() => {
  wrapper?.unmount()
  vi.restoreAllMocks()
})

async function open() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/account', component: AccountView },
      { path: '/assistant', component: { template: '<p>原聊天</p>' } },
      { path: '/product', component: { template: '<p>产品介绍与下载</p>' } },
    ],
  })
  await router.push('/account?from=%2Fassistant%3Fconversation%3Dabc')
  wrapper = mount(defineComponent({ setup: () => () => h(RouterView) }), {
    global: { plugins: [createPinia(), router], stubs: { WebAppHeader: true } },
  })
  await flushPromises()
  return router
}
function button(label: string) {
  return wrapper.findAll('button').find((item) => item.text() === label)!
}

describe('用户中心', () => {
  it('从独立的手机入口进入产品介绍，不触发保存或直接下载', async () => {
    const router = await open()
    const link = wrapper.get('.account-companion__link')
    expect(link.text()).toContain('在手机上使用')
    expect(link.attributes('href')).toBe('/product')
    await link.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/product')
    expect(profileApi.save).not.toHaveBeenCalled()
  })
  it('通过手机入口离开时仍保护未保存的资料', async () => {
    const router = await open()
    await button('编辑个人资料').trigger('click')
    await wrapper.get('#profile-nickname').setValue('还没保存')
    await wrapper.get('.account-companion__link').trigger('click')
    await flushPromises()
    expect(window.confirm).toHaveBeenCalled()
    expect(router.currentRoute.value.path).toBe('/account')
    expect((wrapper.get('#profile-nickname').element as HTMLInputElement).value).toBe('还没保存')
  })
  it('从资料总览进入编辑，保存昵称与简介而不改登录名', async () => {
    await open()
    expect(wrapper.text()).toContain('login_user')
    expect(wrapper.find('#profile-nickname').exists()).toBe(false)
    await button('编辑个人资料').trigger('click')
    await wrapper.get('#profile-nickname').setValue('我的昵称')
    await wrapper.get('#profile-bio').setValue('喜欢散步')
    vi.mocked(profileApi.save).mockResolvedValue({
      ...profile,
      nickname: '我的昵称',
      bio: '喜欢散步',
      version: 'v2',
    })
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(profileApi.save).toHaveBeenCalledWith('我的昵称', '喜欢散步', 'v1', null, false)
    expect(wrapper.text()).toContain('个人资料已保存')
    expect(wrapper.text()).toContain('login_user')
    expect(wrapper.find('#profile-nickname').exists()).toBe(false)
  })
  it('冲突保留输入并阻止盲目重试', async () => {
    await open()
    await button('编辑个人资料').trigger('click')
    await wrapper.get('#profile-nickname').setValue('还没保存')
    vi.mocked(profileApi.save).mockRejectedValue(new HttpError(409, 'raw database error'))
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(wrapper.text()).toContain('资料已在另一处更新')
    expect(wrapper.text()).not.toContain('raw database')
    expect((wrapper.get('#profile-nickname').element as HTMLInputElement).value).toBe('还没保存')
    expect(button('保存修改').attributes('disabled')).toBeDefined()
    expect(button('重新加载资料').exists()).toBe(true)
  })
  it('校验空昵称，未保存时可以取消离开并保留编辑', async () => {
    const router = await open()
    await button('编辑个人资料').trigger('click')
    await wrapper.get('#profile-nickname').setValue('')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.text()).toContain('昵称请填写 1～24 个字')
    expect(profileApi.save).not.toHaveBeenCalled()
    await router.push('/assistant')
    expect(router.currentRoute.value.path).toBe('/account')
    expect(window.confirm).toHaveBeenCalled()
  })
  it('返回原页面保留会话参数', async () => {
    const router = await open()
    await button('← 返回原页面').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/assistant?conversation=abc')
  })
})
describe('头像裁剪及资料字符规则', () => {
  it('不同长宽比使用正方形裁剪并限定范围', () => {
    expect(avatarCropRect(800, 400, 1, 0.5, 0.5)).toEqual({ left: 200, top: 0, side: 400 })
    expect(avatarCropRect(400, 800, 2, 1, 0)).toEqual({ left: 200, top: 0, side: 200 })
    expect(avatarCropRect(400, 400, 0, -1, 2)).toEqual({ left: 0, top: 0, side: 400 })
  })
  it('表情组合算一个字，拒绝外部返回地址', () => {
    expect(profileTextLength('昵称👨‍👩‍👦')).toBe(3)
    expect(accountReturnPath('https://evil.test')).toBe('/events')
    expect(accountReturnPath('//evil.test')).toBe('/events')
    expect(accountReturnPath('/events\\evil.test')).toBe('/events')
  })
})
