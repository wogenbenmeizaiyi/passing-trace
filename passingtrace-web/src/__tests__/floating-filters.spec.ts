import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import FloatingFilters from '@/components/FloatingFilters.vue'
import type { FilterField } from '@/components/filter-types'
const fields: FilterField[] = [
  {
    key: 'scope',
    label: '来源',
    defaultValue: 'own',
    options: [
      { value: 'own', label: '我的' },
      { value: 'joint', label: '共同' },
    ],
  },
  { key: 'from', label: '开始日期', type: 'date', ownOnly: true },
  { key: 'to', label: '结束日期', type: 'date', ownOnly: true },
  {
    key: 'tags',
    label: '标签',
    type: 'tags',
    ownOnly: true,
    options: [
      { value: 'food', label: '餐饮' },
      { value: 'walk', label: '散步' },
    ],
  },
]
let wrapper: VueWrapper
const originalShow = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'showModal')
const originalClose = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'close')
beforeAll(() => {
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', {
    configurable: true,
    writable: true,
    value(this: HTMLDialogElement) {
      this.open = true
    },
  })
  Object.defineProperty(HTMLDialogElement.prototype, 'close', {
    configurable: true,
    writable: true,
    value(this: HTMLDialogElement) {
      this.open = false
    },
  })
})
afterAll(() => {
  if (originalShow) Object.defineProperty(HTMLDialogElement.prototype, 'showModal', originalShow)
  else Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal')
  if (originalClose) Object.defineProperty(HTMLDialogElement.prototype, 'close', originalClose)
  else Reflect.deleteProperty(HTMLDialogElement.prototype, 'close')
})
beforeEach(() => {
  vi.spyOn(HTMLDialogElement.prototype, 'showModal').mockImplementation(function (
    this: HTMLDialogElement,
  ) {
    this.open = true
  })
  vi.spyOn(HTMLDialogElement.prototype, 'close').mockImplementation(function (
    this: HTMLDialogElement,
  ) {
    this.open = false
  })
  wrapper = mount(FloatingFilters, {
    attachTo: document.body,
    props: { title: '筛选记录', fields, values: { scope: 'own', from: '', to: '', tags: [] } },
    global: { stubs: { Teleport: true } },
  })
})
afterEach(() => {
  wrapper?.unmount()
  vi.restoreAllMocks()
})
describe('统一悬浮筛选', () => {
  it('按需打开，关闭丢弃草稿并返回焦点，应用才提交', async () => {
    expect(wrapper.find('form').exists()).toBe(false)
    await wrapper.get('.filter-fab').trigger('click')
    await wrapper.get('input[type="date"]').setValue('2026-09-01')
    expect(wrapper.emitted('apply')).toBeUndefined()
    await wrapper.get('dialog').trigger('cancel')
    expect(document.activeElement).toBe(wrapper.get('.filter-fab').element)
    await wrapper.get('.filter-fab').trigger('click')
    expect((wrapper.get('input[type="date"]').element as HTMLInputElement).value).toBe('')
    await wrapper.get('input[type="date"]').setValue('2026-09-02')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.emitted('apply')?.[0]?.[0]).toMatchObject({ from: '2026-09-02' })
    expect(wrapper.find('form').exists()).toBe(false)
  })
  it('拒绝倒置日期，重置只修改草稿', async () => {
    await wrapper.get('.filter-fab').trigger('click')
    const dates = wrapper.findAll('input[type="date"]')
    await dates[0]!.setValue('2026-09-20')
    await dates[1]!.setValue('2026-09-01')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.get('[role="alert"]').text()).toContain('不能早于')
    expect(wrapper.emitted('apply')).toBeUndefined()
    await wrapper.get('.filter-actions button[type="button"]').trigger('click')
    expect(wrapper.emitted('apply')).toBeUndefined()
    await wrapper.get('form').trigger('submit')
    expect(wrapper.emitted('apply')?.[0]?.[0]).toEqual({ scope: 'own', from: '', to: '', tags: [] })
  })
  it('多选标签有摘要、数量、单项移除及清除全部', async () => {
    await wrapper.get('.filter-fab').trigger('click')
    await wrapper.findAll('input[type="checkbox"]')[0]!.setValue(true)
    await wrapper.findAll('input[type="checkbox"]')[1]!.setValue(true)
    await wrapper.get('form').trigger('submit')
    expect(wrapper.emitted('apply')?.[0]?.[0]).toMatchObject({ tags: ['food', 'walk'] })
    await wrapper.setProps({ values: { scope: 'own', tags: ['food', 'walk'] } })
    expect(wrapper.get('.filter-fab').attributes('aria-label')).toContain('2 项')
    await wrapper.get('[aria-label="移除标签：餐饮"]').trigger('click')
    expect(wrapper.emitted('apply')?.[1]?.[0]).toMatchObject({ tags: ['walk'] })
    await wrapper.get('.filter-clear').trigger('click')
    expect(wrapper.emitted('apply')?.[2]?.[0]).toMatchObject({ tags: [], scope: 'own' })
  })
  it('共同来源隐藏不适用的细项，应用时清除旧条件', async () => {
    await wrapper.setProps({ values: { scope: 'own', from: '2026-09-01', to: '', tags: ['food'] } })
    await wrapper.get('.filter-fab').trigger('click')
    await wrapper.get('select').setValue('joint')
    expect(wrapper.find('input[type="date"]').exists()).toBe(false)
    await wrapper.get('form').trigger('submit')
    expect(wrapper.emitted('apply')?.[0]?.[0]).toEqual({
      scope: 'joint',
      from: '',
      to: '',
      tags: [],
    })
  })
})
