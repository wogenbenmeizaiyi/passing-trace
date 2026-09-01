import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import EvidenceDisclosure from '@/components/EvidenceDisclosure.vue'

describe('AI 回答相关记录', () => {
  it('默认收起，点击摘要行后展开记录链接', async () => {
    const wrapper = mount(EvidenceDisclosure, {
      props: {
        records: [
          { eventId: 13, title: '整理项目下一阶段计划' },
          { eventId: 8, title: '和朋友吃火锅' },
        ],
      },
      global: {
        stubs: {
          RouterLink: {
            props: ['to'],
            template: '<a :href="to"><slot /></a>',
          },
        },
      },
    })

    const details = wrapper.get('details')
    expect((details.element as HTMLDetailsElement).open).toBe(false)
    expect(wrapper.get('summary').text()).toContain('相关记录')
    expect(wrapper.get('summary').text()).toContain('2 条')

    await wrapper.get('summary').trigger('click')
    expect((details.element as HTMLDetailsElement).open).toBe(true)
    expect(wrapper.get('a[href="/events/13"]').text()).toContain('整理项目下一阶段计划')
  })
})
