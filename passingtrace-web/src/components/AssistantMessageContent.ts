import { defineComponent, h, type PropType, type VNodeChild } from 'vue'
import { RouterLink } from 'vue-router'
import { recordFromConversation, storylineFromConversation } from '@/utils/assistant-navigation'

type InlineToken =
  | { type: 'text'; value: string }
  | { type: 'strong'; value: string }
  | { type: 'code'; value: string }
  | { type: 'event'; eventId: number; value: string }
  | { type: 'storyline'; storylineId: string; value: string }

type Block =
  | { type: 'heading'; level: number; content: string }
  | { type: 'paragraph' | 'quote'; content: string }
  | { type: 'list'; ordered: boolean; items: string[] }
  | { type: 'rule' }

function inlineTokens(
  source: string,
  recordTitles: Map<number, string>,
  storylineTitles: Map<string, string>,
): InlineToken[] {
  const tokens: InlineToken[] = []
  const pattern =
    /\[Event\s*#(\d+)\]|\[Storyline\s*#([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\]|\*\*(.+?)\*\*|`([^`]+)`/gi
  let cursor = 0
  for (const match of source.matchAll(pattern)) {
    const index = match.index ?? 0
    if (index > cursor) tokens.push({ type: 'text', value: source.slice(cursor, index) })
    if (match[1]) {
      const eventId = Number(match[1])
      tokens.push({
        type: 'event',
        eventId,
        value: recordTitles.get(eventId) || '查看记录',
      })
    } else if (match[2] !== undefined) {
      const storylineId = match[2].toLowerCase()
      tokens.push({
        type: 'storyline',
        storylineId,
        value: storylineTitles.get(storylineId) || '查看故事线',
      })
    } else if (match[3] !== undefined) {
      tokens.push({ type: 'strong', value: match[3] })
    } else {
      tokens.push({ type: 'code', value: match[4]! })
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
    conversationId: { type: String as PropType<string | null>, default: null },
    records: {
      type: Array as PropType<Array<{ eventId: number; title: string | null }>>,
      default: () => [],
    },
    storylines: {
      type: Array as PropType<Array<{ storylineId: string; title: string | null }>>,
      default: () => [],
    },
  },
  setup(props) {
    const renderInline = (source: string): VNodeChild[] => {
      const recordTitles = new Map(
        props.records.map((record) => [record.eventId, record.title?.trim() || '']),
      )
      const storylineTitles = new Map(
        props.storylines.map((storyline) => [
          storyline.storylineId.toLowerCase(),
          storyline.title?.trim() || '',
        ]),
      )
      return inlineTokens(source, recordTitles, storylineTitles).map((token) => {
        if (token.type === 'strong') return h('strong', token.value)
        if (token.type === 'code') return h('code', token.value)
        if (token.type === 'event') {
          return h(
            RouterLink,
            {
              class: 'record-citation',
              to: recordFromConversation(token.eventId, props.conversationId),
            },
            { default: () => token.value },
          )
        }
        if (token.type === 'storyline') {
          return h(
            RouterLink,
            {
              class: 'storyline-citation',
              to: storylineFromConversation(token.storylineId, props.conversationId),
            },
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
