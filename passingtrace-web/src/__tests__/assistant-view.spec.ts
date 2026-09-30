import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { User } from 'oidc-client-ts'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  aiApi,
  type ConversationDetail,
  type ConversationMessagePage,
  type ConversationSummary,
} from '@/api/ai'
import { useAuthStore } from '@/stores/auth'
import AssistantView from '@/views/AssistantView.vue'

vi.mock('@/api/ai', () => ({
  aiApi: {
    listConversations: vi.fn<typeof aiApi.listConversations>(),
    getConversation: vi.fn<typeof aiApi.getConversation>(),
    listConversationsPage: vi.fn<typeof aiApi.listConversationsPage>(),
    getConversationMessagesPage: vi.fn<typeof aiApi.getConversationMessagesPage>(),
    getConversationSummary: vi.fn<typeof aiApi.getConversationSummary>(),
    createConversation: vi.fn<typeof aiApi.createConversation>(),
    deleteConversation: vi.fn<typeof aiApi.deleteConversation>(),
    listMemories: vi.fn<typeof aiApi.listMemories>(),
    updateMemory: vi.fn<typeof aiApi.updateMemory>(),
    deleteMemory: vi.fn<typeof aiApi.deleteMemory>(),
    sendMessage: vi.fn<typeof aiApi.sendMessage>(),
    listApprovals: vi.fn<typeof aiApi.listApprovals>(),
    decideApproval: vi.fn<typeof aiApi.decideApproval>(),
  },
}))

const first: ConversationSummary = {
  id: '11111111-1111-4111-8111-111111111111',
  title: '九月生活回顾',
  createdAt: '2026-09-01T09:00:00Z',
  updatedAt: '2026-09-14T09:00:00Z',
}
const second: ConversationSummary = {
  id: '22222222-2222-4222-8222-222222222222',
  title: '周末出游安排',
  createdAt: '2026-09-02T09:00:00Z',
  updatedAt: '2026-09-13T09:00:00Z',
}

function detail(conversation: ConversationSummary): ConversationDetail {
  return {
    ...conversation,
    messages: [
      {
        id: 1,
        role: 'User',
        content: `${conversation.title}的问题`,
        createdAt: conversation.createdAt,
        evidence: null,
      },
      {
        id: 2,
        role: 'Assistant',
        content: `${conversation.title}的回答`,
        createdAt: conversation.updatedAt,
        evidence: null,
      },
    ],
  }
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise
  })
  return { promise, resolve }
}

function messagePage(conversation: ConversationSummary): ConversationMessagePage {
  return { items: detail(conversation).messages, hasMore: false, nextBeforeId: null }
}

const wrappers: VueWrapper[] = []

async function mountAssistant(openFirst = true, renderReferences = false) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/assistant', component: AssistantView }],
  })
  await router.push('/assistant')
  await router.isReady()
  const wrapper = mount(AssistantView, {
    attachTo: document.body,
    global: {
      plugins: [router],
      stubs: {
        WebAppHeader: true,
        BrandMark: true,
        AmapActionCards: true,
        EvidenceDisclosure: true,
        AssistantMessageContent: renderReferences
          ? false
          : {
              props: ['content', 'records'],
              template: '<div class="assistant-markdown">{{ content }}</div>',
            },
      },
    },
  })
  wrappers.push(wrapper)
  await flushPromises()
  if (openFirst) {
    await wrapper.findAll('.conversation-open')[0]!.trigger('click')
    await flushPromises()
  }
  return wrapper
}

function deleteButton(wrapper: VueWrapper, conversation: ConversationSummary) {
  return wrapper.get<HTMLButtonElement>(`button[aria-label="删除对话：${conversation.title}"]`)
}

function dialogButton(wrapper: VueWrapper, label: string) {
  return wrapper
    .get('dialog[open]')
    .findAll<HTMLButtonElement>('button')
    .find((button) => button.text() === label)!
}

describe('助手对话历史', () => {
  const originalShowModal = Object.getOwnPropertyDescriptor(
    HTMLDialogElement.prototype,
    'showModal',
  )
  const originalClose = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'close')

  beforeAll(() => {
    // jsdom does not provide native dialog top-layer behavior.
    Object.defineProperties(HTMLDialogElement.prototype, {
      showModal: {
        configurable: true,
        value(this: HTMLDialogElement) {
          this.open = true
        },
      },
      close: {
        configurable: true,
        value(this: HTMLDialogElement) {
          this.open = false
          this.dispatchEvent(new Event('close'))
        },
      },
    })
  })

  afterAll(() => {
    if (originalShowModal)
      Object.defineProperty(HTMLDialogElement.prototype, 'showModal', originalShowModal)
    else Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal')
    if (originalClose) Object.defineProperty(HTMLDialogElement.prototype, 'close', originalClose)
    else Reflect.deleteProperty(HTMLDialogElement.prototype, 'close')
  })

  beforeEach(() => {
    vi.resetAllMocks()
    setActivePinia(createPinia())
    useAuthStore().user = {
      access_token: 'test-token',
      profile: { sub: 'test-user' },
      expired: false,
    } as User
    vi.mocked(aiApi.listConversationsPage).mockResolvedValue({
      items: [{ ...first }, { ...second }],
      nextCursor: null,
    })
    vi.mocked(aiApi.getConversationMessagesPage).mockImplementation(async (id) =>
      messagePage(id === first.id ? first : second),
    )
    vi.mocked(aiApi.getConversationSummary).mockResolvedValue(first)
    vi.mocked(aiApi.createConversation).mockResolvedValue({
      ...first,
      id: '33333333-3333-4333-8333-333333333333',
      title: '新对话',
    })
    vi.mocked(aiApi.deleteConversation).mockResolvedValue(undefined)
    vi.mocked(aiApi.listMemories).mockResolvedValue([])
    vi.mocked(aiApi.sendMessage).mockResolvedValue(undefined)
    vi.mocked(aiApi.listApprovals).mockResolvedValue([])
  })

  afterEach(() => {
    for (const wrapper of wrappers.splice(0)) wrapper.unmount()
  })

  it('进入页面只取一页标题，点开对话才加载其消息', async () => {
    const wrapper = await mountAssistant(false)
    expect(aiApi.listConversationsPage).toHaveBeenCalledTimes(1)
    expect(aiApi.listConversations).not.toHaveBeenCalled()
    expect(aiApi.getConversation).not.toHaveBeenCalled()
    expect(aiApi.getConversationMessagesPage).not.toHaveBeenCalled()
    expect(wrapper.find('.assistant-empty').exists()).toBe(true)
    expect(wrapper.findAll('.history-month h3').map((item) => item.text())).toEqual([
      '2026 年 9 月',
    ])

    await wrapper.findAll('.conversation-open')[1]!.trigger('click')
    await flushPromises()
    expect(aiApi.getConversationMessagesPage).toHaveBeenCalledExactlyOnceWith(second.id)
    expect(wrapper.get('.messages').text()).toContain(`${second.title}的回答`)
  })

  const approval = {
    id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    conversationId: first.id,
    targetType: 'Plan' as const,
    targetId: '42',
    title: '周末跑步',
    description: '删除这条计划。',
    expiresAt: '2026-09-30T12:15:00Z',
  }
  const mutation = {
    operationId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
    operation: 'CreateMyRecord',
    state: 'Succeeded',
    targets: [{ type: 'Plan' as const, id: '42', title: '周末跑步', revision: 1 }],
    message: {
      id: 99,
      role: 'Assistant',
      content: '已创建：[Event #42]',
      createdAt: '2026-09-30T12:00:00Z',
      evidence: { records: [{ eventId: 42, title: '周末跑步' }], memories: [], aggregate: null },
    },
  }

  it('恢复删除授权，点击确定才提交；执行中禁止重复点击', async () => {
    vi.mocked(aiApi.listApprovals).mockResolvedValue([approval])
    const pending = deferred<Awaited<ReturnType<typeof aiApi.decideApproval>>>()
    vi.mocked(aiApi.decideApproval).mockReturnValue(pending.promise)
    const wrapper = await mountAssistant(true, true)
    const panel = wrapper.get('[aria-label="删除授权"]')
    expect(panel.text()).toContain('周末跑步')
    expect(aiApi.decideApproval).not.toHaveBeenCalled()
    expect(
      panel.element.compareDocumentPosition(wrapper.get('.composer').element) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
    await panel
      .findAll('button')
      .find((button) => button.text() === '确定')!
      .trigger('click')
    expect(aiApi.decideApproval).toHaveBeenCalledExactlyOnceWith(first.id, approval.id, 'confirm')
    expect(
      panel.findAll<HTMLButtonElement>('button').every((button) => button.element.disabled),
    ).toBe(true)
    pending.resolve({
      id: approval.id,
      state: 'Succeeded',
      result: {
        ...mutation,
        operation: 'Delete',
        message: { ...mutation.message, content: '已删除：周末跑步', evidence: null },
      },
    })
    await flushPromises()
    expect(wrapper.find('[aria-label="删除授权"]').exists()).toBe(false)
    expect(wrapper.get('.messages').text()).toContain('已删除：周末跑步')
  })

  it('取消删除和授权失败可重试，不误报删除成功', async () => {
    vi.mocked(aiApi.listApprovals).mockResolvedValue([approval])
    vi.mocked(aiApi.decideApproval)
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce({
        id: approval.id,
        state: 'Cancelled',
        result: {
          ...mutation,
          operation: 'Delete',
          state: 'Cancelled',
          message: { ...mutation.message, content: '已取消删除：周末跑步', evidence: null },
        },
      })
    const wrapper = await mountAssistant()
    const cancel = () =>
      wrapper
        .get('[aria-label="删除授权"]')
        .findAll('button')
        .find((button) => button.text() === '取消')!
    await cancel().trigger('click')
    await flushPromises()
    expect(wrapper.get('[aria-label="删除授权"]').text()).toContain('请重试')
    await cancel().trigger('click')
    await flushPromises()
    expect(aiApi.decideApproval).toHaveBeenLastCalledWith(first.id, approval.id, 'cancel')
    expect(wrapper.get('.messages').text()).toContain('已取消删除')
  })

  it('写入回执独立显示链接并去重，后续回答失败仍保留', async () => {
    vi.mocked(aiApi.sendMessage).mockImplementation(async (_id, _text, onEvent) => {
      onEvent({ type: 'mutation-result', data: mutation })
      onEvent({ type: 'mutation-result', data: mutation })
      onEvent({ type: 'approval-request', data: approval })
      onEvent({ type: 'error', data: { message: '回答中断' } })
    })
    const wrapper = await mountAssistant(true, true)
    await wrapper.get('.composer textarea').setValue('创建一个跑步计划')
    await wrapper.get('.composer').trigger('submit')
    await flushPromises()
    const links = wrapper.findAll('.messages a').filter((link) => link.text() === '周末跑步')
    expect(links).toHaveLength(1)
    expect(links[0]!.attributes('href')).toContain('/events/42')
    expect(wrapper.get('.messages').text()).toContain('已创建')
    expect(wrapper.get('[aria-label="删除授权"]').text()).toContain('周末跑步')
    expect(wrapper.text()).toContain('回答中断')
  })

  it('切换会话不显示另一段聊天的授权', async () => {
    vi.mocked(aiApi.listApprovals).mockImplementation(async (id) =>
      id === first.id ? [approval] : [],
    )
    const wrapper = await mountAssistant()
    expect(wrapper.find('[aria-label="删除授权"]').exists()).toBe(true)
    await wrapper.findAll('.conversation-open')[1]!.trigger('click')
    await flushPromises()
    expect(wrapper.find('[aria-label="删除授权"]').exists()).toBe(false)
  })

  it('授权读取失败显示重试，退出登录清除授权和草稿', async () => {
    vi.mocked(aiApi.listApprovals)
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce([approval])
    const wrapper = await mountAssistant()
    expect(wrapper.get('[role="alert"]').text()).toContain('删除授权暂时无法读取')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '重试读取')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.find('[aria-label="删除授权"]').exists()).toBe(true)
    await wrapper.get('textarea').setValue('私密草稿')
    useAuthStore().user = null
    await flushPromises()
    expect(wrapper.find('[aria-label="删除授权"]').exists()).toBe(false)
    expect(aiApi.decideApproval).not.toHaveBeenCalled()
  })

  it('更早的标题按需加载，阻止重复请求并去重，不获取正文', async () => {
    vi.mocked(aiApi.listConversationsPage).mockResolvedValueOnce({
      items: [first],
      nextCursor: 'page-2',
    })
    const wrapper = await mountAssistant(false)
    const page = deferred<{ items: ConversationSummary[]; nextCursor: string | null }>()
    vi.mocked(aiApi.listConversationsPage).mockReturnValueOnce(page.promise)
    const button = wrapper.get<HTMLButtonElement>('.history-load-more')
    await button.trigger('click')
    button.element.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    expect(aiApi.listConversationsPage).toHaveBeenCalledTimes(2)
    expect(aiApi.listConversationsPage).toHaveBeenLastCalledWith('page-2')
    page.resolve({ items: [first, second], nextCursor: null })
    await flushPromises()
    expect(wrapper.findAll('.conversation')).toHaveLength(2)
    expect(wrapper.find('.history-load-more').exists()).toBe(false)
    expect(aiApi.getConversationMessagesPage).not.toHaveBeenCalled()
  })

  it('较早消息仅在点击时加载，插入顶部并保持阅读位置', async () => {
    vi.mocked(aiApi.getConversationMessagesPage).mockResolvedValueOnce({
      items: detail(first).messages.map((item) => ({ ...item, id: item.id + 20 })),
      hasMore: true,
      nextBeforeId: 21,
    })
    const wrapper = await mountAssistant()
    const element = wrapper.get<HTMLElement>('.messages').element
    Object.defineProperty(element, 'scrollHeight', {
      get: () => wrapper.findAll('article.message').length * 100,
    })
    element.scrollTop = 15
    vi.mocked(aiApi.getConversationMessagesPage).mockResolvedValueOnce(messagePage(second))
    await wrapper.get('.earlier-messages button').trigger('click')
    await flushPromises()
    expect(aiApi.getConversationMessagesPage).toHaveBeenLastCalledWith(first.id, 21)
    expect(wrapper.findAll('article.message')).toHaveLength(4)
    expect(wrapper.findAll('article.message')[0]!.text()).toContain(second.title)
    expect(element.scrollTop).toBe(215)
    expect(wrapper.find('.earlier-messages').exists()).toBe(false)
  })

  it('更早消息加载失败可重试且不清空已显示内容', async () => {
    vi.mocked(aiApi.getConversationMessagesPage).mockResolvedValueOnce({
      ...messagePage(first),
      hasMore: true,
      nextBeforeId: 1,
    })
    const wrapper = await mountAssistant()
    vi.mocked(aiApi.getConversationMessagesPage).mockRejectedValueOnce(
      new Error('Connection failed'),
    )
    await wrapper.get('.earlier-messages button').trigger('click')
    await flushPromises()
    expect(wrapper.get('.earlier-messages [role="alert"]').text()).toContain('请重试')
    expect(wrapper.get('.messages').text()).toContain(`${first.title}的回答`)
    expect(wrapper.get<HTMLButtonElement>('.earlier-messages button').element.disabled).toBe(false)
  })

  it('回答后只刷新当前会话标题，不重新加载整个列表或正文', async () => {
    const wrapper = await mountAssistant()
    vi.mocked(aiApi.getConversationSummary).mockResolvedValueOnce({
      ...first,
      title: '九月消费统计',
    })
    await wrapper.get('textarea').setValue('统计消费')
    await wrapper.get('.composer').trigger('submit')
    await flushPromises()
    expect(aiApi.getConversationSummary).toHaveBeenCalledExactlyOnceWith(first.id)
    expect(aiApi.listConversationsPage).toHaveBeenCalledTimes(1)
    expect(aiApi.getConversationMessagesPage).toHaveBeenCalledTimes(1)
    expect(wrapper.get('.conversation.active').text()).toBe('九月消费统计')
  })

  it('回答中断后停止展示正在检索的空气泡', async () => {
    vi.mocked(aiApi.sendMessage).mockImplementationOnce(async (_id, _content, onEvent) => {
      onEvent({ type: 'error', data: { message: '这次回答中途断开了，请重新发送一次。' } })
    })
    const wrapper = await mountAssistant()
    await wrapper.get('textarea').setValue('查看我的运动量')
    await wrapper.get('.composer').trigger('submit')
    await flushPromises()
    expect(wrapper.get('.messages').text()).not.toContain('正在检索')
    expect(wrapper.get('.error-banner').text()).toContain('重新发送')
    expect(wrapper.findAll('article.message.assistant').slice(-1)[0]!.isVisible()).toBe(false)
  })

  it('聊天只展示正文，不重复显示名称和头像，读屏仍能区分双方', async () => {
    const wrapper = await mountAssistant()
    const userMessage = wrapper.get('article.message.user')
    const assistantMessage = wrapper.get('article.message.assistant')

    expect(userMessage.text()).toBe(`${first.title}的问题`)
    expect(assistantMessage.text()).toBe(`${first.title}的回答`)
    expect(userMessage.attributes('aria-label')).toBe('你的消息')
    expect(assistantMessage.attributes('aria-label')).toBe('AI 回复')
    expect(wrapper.find('.message-body > strong').exists()).toBe(false)
    expect(wrapper.find('.message-mark').exists()).toBe(false)
    expect(wrapper.find('.message brand-mark-stub').exists()).toBe(false)
  })

  it('每段历史有独立的打开和删除按钮，删除前展示目标标题并可取消', async () => {
    const wrapper = await mountAssistant()
    expect(wrapper.findAll('.conversation-open')).toHaveLength(2)
    expect(wrapper.findAll('.conversation-delete')).toHaveLength(2)
    expect(wrapper.find('button button').exists()).toBe(false)
    const trigger = deleteButton(wrapper, second)
    await trigger.trigger('click')

    const dialog = wrapper.get('dialog[open]')
    expect(wrapper.get(`#${dialog.attributes('aria-labelledby')}`).text()).toBe('删除这段对话？')
    expect(wrapper.get(`#${dialog.attributes('aria-describedby')}`).text()).toContain(
      '你的记录、计划、故事线和长期记忆不会被删除',
    )
    expect(dialog.text()).toContain(second.title)
    expect(aiApi.deleteConversation).not.toHaveBeenCalled()
    expect(aiApi.getConversationMessagesPage).toHaveBeenCalledTimes(1)
    expect(wrapper.get('.conversation.active').text()).toContain(first.title)

    await dialogButton(wrapper, '取消').trigger('click')
    expect(wrapper.find('dialog[open]').exists()).toBe(false)
    expect(wrapper.findAll('.conversation')).toHaveLength(2)
    expect(wrapper.get('.messages').text()).toContain(`${first.title}的回答`)
    expect(aiApi.deleteConversation).not.toHaveBeenCalled()
    expect(document.activeElement).toBe(trigger.element)
  })

  it('取消原生弹窗保留对话并恢复焦点', async () => {
    const wrapper = await mountAssistant()
    const trigger = deleteButton(wrapper, first)
    await trigger.trigger('click')
    await wrapper.get('dialog[open]').trigger('cancel')

    expect(wrapper.find('dialog[open]').exists()).toBe(false)
    expect(aiApi.deleteConversation).not.toHaveBeenCalled()
    expect(wrapper.get('.messages').text()).toContain(`${first.title}的回答`)
    expect(document.activeElement).toBe(trigger.element)
  })

  it('删除未选中的对话保留当前消息和输入草稿', async () => {
    const wrapper = await mountAssistant()
    await wrapper.get('textarea').setValue('这是一条未发送的追问')
    await deleteButton(wrapper, second).trigger('click')
    await dialogButton(wrapper, '删除对话').trigger('click')
    await flushPromises()

    expect(aiApi.deleteConversation).toHaveBeenCalledExactlyOnceWith(second.id)
    expect(wrapper.findAll('.conversation')).toHaveLength(1)
    expect(wrapper.get('.conversation.active').text()).toContain(first.title)
    expect(wrapper.get('.messages').text()).toContain(`${first.title}的回答`)
    expect(wrapper.get<HTMLTextAreaElement>('textarea').element.value).toBe('这是一条未发送的追问')
    expect(wrapper.find('dialog[open]').exists()).toBe(false)
    expect(wrapper.text()).toContain('对话已删除')
    expect(aiApi.getConversationMessagesPage).toHaveBeenCalledTimes(1)
    expect(aiApi.createConversation).not.toHaveBeenCalled()
  })

  it('删除当前对话后展示空聊天，不自动打开其他历史或创建对话', async () => {
    const wrapper = await mountAssistant()
    await deleteButton(wrapper, first).trigger('click')
    await dialogButton(wrapper, '删除对话').trigger('click')
    await flushPromises()

    expect(aiApi.deleteConversation).toHaveBeenCalledExactlyOnceWith(first.id)
    expect(wrapper.findAll('.conversation')).toHaveLength(1)
    expect(wrapper.get('.conversation').text()).toContain(second.title)
    expect(wrapper.find('.conversation.active').exists()).toBe(false)
    expect(wrapper.findAll('.message')).toHaveLength(0)
    expect(wrapper.get('.assistant-empty').text()).toContain('想从哪一段生活问起？')
    expect(aiApi.getConversationMessagesPage).toHaveBeenCalledTimes(1)
    expect(aiApi.createConversation).not.toHaveBeenCalled()
    expect(document.activeElement).toBe(wrapper.get('.new-chat').element)
  })

  it('删除失败显示可重试的错误，保留确认框、历史、消息和草稿', async () => {
    vi.mocked(aiApi.deleteConversation).mockRejectedValueOnce(new Error('HTTP 503'))
    const wrapper = await mountAssistant()
    await wrapper.get('textarea').setValue('保留这条草稿')
    await deleteButton(wrapper, first).trigger('click')
    await dialogButton(wrapper, '删除对话').trigger('click')
    await flushPromises()

    expect(wrapper.get('dialog[open] [role="alert"]').text()).toContain('请稍后重试')
    expect(wrapper.findAll('.conversation')).toHaveLength(2)
    expect(wrapper.get('.messages').text()).toContain(`${first.title}的回答`)
    expect(wrapper.get<HTMLTextAreaElement>('textarea').element.value).toBe('保留这条草稿')
    expect(dialogButton(wrapper, '删除对话').element.disabled).toBe(false)
    expect(dialogButton(wrapper, '取消').element.disabled).toBe(false)

    await dialogButton(wrapper, '删除对话').trigger('click')
    await flushPromises()
    expect(aiApi.deleteConversation).toHaveBeenCalledTimes(2)
    expect(wrapper.find('dialog[open]').exists()).toBe(false)
    expect(wrapper.findAll('.conversation')).toHaveLength(1)
  })

  it('删除请求未完成时禁用冲突操作并阻止重复提交和取消', async () => {
    const deletion = deferred<void>()
    vi.mocked(aiApi.deleteConversation).mockReturnValueOnce(deletion.promise)
    const wrapper = await mountAssistant()
    await deleteButton(wrapper, second).trigger('click')
    const confirm = dialogButton(wrapper, '删除对话')
    await confirm.trigger('click')

    expect(confirm.text()).toBe('正在删除…')
    expect(confirm.element.disabled).toBe(true)
    expect(dialogButton(wrapper, '取消').element.disabled).toBe(true)
    expect(wrapper.get<HTMLButtonElement>('.new-chat').element.disabled).toBe(true)
    for (const button of wrapper.findAll<HTMLButtonElement>(
      '.conversation-open, .conversation-delete',
    ))
      expect(button.element.disabled).toBe(true)
    confirm.element.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await wrapper.get('dialog[open]').trigger('cancel')
    expect(aiApi.deleteConversation).toHaveBeenCalledExactlyOnceWith(second.id)
    expect(wrapper.find('dialog[open]').exists()).toBe(true)
    expect(wrapper.findAll('.conversation')).toHaveLength(2)

    deletion.resolve(undefined)
    await flushPromises()
    expect(wrapper.find('dialog[open]').exists()).toBe(false)
    expect(wrapper.findAll('.conversation')).toHaveLength(1)
  })

  it('发送期间禁止切换、新建和删除对话', async () => {
    const response = deferred<void>()
    vi.mocked(aiApi.sendMessage).mockReturnValueOnce(response.promise)
    const wrapper = await mountAssistant()
    await wrapper.get('textarea').setValue('继续总结')
    await wrapper.get('.composer').trigger('submit')
    await flushPromises()

    expect(aiApi.sendMessage).toHaveBeenCalledWith(first.id, '继续总结', expect.any(Function))
    for (const button of wrapper.findAll<HTMLButtonElement>(
      '.new-chat, .conversation-open, .conversation-delete',
    )) {
      expect(button.element.disabled).toBe(true)
      button.element.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    }
    await flushPromises()
    expect(aiApi.getConversationMessagesPage).toHaveBeenCalledTimes(1)
    expect(aiApi.createConversation).not.toHaveBeenCalled()
    expect(aiApi.deleteConversation).not.toHaveBeenCalled()
    expect(wrapper.find('dialog[open]').exists()).toBe(false)

    response.resolve(undefined)
    await flushPromises()
    expect(wrapper.get<HTMLButtonElement>('.new-chat').element.disabled).toBe(false)
  })

  it('统计失败展示服务端的文字提示，消息正文仍不显示名称', async () => {
    const message = '暂时没能完成消费或记录统计，请稍后重试。'
    vi.mocked(aiApi.sendMessage).mockImplementationOnce(async (_id, _content, onEvent) => {
      onEvent({ type: 'delta', data: { text: '正在统计。' } })
      onEvent({
        type: 'error',
        data: { message },
      })
    })
    const wrapper = await mountAssistant()
    await wrapper.get('textarea').setValue('统计所有消费金额')
    await wrapper.get('.composer').trigger('submit')
    await flushPromises()

    expect(wrapper.get('.error-banner[role="alert"]').text()).toBe(message)
    expect(wrapper.findAll('article.message.user').slice(-1)[0]!.text()).toBe('统计所有消费金额')
    expect(wrapper.findAll('article.message.assistant').slice(-1)[0]!.text()).toBe('正在统计。')
    expect(wrapper.find('.message-body > strong').exists()).toBe(false)
    expect(wrapper.get<HTMLButtonElement>('.new-chat').element.disabled).toBe(false)
  })

  it('连续切换时较晚返回的旧请求不会覆盖当前对话', async () => {
    const wrapper = await mountAssistant()
    const oldRequest = deferred<ConversationMessagePage>()
    const latestRequest = deferred<ConversationMessagePage>()
    vi.mocked(aiApi.getConversationMessagesPage).mockImplementation((id) =>
      id === first.id ? oldRequest.promise : latestRequest.promise,
    )
    const buttons = wrapper.findAll<HTMLButtonElement>('.conversation-open')
    // Deliver both clicks before the disabled state is rendered to exercise the race.
    buttons[0]!.element.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    buttons[1]!.element.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    latestRequest.resolve(messagePage(second))
    await flushPromises()
    expect(wrapper.get('.conversation.active').text()).toContain(second.title)
    expect(wrapper.get('.messages').text()).toContain(`${second.title}的回答`)

    oldRequest.resolve(messagePage(first))
    await flushPromises()
    expect(wrapper.get('.conversation.active').text()).toContain(second.title)
    expect(wrapper.get('.messages').text()).toContain(`${second.title}的回答`)
    expect(wrapper.get('.messages').text()).not.toContain(`${first.title}的回答`)
  })

  it('新建失败显示错误并保留当前聊天和草稿', async () => {
    vi.mocked(aiApi.createConversation).mockRejectedValueOnce(new Error('HTTP 503'))
    const wrapper = await mountAssistant()
    await wrapper.get('textarea').setValue('新建前的草稿')
    await wrapper.get('.new-chat').trigger('click')
    await flushPromises()

    expect(wrapper.get('.error-banner[role="alert"]').text()).toContain('暂时无法新建对话')
    expect(wrapper.findAll('.conversation')).toHaveLength(2)
    expect(wrapper.get('.messages').text()).toContain(`${first.title}的回答`)
    expect(wrapper.get<HTMLTextAreaElement>('textarea').element.value).toBe('新建前的草稿')
    expect(wrapper.get<HTMLButtonElement>('.new-chat').element.disabled).toBe(false)
  })

  it('新建请求未完成时阻止重复新建及历史操作，完成后进入新对话', async () => {
    const creation = deferred<ConversationSummary>()
    vi.mocked(aiApi.createConversation).mockReturnValueOnce(creation.promise)
    const wrapper = await mountAssistant()
    const newChat = wrapper.get<HTMLButtonElement>('.new-chat')
    await newChat.trigger('click')

    expect(newChat.text()).toContain('正在新建')
    for (const button of wrapper.findAll<HTMLButtonElement>(
      '.new-chat, .conversation-open, .conversation-delete',
    )) {
      expect(button.element.disabled).toBe(true)
      button.element.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    }
    expect(aiApi.createConversation).toHaveBeenCalledTimes(1)
    expect(aiApi.getConversationMessagesPage).toHaveBeenCalledTimes(1)
    expect(wrapper.find('dialog[open]').exists()).toBe(false)

    creation.resolve({ ...first, id: '33333333-3333-4333-8333-333333333333', title: '新的对话' })
    await flushPromises()
    expect(wrapper.findAll('.conversation')).toHaveLength(3)
    expect(wrapper.get('.conversation.active').text()).toContain('新的对话')
    expect(wrapper.find('.assistant-empty').exists()).toBe(true)
    expect(newChat.element.disabled).toBe(false)
    expect(document.activeElement).toBe(wrapper.get('textarea').element)
  })
})
