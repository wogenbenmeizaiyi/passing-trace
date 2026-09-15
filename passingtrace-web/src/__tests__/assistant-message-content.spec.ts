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

  it('把 Storyline 引用替换为可点击的故事线标题并保留原对话', () => {
    const conversationId = 'd2759c32-4fb0-4e4b-8d9e-0d7909fbbbc6'
    const storylineId = '9b31d202-7ad3-4b76-8677-5a711f245da0'
    const wrapper = mount(AssistantMessageContent, {
      props: {
        content: `本月仍在执行 [Storyline #${storylineId}]`,
        conversationId,
        storylines: [{ storylineId, title: '从三公里慢慢跑到十公里' }],
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

    const link = wrapper.get(`a[href="/storylines/${storylineId}?conversation=${conversationId}"]`)
    expect(link.text()).toBe('从三公里慢慢跑到十公里')
    expect(wrapper.text()).not.toContain('Storyline #')
  })

  it('故事线证据缺少标题时显示普通用户可理解的链接文字', () => {
    const storylineId = '9b31d202-7ad3-4b76-8677-5a711f245da0'
    const wrapper = mount(AssistantMessageContent, {
      props: { content: `继续查看 [Storyline #${storylineId}]` },
      global: {
        stubs: {
          RouterLink: {
            props: ['to'],
            template: '<a :href="to"><slot /></a>',
          },
        },
      },
    })

    expect(wrapper.get(`a[href="/storylines/${storylineId}"]`).text()).toBe('查看故事线')
    expect(wrapper.text()).not.toContain(storylineId)
  })
})
