import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it } from 'vitest'

import HomeView from '@/views/HomeView.vue'

describe('PassingTrace web home', () => {
  it('只保留扫码登录和记录入口，不显示宣传内容', () => {
    const wrapper = mount(HomeView, {
      global: { plugins: [createPinia()], stubs: { RouterLink: { template: '<a><slot /></a>' } } },
    })
    expect(wrapper.text()).toContain('手机扫码登录')
    expect(wrapper.text()).toContain('我的记录')
    expect(wrapper.text()).not.toContain('最近的轨迹')
    expect(wrapper.text()).not.toContain('生活洞察')
  })
})
