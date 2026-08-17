import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import HomeView from '@/views/HomeView.vue'

describe('SSO verification site', () => {
  it('explains the independent client proof', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: HomeView }],
    })
    await router.push('/')
    await router.isReady()

    const wrapper = mount(HomeView, {
      global: {
        plugins: [router],
      },
    })
    expect(wrapper.text()).toContain('这是另一个网站')
    expect(wrapper.text()).toContain('发起 SSO 授权')
    expect(wrapper.text()).toContain('passingtrace-sso-demo')
  })
})
