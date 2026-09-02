import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'

import AppearanceMenu from '@/components/AppearanceMenu.vue'
import { appearanceMode, appearancePalette } from '@/theme/appearance'

describe('Web 主题与外观', () => {
  afterEach(() => {
    appearanceMode.value = 'system'
    appearancePalette.value = 'pine'
    document.documentElement.removeAttribute('data-theme')
    document.documentElement.removeAttribute('data-palette')
    window.localStorage.clear()
  })

  it('提供与手机一致的四套颜色主题和显示模式', async () => {
    const wrapper = mount(AppearanceMenu, { attachTo: document.body })
    await wrapper.get('.appearance-trigger').trigger('click')

    expect(wrapper.text()).toContain('松间')
    expect(wrapper.text()).toContain('潮汐')
    expect(wrapper.text()).toContain('暮紫')
    expect(wrapper.text()).toContain('沙丘')
    expect(wrapper.text()).toContain('跟随系统')

    await wrapper.get('.palette-tide').trigger('click')
    expect(document.documentElement.dataset.palette).toBe('tide')
    expect(window.localStorage.getItem('passingtrace.appearance.palette')).toBe('tide')
    wrapper.unmount()
  })

  it('点击菜单之外会关闭主题面板', async () => {
    const wrapper = mount(AppearanceMenu, { attachTo: document.body })
    await wrapper.get('.appearance-trigger').trigger('click')
    expect(wrapper.find('.appearance-popover').exists()).toBe(true)

    document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }))
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.appearance-popover').exists()).toBe(false)
    wrapper.unmount()
  })
})
