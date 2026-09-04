import { defineComponent, h, type PropType, type VNodeChild } from 'vue'
import { RouterLink } from 'vue-router'

type InlineToken =
  | { type: 'text'; value: string }
  | { type: 'strong'; value: string }
  | { type: 'code'; value: string }
  | { type: 'event'; eventId: number; value: string }

type Block =
  | { type: 'heading'; level: number; content: string }
  | { type: 'paragraph' | 'quote'; content: string }
  | { type: 'list'; ordered: boolean; items: string[] }
  | { type: 'rule' }

function inlineTokens(source: string, titles: Map<number, string>): InlineToken[] {
  const tokens: InlineToken[] = []
  const pattern = /\[Event\s*#(\d+)\]|\*\*(.+?)\*\*|`([^`]+)`/gi
  let cursor = 0
  for (const match of source.matchAll(pattern)) {
    const index = match.index ?? 0
    if (index > cursor) tokens.push({ type: 'text', value: source.slice(cursor, index) })
    if (match[1]) {
      const eventId = Number(match[1])
      tokens.push({ type: 'event', eventId, value: titles.get(eventId) || '查看记录' })
    } else if (match[2] !== undefined) {
      tokens.push({ type: 'strong', value: match[2] })
    } else {
      tokens.push({ type: 'code', value: match[3]! })
    }
    cursor = index + match[0].length
  }
  if (cursor < source.length) tokens.push({ type: 'text', value: source.slice(cursor) })
  return tokens
}

function blocks(source: string): Block[] {
  const result: Block[] = []
  const lines = source.replace(/\r\n/g, '\n').split('\n')
  let paragraph: string[] = []
  let list: { ordered: boolean; items: string[] } | null = null
  const flushParagraph = () => {
    if (paragraph.length) result.push({ type: 'paragraph', content: paragraph.join('\n') })
    paragraph = []
  }
  const flushList = () => {
    if (list) result.push({ type: 'list', ordered: list.ordered, items: list.items })
    list = null
  }

  for (const line of lines) {
    const heading = /^(#{1,3})\s+(.+)$/.exec(line)
    const listItem = /^\s*(?:(\d+)[.)]|[-*])\s+(.+)$/.exec(line)
    if (!line.trim()) {
      flushParagraph()
      flushList()
    } else if (/^\s*(?:---+|___+)\s*$/.test(line)) {
      flushParagraph()
      flushList()
      result.push({ type: 'rule' })
    } else if (heading) {
      flushParagraph()
      flushList()
      result.push({ type: 'heading', level: heading[1]!.length, content: heading[2]! })
    } else if (listItem) {
      flushParagraph()
      const ordered = Boolean(listItem[1])
      if (list && list.ordered !== ordered) flushList()
      list ??= { ordered, items: [] }
      list.items.push(listItem[2]!)
    } else if (line.startsWith('> ')) {
      flushParagraph()
      flushList()
      result.push({ type: 'quote', content: line.slice(2) })
    } else {
      flushList()
      paragraph.push(line)
    }
  }
  flushParagraph()
  flushList()
  return result
}

export default defineComponent({
  name: 'AssistantMessageContent',
  props: {
    content: { type: String, required: true },
    records: {
      type: Array as PropType<Array<{ eventId: number; title: string | null }>>,
      default: () => [],
    },
  },
  setup(props) {
    const renderInline = (source: string): VNodeChild[] => {
      const titles = new Map(
        props.records.map((record) => [record.eventId, record.title?.trim() || '']),
      )
      return inlineTokens(source, titles).map((token) => {
        if (token.type === 'strong') return h('strong', token.value)
        if (token.type === 'code') return h('code', token.value)
        if (token.type === 'event') {
          return h(
            RouterLink,
            { class: 'record-citation', to: `/events/${token.eventId}` },
            { default: () => token.value },
          )
        }
        return token.value
      })
    }

    return () =>
      h(
        'div',
        { class: 'assistant-markdown' },
        blocks(props.content || '正在检索你的记录…').map((block) => {
          if (block.type === 'rule') return h('hr')
          if (block.type === 'heading') return h(`h${block.level}`, renderInline(block.content))
          if (block.type === 'quote') return h('blockquote', renderInline(block.content))
          if (block.type === 'list') {
            return h(
              block.ordered ? 'ol' : 'ul',
              block.items.map((item) => h('li', renderInline(item))),
            )
          }
          return h(
            'p',
            block.content
              .split('\n')
              .flatMap((line, index) =>
                index ? [h('br'), ...renderInline(line)] : renderInline(line),
              ),
          )
        }),
      )
  },
})
