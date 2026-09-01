import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vitest'

import HomeView from '@/views/HomeView.vue'

describe('PassingTrace web home', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('默认展示产品介绍、Android 下载和 Web 登录入口', () => {
    const wrapper = mount(HomeView, {
      global: { plugins: [createPinia()], stubs: { RouterLink: { template: '<a><slot /></a>' } } },
    })
    expect(wrapper.text()).toContain('下载 Android 版')
    expect(wrapper.text()).toContain('在网页端登录')
    expect(wrapper.text()).toContain('多媒体记录')
    expect(wrapper.text()).toContain('AI 分类与理解')
    expect(wrapper.text()).toContain('私有 S3 对象存储')
    expect(wrapper.find('a[href$="/api/v1/app-updates/android/latest/download"]').exists()).toBe(
      true,
    )
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
