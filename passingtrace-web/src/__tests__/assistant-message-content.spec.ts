import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import AssistantMessageContent from '@/components/AssistantMessageContent'

describe('Web AI 回答', () => {
  it('渲染 Markdown 并把 Event 引用替换为可点击的记录标题', () => {
    const wrapper = mount(AssistantMessageContent, {
      props: {
        content: '**这个月的美食**\n\n- 蘑菇汤 [Event #21]\n- 夜市小吃 [Event #16]',
        records: [
          { eventId: 21, title: '第一次做蘑菇汤' },
          { eventId: 16, title: '逛夜市吃小吃' },
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

    expect(wrapper.get('strong').text()).toBe('这个月的美食')
    expect(wrapper.findAll('li')).toHaveLength(2)
    expect(wrapper.get('a[href="/events/21"]').text()).toBe('第一次做蘑菇汤')
    expect(wrapper.text()).not.toContain('Event #21')
  })
})
