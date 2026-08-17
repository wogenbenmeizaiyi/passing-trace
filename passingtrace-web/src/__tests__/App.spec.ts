import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it } from 'vitest'

import HomeView from '@/views/HomeView.vue'

describe('PassingTrace web home', () => {
  it('renders the product promise and SSO entrance', () => {
    const wrapper = mount(HomeView, {
      global: { plugins: [createPinia()], stubs: { RouterLink: { template: '<a><slot /></a>' } } },
    })
    expect(wrapper.text()).toContain('把生活留下来')
    expect(wrapper.text()).toContain('标准 OIDC · PKCE')
    expect(wrapper.text()).toContain('手机扫码登录')
  })
})
