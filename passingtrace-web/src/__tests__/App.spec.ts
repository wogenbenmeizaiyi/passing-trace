import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vitest'

import HomeView from '@/views/HomeView.vue'

describe('星期八 Web 产品介绍页', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('默认展示产品介绍、Android 下载和 Web 登录入口', () => {
    const wrapper = mount(HomeView, {
      global: { plugins: [createPinia()], stubs: { RouterLink: { template: '<a><slot /></a>' } } },
    })
    expect(wrapper.text()).toContain('星期八')
    expect(wrapper.text()).toContain('下载 Android 版')
    expect(wrapper.text()).toContain('在网页端登录')
    expect(wrapper.text()).toContain('记录每个当下')
    expect(wrapper.text()).toContain('拼成完整故事线')
    expect(wrapper.text()).toContain('问回自己的生活')
    expect(wrapper.text()).toContain('周末去看海')
    expect(wrapper.text()).toContain('高德导航')
    expect(wrapper.text()).toContain('记录和照片不公开展示')
    expect(wrapper.find('a[href$="/api/v1/app-updates/android/latest/download"]').exists()).toBe(
      true,
    )
    wrapper.unmount()
  })

  it('宣传文案说明使用体验和隐私控制，不展示实现术语或夸大隐私承诺', () => {
    const wrapper = mount(HomeView, {
      global: { plugins: [createPinia()], stubs: { RouterLink: { template: '<a><slot /></a>' } } },
    })
    const copy = wrapper.text()
    for (const term of [
      'S3',
      '对象存储',
      '记录修订',
      '只读检索',
      '检索证据',
      '导航动作',
      '用户范围',
      '访问地址短时有效',
      '已保存坐标',
      '端到端加密',
      '绝对安全',
    ]) {
      expect(copy).not.toContain(term)
    }
    expect(wrapper.get('.hero-note').text()).toContain('相关记录可以点开查看')
    expect(wrapper.get('#privacy').text()).toContain('AI 记住的内容可查看、修改或删除')
    expect(wrapper.get('#assistant').text()).toContain('哪些来自高德地图，都会注明')
    wrapper.unmount()
  })

  it('下载服务失败时留在主页并提供重试', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 404 })),
    )
    const wrapper = mount(HomeView, {
      attachTo: document.body,
      global: { plugins: [createPinia()], stubs: { RouterLink: { template: '<a><slot /></a>' } } },
    })

    await wrapper.find('.hero-actions a').trigger('click')
    await flushPromises()

    expect(wrapper.get('#download-error').text()).toContain('当前暂无可下载的 Android 安装包')
    expect(wrapper.get('#download-error').text()).toContain('重试')
    expect(document.activeElement).toBe(wrapper.get('#download-error').element)
    wrapper.unmount()
  })
})
