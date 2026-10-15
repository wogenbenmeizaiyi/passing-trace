import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import SocialPaneDivider from '@/components/SocialPaneDivider.vue'
import { nextTick } from 'vue'

let wrapper: VueWrapper
beforeEach(() => localStorage.clear())
afterEach(() => wrapper?.unmount())
it('支持键盘与按钮调节、范围限制和按账号恢复宽度', async () => {
  wrapper = mount(SocialPaneDivider, { props: { storageKey: 'width:alice' } })
  const separator = wrapper.get('[role="separator"]')
  expect(separator.attributes('aria-valuenow')).toBe('300')
  await separator.trigger('keydown', { key: 'ArrowRight' })
  expect(separator.attributes('aria-valuenow')).toBe('320')
  await wrapper.get('[aria-label="缩窄列表"]').trigger('click')
  expect(localStorage.getItem('width:alice')).toBe('280')
  await separator.trigger('keydown', { key: 'Home' })
  await separator.trigger('keydown', { key: 'ArrowLeft' })
  expect(separator.attributes('aria-valuenow')).toBe('240')
  await separator.trigger('keydown', { key: 'End' })
  await wrapper.get('[aria-label="加宽列表"]').trigger('click')
  expect(separator.attributes('aria-valuenow')).toBe('440')
  await wrapper.setProps({ storageKey: 'width:bob' })
  expect(separator.attributes('aria-valuenow')).toBe('300')
  await wrapper.setProps({ storageKey: 'width:alice' })
  expect(separator.attributes('aria-valuenow')).toBe('440')
  await separator.trigger('dblclick')
  expect(localStorage.getItem('width:alice')).toBe('300')
})
it('拖动实时调整，结束时保存，右键不启动拖动', async () => {
  wrapper = mount(SocialPaneDivider, { props: { storageKey: 'width:drag' } })
  const separator = wrapper.get('[role="separator"]')
  Object.defineProperty(separator.element, 'setPointerCapture', {
    value: vi.fn<(id: number) => void>(),
  })
  async function pointer(type: string, clientX: number, button = 0) {
    const event = new MouseEvent(type, { bubbles: true, clientX, button })
    Object.defineProperty(event, 'pointerId', { value: 1 })
    separator.element.dispatchEvent(event)
    await nextTick()
  }
  await pointer('pointerdown', 300)
  await pointer('pointermove', 370)
  expect(separator.attributes('aria-valuenow')).toBe('370')
  expect(localStorage.getItem('width:drag')).toBeNull()
  await pointer('pointerup', 370)
  expect(localStorage.getItem('width:drag')).toBe('370')
  await pointer('pointerdown', 370, 2)
  await pointer('pointermove', 100, 2)
  expect(separator.attributes('aria-valuenow')).toBe('370')
})
