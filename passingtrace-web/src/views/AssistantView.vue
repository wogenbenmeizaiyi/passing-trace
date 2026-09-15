<script setup lang="ts">
import {
  computed,
  nextTick,
  onActivated,
  onDeactivated,
  onMounted,
  onUnmounted,
  reactive,
  ref,
  watch,
} from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate, useRoute, useRouter } from 'vue-router'
import {
  aiApi,
  type AssistantAction,
  type ConversationDetail,
  type ConversationSummary,
  type EvidenceBundle,
  type UserMemory,
} from '@/api/ai'
import AmapActionCards from '@/components/AmapActionCards.vue'
import BrandMark from '@/components/BrandMark.vue'
import AssistantMessageContent from '@/components/AssistantMessageContent'
import EvidenceDisclosure from '@/components/EvidenceDisclosure.vue'
import WebAppHeader from '@/components/WebAppHeader.vue'
import { useAuthStore } from '@/stores/auth'
import { conversationIdFromQuery } from '@/utils/assistant-navigation'

defineOptions({ name: 'AssistantView' })

interface ChatItem {
  id?: number
  role: 'User' | 'Assistant'
  content: string
  pending?: boolean
  evidence?: EvidenceBundle | null
  actions?: AssistantAction[]
}

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const conversations = ref<ConversationSummary[]>([])
const currentId = ref<string | null>(null)
const messages = ref<ChatItem[]>([])
const memories = ref<UserMemory[]>([])
const question = ref('')
const busy = ref(false)
const loading = ref(false)
const historyCursor = ref<string | null>(null)
const loadingMoreHistory = ref(false)
const historyError = ref('')
const earlierMessageId = ref<number | null>(null)
const loadingEarlier = ref(false)
const earlierError = ref('')
const error = ref<string | null>(null)
const showMemories = ref(false)
const showHistory = ref(false)
const creating = ref(false)
const openingId = ref<string | null>(null)
const deleting = ref(false)
const deleteTarget = ref<ConversationSummary | null>(null)
const deleteError = ref<string | null>(null)
const notice = ref('')
const deleteDialog = ref<HTMLDialogElement | null>(null)
const newChatButton = ref<HTMLButtonElement | null>(null)
const messagesElement = ref<HTMLElement | null>(null)
const historyElement = ref<HTMLElement | null>(null)
const composerInput = ref<HTMLTextAreaElement | null>(null)
const interactionLocked = computed(
  () =>
    busy.value ||
    loading.value ||
    creating.value ||
    openingId.value !== null ||
    deleting.value ||
    loadingEarlier.value ||
    loadingMoreHistory.value,
)
let deleteTrigger: HTMLElement | null = null
let openRequest = 0
let historyRequest = 0
let followLatest = true
let viewActive = true
let savedView: {
  id: string | null
  messagesTop: number
  historyTop: number
  focus: HTMLElement | null
} | null = null
const suggestions = ['帮我总结这个月的生活', '我最近去过哪些地方？', '最近有哪些安排还没有完成？']
const historyGroups = computed(() => {
  const groups = new Map<string, { label: string; items: ConversationSummary[] }>()
  for (const conversation of conversations.value) {
    const date = new Date(conversation.updatedAt)
    const label = Number.isNaN(date.getTime())
      ? '更早的对话'
      : `${date.getFullYear()} 年 ${date.getMonth() + 1} 月`
    const group = groups.get(label) ?? { label, items: [] }
    group.items.push(conversation)
    groups.set(label, group)
  }
  return [...groups.values()]
})

async function load() {
  if (loading.value) return
  loading.value = true
  if (route.query.conversation === undefined || conversationIdFromQuery(route.query.conversation))
    error.value = null
  const request = ++historyRequest
  try {
    const page = await aiApi.listConversationsPage()
    if (request !== historyRequest) return
    conversations.value = page.items
    historyCursor.value = page.nextCursor
    const loadedMemories = await aiApi.listMemories()
    if (request === historyRequest) memories.value = loadedMemories
  } catch {
    if (request === historyRequest) error.value = '聊天信息暂时无法加载，请刷新后重试。'
  } finally {
    if (request === historyRequest) loading.value = false
  }
}

async function loadMoreHistory() {
  if (!historyCursor.value || interactionLocked.value) return
  const request = ++historyRequest
  loadingMoreHistory.value = true
  historyError.value = ''
  try {
    const page = await aiApi.listConversationsPage(historyCursor.value)
    if (request !== historyRequest) return
    const known = new Set(conversations.value.map((item) => item.id))
    conversations.value.push(...page.items.filter((item) => !known.has(item.id)))
    historyCursor.value = page.nextCursor
  } catch {
    if (request === historyRequest) historyError.value = '更早的对话暂时无法加载，请重试。'
  } finally {
    if (request === historyRequest) loadingMoreHistory.value = false
  }
}

function mapMessage(item: ConversationDetail['messages'][number]): ChatItem {
  return {
    id: item.id,
    role: item.role === 'User' ? 'User' : 'Assistant',
    content: item.content,
    evidence: item.evidence,
    actions: item.evidence?.actions ?? [],
  }
}

async function loadEarlierMessages() {
  const id = currentId.value
  const beforeId = earlierMessageId.value
  if (!id || !beforeId || interactionLocked.value) return
  const request = openRequest
  loadingEarlier.value = true
  earlierError.value = ''
  const element = messagesElement.value
  const height = element?.scrollHeight ?? 0
  const top = element?.scrollTop ?? 0
  try {
    const page = await aiApi.getConversationMessagesPage(id, beforeId)
    if (request !== openRequest || currentId.value !== id) return
    const known = new Set(messages.value.map((item) => item.id))
    messages.value.unshift(...page.items.filter((item) => !known.has(item.id)).map(mapMessage))
    earlierMessageId.value = page.hasMore ? page.nextBeforeId : null
    followLatest = false
    await nextTick()
    if (element) element.scrollTop = top + element.scrollHeight - height
  } catch {
    if (request === openRequest) earlierError.value = '更早的消息暂时无法加载，请重试。'
  } finally {
    if (request === openRequest) loadingEarlier.value = false
  }
}

async function startConversation() {
  const value = await aiApi.createConversation()
  conversations.value.unshift(value)
  currentId.value = value.id
  messages.value = []
  earlierMessageId.value = null
  earlierError.value = ''
  ++openRequest
  followLatest = true
  await syncConversationUrl(value.id)
  return value.id
}

async function syncConversationUrl(id: string | null) {
  // An asynchronous create can finish after the user has left this workspace.
  if (route.path !== '/assistant') return
  const query = { ...route.query }
  if (id) query.conversation = id
  else delete query.conversation
  await router.replace({ path: '/assistant', query })
}

async function create() {
  if (interactionLocked.value) return
  creating.value = true
  error.value = null
  notice.value = ''
  try {
    await startConversation()
    showHistory.value = false
    await nextTick()
    composerInput.value?.focus()
  } catch {
    error.value = '暂时无法新建对话，请稍后重试。'
  } finally {
    creating.value = false
  }
}

async function open(id: string, syncUrl = true) {
  if (
    busy.value ||
    creating.value ||
    deleting.value ||
    loadingEarlier.value ||
    loadingMoreHistory.value
  )
    return
  const request = ++openRequest
  openingId.value = id
  error.value = null
  notice.value = ''
  try {
    const value = await aiApi.getConversationMessagesPage(id)
    if (request !== openRequest) return
    currentId.value = id
    messages.value = value.items.map(mapMessage)
    earlierMessageId.value = value.hasMore ? value.nextBeforeId : null
    earlierError.value = ''
    followLatest = true
    showHistory.value = false
    if (syncUrl) await syncConversationUrl(id)
    await scrollMessages()
  } catch {
    if (request === openRequest) error.value = '这段对话暂时打不开，请重试。'
  } finally {
    if (request === openRequest) openingId.value = null
  }
}

async function requestDelete(conversation: ConversationSummary, event: Event) {
  if (interactionLocked.value) return
  deleteTrigger = event.currentTarget as HTMLElement
  deleteTarget.value = conversation
  deleteError.value = null
  await nextTick()
  deleteDialog.value?.showModal()
}

function cancelDelete() {
  if (deleting.value) return
  deleteDialog.value?.close()
  deleteTarget.value = null
  deleteError.value = null
  if (deleteTrigger?.isConnected) deleteTrigger.focus()
  else newChatButton.value?.focus()
}

async function confirmDelete() {
  const target = deleteTarget.value
  if (!target || interactionLocked.value) return
  deleting.value = true
  deleteError.value = null
  try {
    await aiApi.deleteConversation(target.id)
    conversations.value = conversations.value.filter((item) => item.id !== target.id)
    if (currentId.value === target.id) {
      ++openRequest
      currentId.value = null
      messages.value = []
      earlierMessageId.value = null
      earlierError.value = ''
      error.value = null
      await syncConversationUrl(null)
    }
    notice.value = '对话已删除，你的记录和故事线不受影响。'
    deleting.value = false
    await nextTick()
    cancelDelete()
  } catch {
    deleteError.value = '没能删除这段对话，请稍后重试。'
  } finally {
    deleting.value = false
  }
}

function trackMessageScroll() {
  const element = messagesElement.value
  if (element) followLatest = element.scrollHeight - element.scrollTop - element.clientHeight < 96
}

async function scrollMessages() {
  await nextTick()
  if (viewActive && followLatest && messagesElement.value)
    messagesElement.value.scrollTop = messagesElement.value.scrollHeight
}

async function send(textOverride?: string) {
  const text = (textOverride ?? question.value).trim()
  if (!text || interactionLocked.value) return
  const requestedId = conversationIdFromQuery(route.query.conversation)
  if (route.query.conversation !== undefined && (!requestedId || requestedId !== currentId.value)) {
    error.value = '请先从历史记录中重新打开对话，或点击“新对话”再发送。'
    return
  }
  busy.value = true
  error.value = null
  notice.value = ''
  let answer: ChatItem | undefined
  try {
    if (!currentId.value) await startConversation()
    messages.value.push({ role: 'User', content: text })
    answer = reactive<ChatItem>({ role: 'Assistant', content: '', pending: true })
    messages.value.push(answer)
    const currentAnswer = answer
    question.value = ''
    followLatest = true
    if (composerInput.value) composerInput.value.style.height = 'auto'
    await scrollMessages()
    await aiApi.sendMessage(currentId.value!, text, (event) => {
      if (event.type === 'delta') {
        currentAnswer.content = event.data.replacement
          ? event.data.text
          : currentAnswer.content + event.data.text
      } else if (event.type === 'evidence') {
        currentAnswer.evidence = event.data
        currentAnswer.actions = event.data.actions ?? currentAnswer.actions
      } else if (event.type === 'action') {
        currentAnswer.actions ??= []
        if (
          !currentAnswer.actions.some(
            (action) => action.type === event.data.type && action.label === event.data.label,
          )
        )
          currentAnswer.actions.push(event.data)
      } else if (event.type === 'error') {
        error.value = event.data.message
        currentAnswer.pending = false
      }
      void scrollMessages()
    })
    try {
      const summary = await aiApi.getConversationSummary(currentId.value!)
      conversations.value = [
        summary,
        ...conversations.value.filter((item) => item.id !== summary.id),
      ]
    } catch {
      // A title refresh must not turn a successfully received answer into a send error.
    }
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '发送失败。'
  } finally {
    if (answer) answer.pending = false
    busy.value = false
  }
}

async function confirmMemory(memory: UserMemory) {
  await aiApi.updateMemory(memory.id, { status: 'Confirmed' })
  memories.value = await aiApi.listMemories()
}

async function editMemory(memory: UserMemory) {
  const content = window.prompt('修正这条记忆', memory.content)?.trim()
  if (!content || content === memory.content) return
  await aiApi.updateMemory(memory.id, { content, status: 'Corrected' })
  memories.value = await aiApi.listMemories()
}

async function forgetMemory(memory: UserMemory) {
  await aiApi.deleteMemory(memory.id)
  memories.value = await aiApi.listMemories()
}

function resizeComposer(event: Event) {
  const target = event.target as HTMLTextAreaElement
  target.style.height = 'auto'
  target.style.height = `${Math.min(target.scrollHeight, 160)}px`
}

onMounted(() => {
  if (auth.isAuthenticated) void load()
})
onBeforeRouteLeave(() => {
  savedView = {
    id: currentId.value,
    messagesTop: messagesElement.value?.scrollTop ?? 0,
    historyTop: historyElement.value?.scrollTop ?? 0,
    focus: document.activeElement instanceof HTMLElement ? document.activeElement : null,
  }
  viewActive = false
})
onDeactivated(() => {
  viewActive = false
})
onActivated(async () => {
  viewActive = true
  const saved = savedView
  if (!saved || saved.id !== (conversationIdFromQuery(route.query.conversation) ?? currentId.value))
    return
  await nextTick()
  if (messagesElement.value) messagesElement.value.scrollTop = saved.messagesTop
  if (historyElement.value) historyElement.value.scrollTop = saved.historyTop
  if (saved.focus?.isConnected) saved.focus.focus({ preventScroll: true })
})
onBeforeRouteUpdate((to) => {
  if (
    (busy.value || creating.value || deleting.value) &&
    conversationIdFromQuery(to.query.conversation) !== currentId.value
  )
    return false
})
onUnmounted(() => {
  ++openRequest
  ++historyRequest
})
watch(
  () => auth.isAuthenticated,
  (authenticated) => {
    if (authenticated && conversations.value.length === 0) void load()
  },
)
watch(
  [() => auth.isAuthenticated, () => route.path, () => route.query.conversation],
  ([authenticated]) => {
    // Cached views still observe the router while a record is on screen.
    if (!authenticated || route.path !== '/assistant') return
    const id = conversationIdFromQuery(route.query.conversation)
    if (route.query.conversation !== undefined && !id) {
      error.value = '对话链接无效，请从左侧历史记录中选择对话。'
      return
    }
    if (id && id !== currentId.value && id !== openingId.value) void open(id, false)
    else if (!id && currentId.value) void syncConversationUrl(currentId.value)
  },
  { immediate: true },
)
</script>

<template>
  <div class="app-shell assistant-shell">
    <WebAppHeader />

    <main v-if="auth.isAuthenticated" class="assistant-page" @keydown.esc="showHistory = false">
      <button
        v-if="showHistory"
        class="history-scrim"
        aria-label="关闭对话历史"
        @click="showHistory = false"
      ></button>
      <aside
        id="conversation-history"
        class="conversation-panel"
        :class="{ 'is-open': showHistory }"
        aria-label="对话历史"
      >
        <div class="conversation-panel__heading">
          <div>
            <p class="eyebrow">CONVERSATIONS</p>
            <h2>对话</h2>
          </div>
          <button class="history-close" aria-label="收起对话历史" @click="showHistory = false">
            <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
              <path d="m6 6 12 12M18 6 6 18" />
            </svg>
          </button>
        </div>
        <button
          ref="newChatButton"
          class="button button-primary new-chat"
          :disabled="interactionLocked"
          @click="create"
        >
          <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
            <path d="M12 5v14M5 12h14" />
          </svg>
          {{ creating ? '正在新建…' : '新对话' }}
        </button>
        <div
          ref="historyElement"
          class="conversation-list"
          tabindex="0"
          role="region"
          aria-label="会话列表"
        >
          <section
            v-for="group in historyGroups"
            :key="group.label"
            class="history-month"
            :aria-label="group.label"
          >
            <h3>{{ group.label }}</h3>
            <div
              v-for="conversation in group.items"
              :key="conversation.id"
              class="conversation"
              :class="{ active: currentId === conversation.id }"
            >
              <button
                class="conversation-open"
                :title="conversation.title"
                :aria-pressed="currentId === conversation.id"
                :disabled="interactionLocked"
                @click="open(conversation.id)"
              >
                <span>{{ conversation.title }}</span>
              </button>
              <button
                class="conversation-delete"
                :aria-label="`删除对话：${conversation.title}`"
                title="删除对话"
                :disabled="interactionLocked"
                @click="requestDelete(conversation, $event)"
              >
                <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
                  <path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 11v5M14 11v5" />
                </svg>
              </button>
            </div>
          </section>
          <p v-if="loading" class="conversation-empty" role="status">正在加载对话…</p>
          <p v-if="historyError" class="history-load-error" role="alert">{{ historyError }}</p>
          <button
            v-if="historyCursor"
            class="history-load-more"
            :disabled="loadingMoreHistory || loading"
            @click="loadMoreHistory"
          >
            {{ loadingMoreHistory ? '正在加载…' : '加载更早对话' }}
          </button>
          <p v-if="!loading && conversations.length === 0" class="conversation-empty">
            还没有历史对话。
          </p>
        </div>
      </aside>

      <section class="chat" aria-labelledby="assistant-title">
        <header class="chat-heading">
          <div>
            <p class="eyebrow">PRIVATE RECORD ASSISTANT</p>
            <h1 id="assistant-title">问问你的记录</h1>
            <p>回答只使用你的记录、结构化统计与有证据的记忆。</p>
          </div>
          <div class="chat-tools">
            <button
              class="history-toggle memory-button"
              :aria-expanded="showHistory"
              aria-controls="conversation-history"
              @click="showHistory = true"
            >
              <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
                <path d="M4 5h16v12H9l-5 4V5ZM8 9h8M8 13h5" />
              </svg>
              历史对话
            </button>
            <button
              class="memory-button"
              :aria-expanded="showMemories"
              @click="showMemories = true"
            >
              <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
                <path d="M5 5h11a3 3 0 0 1 3 3v11H8a3 3 0 0 1-3-3V5Z" />
                <path d="M8 19a3 3 0 0 1 0-6h11M9 8h6" />
              </svg>
              我的记忆 <span>{{ memories.length }}</span>
            </button>
          </div>
        </header>

        <p v-if="openingId" class="chat-feedback" role="status">正在读取对话…</p>
        <div
          ref="messagesElement"
          class="messages"
          tabindex="0"
          role="region"
          aria-label="聊天内容"
          aria-live="polite"
          @scroll="trackMessageScroll"
        >
          <div v-if="earlierMessageId" class="earlier-messages">
            <button :disabled="interactionLocked" @click="loadEarlierMessages">
              {{ loadingEarlier ? '正在加载…' : '查看更早消息' }}
            </button>
            <p v-if="earlierError" role="alert">{{ earlierError }}</p>
          </div>
          <section v-if="messages.length === 0" class="assistant-empty">
            <span class="assistant-empty__mark"><BrandMark /></span>
            <h2>想从哪一段生活问起？</h2>
            <p>我会先检索你的记录，再用可点击的记录标题说明依据。</p>
            <div class="suggestions">
              <button
                v-for="suggestion in suggestions"
                :key="suggestion"
                :disabled="interactionLocked"
                @click="send(suggestion)"
              >
                {{ suggestion }}
                <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
                  <path d="m9 5 7 7-7 7" />
                </svg>
              </button>
            </div>
          </section>

          <article
            v-for="(message, index) in messages"
            v-show="message.content || message.pending"
            :key="message.id ?? `live-${index}`"
            class="message"
            :class="message.role.toLowerCase()"
            :aria-label="message.role === 'User' ? '你的消息' : 'AI 回复'"
          >
            <div class="message-body">
              <AssistantMessageContent
                :content="message.content || (message.pending ? '正在检索你的记录…' : '')"
                :records="message.evidence?.records ?? []"
                :storylines="message.evidence?.storylines ?? []"
                :conversation-id="currentId"
              />
              <EvidenceDisclosure
                v-if="message.evidence?.records?.length"
                :records="message.evidence.records"
                :conversation-id="currentId"
              />
              <AmapActionCards
                :places="message.evidence?.amapPlaces ?? []"
                :actions="message.actions ?? message.evidence?.actions ?? []"
              />
            </div>
          </article>
        </div>

        <p v-if="notice" class="chat-feedback" role="status">{{ notice }}</p>
        <p v-if="error" class="error-banner" role="alert">{{ error }}</p>
        <form class="composer" @submit.prevent="send()">
          <label class="sr-only" for="assistant-question">询问你的记录</label>
          <textarea
            id="assistant-question"
            ref="composerInput"
            v-model="question"
            rows="1"
            maxlength="8000"
            placeholder="询问你的经历、安排、花费或趋势…"
            @input="resizeComposer"
            @keydown.enter.exact.prevent="send()"
          />
          <button
            class="composer-send"
            :disabled="interactionLocked || !question.trim()"
            aria-label="发送问题"
          >
            <svg v-if="!busy" class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
              <path d="m12 19V5m0 0-5 5m5-5 5 5" />
            </svg>
            <span v-else class="send-spinner" aria-hidden="true"></span>
          </button>
        </form>
      </section>
    </main>

    <main v-else class="assistant-login">
      <span><BrandMark /></span>
      <h1>登录后问问你的记录</h1>
      <p>AI 只会在当前登录用户的数据中检索。</p>
      <button class="button button-primary" @click="auth.login(route.fullPath)">扫码登录</button>
    </main>

    <dialog
      ref="deleteDialog"
      class="delete-dialog"
      aria-labelledby="delete-dialog-title"
      aria-describedby="delete-dialog-description"
      @cancel.prevent="cancelDelete"
    >
      <h2 id="delete-dialog-title">删除这段对话？</h2>
      <p class="delete-dialog__title">{{ deleteTarget?.title }}</p>
      <p id="delete-dialog-description">
        这段对话将从聊天历史中移除，无法在这里恢复。你的记录、计划、故事线和长期记忆不会被删除。
      </p>
      <p v-if="deleteError" class="delete-dialog__error" role="alert">{{ deleteError }}</p>
      <div class="delete-dialog__actions">
        <button
          class="button button-secondary"
          :disabled="deleting"
          autofocus
          @click="cancelDelete"
        >
          取消
        </button>
        <button class="button delete-dialog__confirm" :disabled="deleting" @click="confirmDelete">
          {{ deleting ? '正在删除…' : '删除对话' }}
        </button>
      </div>
    </dialog>

    <template v-if="showMemories">
      <button class="memory-scrim" aria-label="关闭我的记忆" @click="showMemories = false"></button>
      <aside class="memory-panel" aria-label="我的长期记忆">
        <header>
          <div>
            <p class="eyebrow">MEMORY</p>
            <h2>我的长期记忆</h2>
          </div>
          <button aria-label="关闭" @click="showMemories = false">
            <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
              <path d="m6 6 12 12M18 6 6 18" />
            </svg>
          </button>
        </header>
        <p>自动记忆都有记录证据，你可以确认、修正或遗忘。</p>
        <div class="memory-list">
          <article v-for="memory in memories" :key="memory.id">
            <span>{{ memory.type }} · {{ memory.status }}</span>
            <p>{{ memory.content }}</p>
            <small
              >置信度 {{ Math.round(memory.confidence * 100) }}% ·
              {{ memory.evidenceEventIds.length }} 条证据</small
            >
            <div>
              <button @click="confirmMemory(memory)">确认</button
              ><button @click="editMemory(memory)">修正</button
              ><button class="danger" @click="forgetMemory(memory)">遗忘</button>
            </div>
          </article>
          <p v-if="memories.length === 0" class="memory-empty">还没有形成长期记忆。</p>
        </div>
      </aside>
    </template>
  </div>
</template>

<style scoped>
.assistant-shell {
  height: 100dvh;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.assistant-shell :deep(.site-header) {
  flex: 0 0 auto;
}
.assistant-page {
  width: 100%;
  min-height: 0;
  flex: 1;
  position: relative;
  display: grid;
  grid-template-columns: clamp(17rem, 19vw, 22rem) minmax(0, 1fr);
  grid-template-rows: minmax(0, 1fr);
  overflow: hidden;
  background: var(--surface);
}
.conversation-panel {
  min-width: 0;
  min-height: 0;
  padding: 34px 18px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border-right: 1px solid var(--line);
  background: var(--surface-soft);
}
.conversation-panel__heading {
  padding: 0 8px 22px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex: 0 0 auto;
}
.conversation-panel h2 {
  margin: 0;
  font-size: 24px;
  letter-spacing: -0.035em;
}
.new-chat {
  width: 100%;
  flex: 0 0 auto;
}
.new-chat .ui-icon {
  width: 18px;
  height: 18px;
}
.conversation-list {
  margin-top: 22px;
  min-height: 0;
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 5px;
}
.conversation-list,
.messages {
  overflow-y: auto;
  overscroll-behavior-y: contain;
  scrollbar-gutter: stable;
  scrollbar-width: thin;
  scrollbar-color: var(--line-strong) transparent;
}
.history-month {
  flex: 0 0 auto;
  display: grid;
  gap: 4px;
}
.history-month h3 {
  margin: 12px 12px 6px;
  color: var(--ink-tertiary);
  font-size: 12px;
  font-weight: 500;
}
.history-load-more,
.earlier-messages button {
  min-height: 44px;
  padding: 8px 12px;
  flex: 0 0 auto;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  color: var(--primary-strong);
  background: var(--surface-soft);
  font-size: 13px;
}
.history-load-more {
  margin-top: 12px;
}
.history-load-more:focus-visible,
.earlier-messages button:focus-visible {
  outline: 2px solid var(--primary);
  outline-offset: 2px;
}
.history-load-error,
.earlier-messages p {
  margin: 8px 0;
  color: var(--danger);
  font-size: 13px;
}
.earlier-messages {
  text-align: center;
}
.conversation {
  width: 100%;
  min-height: 48px;
  flex: 0 0 auto;
  display: grid;
  grid-template-columns: minmax(0, 1fr) 44px;
  align-items: center;
  border: 1px solid transparent;
  border-radius: var(--radius-md);
  color: var(--ink-secondary);
  background: transparent;
}
.conversation-open {
  min-width: 0;
  min-height: 48px;
  padding: 10px 8px 10px 12px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 3px;
  border: 0;
  border-radius: var(--radius-md);
  color: inherit;
  background: transparent;
  text-align: left;
}
.conversation:hover {
  background: var(--surface);
}
.conversation.active {
  border-color: color-mix(in srgb, var(--primary) 22%, transparent);
  color: var(--primary-strong);
  background: var(--primary-soft);
}
.conversation span {
  display: -webkit-box;
  overflow: hidden;
  font-size: 13px;
  font-weight: 700;
  line-height: 1.45;
  overflow-wrap: anywhere;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}
.conversation-delete,
.history-close {
  width: 44px;
  height: 44px;
  display: grid;
  place-items: center;
  border: 0;
  border-radius: var(--radius-sm);
  color: var(--ink-secondary);
  background: transparent;
}
.conversation-delete .ui-icon {
  width: 18px;
  height: 18px;
}
.conversation-delete:hover:not(:disabled) {
  color: var(--danger);
  background: color-mix(in srgb, var(--danger) 10%, var(--surface));
}
.chat-tools {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.history-toggle.memory-button,
.history-close,
.history-scrim {
  display: none;
}
.conversation-empty {
  padding: 24px 10px;
  color: var(--ink-tertiary);
  font-size: 11px;
  text-align: center;
}
.chat {
  min-width: 0;
  min-height: 0;
  padding: 32px var(--workspace-gutter) 24px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: var(--surface);
}
.chat > * {
  width: 100%;
}
.chat-heading {
  flex: 0 0 auto;
  padding-bottom: 24px;
  display: flex;
  justify-content: space-between;
  gap: 24px;
  border-bottom: 1px solid var(--line);
}
.chat-heading h1 {
  margin: 0;
  font-size: clamp(30px, 4vw, 42px);
  line-height: 1.15;
  letter-spacing: -0.05em;
}
.chat-heading div > p:last-child {
  margin: 9px 0 0;
  color: var(--ink-secondary);
  font-size: 12px;
}
.memory-button {
  min-height: 44px;
  padding: 0 12px;
  align-self: flex-start;
  display: flex;
  align-items: center;
  gap: 7px;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  color: var(--primary-strong);
  background: var(--surface-soft);
  white-space: nowrap;
}
.memory-button .ui-icon {
  width: 18px;
  height: 18px;
}
.memory-button span {
  min-width: 21px;
  padding: 2px 6px;
  border-radius: 999px;
  color: var(--on-primary);
  background: var(--primary);
  font-size: 10px;
  text-align: center;
}
.messages {
  flex: 1;
  min-height: 0;
  padding: 32px 0 20px;
  display: flex;
  flex-direction: column;
  gap: 20px;
}
.messages > * {
  flex-shrink: 0;
}
.chat-feedback {
  margin: 0;
  padding: 8px 0;
  flex: 0 0 auto;
  color: var(--ink-secondary);
  font-size: 13px;
}
.chat > .error-banner {
  max-height: 25%;
  flex: 0 0 auto;
  overflow-y: auto;
}
.assistant-empty {
  margin: auto;
  max-width: 560px;
  padding: 44px 0;
  text-align: center;
}
.assistant-empty__mark {
  width: 62px;
  height: 62px;
  margin: 0 auto 18px;
  display: block;
}
.assistant-empty h2 {
  margin: 0;
  font-size: 25px;
  letter-spacing: -0.035em;
}
.assistant-empty > p {
  margin: 10px 0 24px;
  color: var(--ink-secondary);
  font-size: 13px;
}
.suggestions {
  display: grid;
  gap: 8px;
}
.suggestions button {
  min-height: 48px;
  padding: 0 14px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  color: var(--ink-secondary);
  background: var(--surface-soft);
  text-align: left;
}
.suggestions button:hover {
  border-color: color-mix(in srgb, var(--primary) 40%, var(--line));
  color: var(--primary-strong);
  background: var(--primary-soft);
}
.suggestions .ui-icon {
  width: 17px;
  height: 17px;
}
.message {
  width: 100%;
  max-width: none;
  display: flex;
  align-items: flex-start;
  gap: 10px;
}
.message.user {
  width: auto;
  max-width: min(72%, 58rem);
  align-self: flex-end;
}
.message-body {
  min-width: 0;
  padding: 15px 17px;
  border: 1px solid var(--line);
  border-radius: 5px 18px 18px;
  background: var(--surface-soft);
}
.message.assistant .message-body {
  width: 100%;
}
.message.user .message-body {
  border-color: var(--primary);
  border-radius: 18px 18px 5px;
  color: var(--on-primary);
  background: var(--primary);
}
.message-body :deep(.assistant-markdown) {
  margin: 0;
  overflow-wrap: anywhere;
  font-size: 14px;
  line-height: 1.75;
}
.message-body :deep(.assistant-markdown > :first-child) {
  margin-top: 0;
}
.message-body :deep(.assistant-markdown > :last-child) {
  margin-bottom: 0;
}
.message-body :deep(.assistant-markdown p) {
  margin: 0.75em 0;
}
.message-body :deep(.assistant-markdown h1),
.message-body :deep(.assistant-markdown h2),
.message-body :deep(.assistant-markdown h3) {
  margin: 1.1em 0 0.45em;
  line-height: 1.4;
  letter-spacing: -0.02em;
}
.message-body :deep(.assistant-markdown h1) {
  font-size: 20px;
}
.message-body :deep(.assistant-markdown h2) {
  font-size: 18px;
}
.message-body :deep(.assistant-markdown h3) {
  font-size: 16px;
}
.message-body :deep(.assistant-markdown ul),
.message-body :deep(.assistant-markdown ol) {
  margin: 0.7em 0;
  padding-left: 1.6em;
}
.message-body :deep(.assistant-markdown li) {
  margin: 0.35em 0;
  padding-left: 0.2em;
}
.message-body :deep(.assistant-markdown li::marker) {
  color: var(--accent);
  font-weight: 700;
}
.message-body :deep(.assistant-markdown hr) {
  margin: 1.25em 0;
  border: 0;
  border-top: 1px solid var(--line-strong);
}
.message-body :deep(.assistant-markdown blockquote) {
  margin: 0.8em 0;
  padding: 0.5em 0.8em;
  border-left: 3px solid var(--accent);
  background: var(--surface);
}
.message-body :deep(.assistant-markdown code) {
  padding: 0.1em 0.35em;
  border-radius: 4px;
  background: var(--surface);
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 0.92em;
}
.message-body :deep(.record-citation),
.message-body :deep(.storyline-citation) {
  margin: 0 0.12em;
  padding: 0.08em 0.28em;
  border-bottom: 1px solid color-mix(in srgb, var(--primary) 58%, transparent);
  color: var(--primary-strong);
  background: color-mix(in srgb, var(--primary-soft) 72%, transparent);
  font-weight: 700;
  text-decoration: none;
}
.message-body :deep(.record-citation:hover),
.message-body :deep(.storyline-citation:hover) {
  color: var(--on-primary);
  background: var(--primary);
}
.message-body :deep(.record-citation:focus-visible),
.message-body :deep(.storyline-citation:focus-visible) {
  outline: 2px solid var(--primary-strong);
  outline-offset: 3px;
}
.composer {
  flex: 0 0 auto;
  min-height: 64px;
  padding: 8px;
  display: grid;
  grid-template-columns: minmax(0, 1fr) 48px;
  align-items: end;
  gap: 8px;
  border: 1px solid var(--line-strong);
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-2);
}
.composer textarea {
  min-height: 46px;
  max-height: 160px;
  padding: 12px 9px;
  resize: none;
  border: 0;
  outline: 0;
  background: transparent;
  line-height: 1.55;
}
.composer textarea:focus-visible {
  box-shadow: none;
}
.composer:focus-within {
  border-color: var(--primary);
  outline: 2px solid color-mix(in srgb, var(--primary) 40%, transparent);
  outline-offset: 2px;
}
.composer-send {
  width: 48px;
  height: 48px;
  display: grid;
  place-items: center;
  border: 0;
  border-radius: var(--radius-md);
  color: var(--on-primary);
  background: var(--primary);
}
.composer-send:disabled {
  background: var(--line-strong);
}
.send-spinner {
  width: 19px;
  height: 19px;
  border: 2px solid color-mix(in srgb, var(--on-primary) 35%, transparent);
  border-top-color: var(--on-primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
.assistant-login {
  min-height: 0;
  flex: 1;
  overflow: auto;
  padding: 48px 24px;
  display: grid;
  place-items: center;
  align-content: center;
  text-align: center;
}
.assistant-login > span {
  width: 72px;
  height: 72px;
}
.assistant-login h1 {
  margin: 22px 0 0;
  font-size: 32px;
}
.assistant-login p {
  margin: 10px 0 24px;
  color: var(--ink-secondary);
}
.memory-scrim {
  position: fixed;
  z-index: 300;
  inset: 0;
  border: 0;
  background: rgba(16, 24, 20, 0.52);
}
.memory-panel {
  width: min(420px, 92vw);
  padding: 28px 24px;
  position: fixed;
  z-index: 310;
  top: 0;
  right: 0;
  bottom: 0;
  overflow: auto;
  border-left: 1px solid var(--line);
  background: var(--surface);
  box-shadow: var(--shadow-2);
}
.memory-panel > header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}
.memory-panel h2 {
  margin: 0;
  font-size: 26px;
}
.memory-panel header > button {
  width: 44px;
  height: 44px;
  display: grid;
  place-items: center;
  border: 0;
  border-radius: var(--radius-md);
  color: var(--ink-secondary);
  background: var(--surface-soft);
  font-size: 24px;
}
.memory-panel > p {
  margin: 12px 0 24px;
  color: var(--ink-secondary);
  font-size: 12px;
  line-height: 1.6;
}
.memory-list {
  display: grid;
  gap: 12px;
}
.memory-list article {
  padding: 16px;
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  background: var(--surface-soft);
}
.memory-list article > span,
.memory-list small {
  color: var(--ink-tertiary);
  font-size: 10px;
}
.memory-list article > p {
  margin: 8px 0;
  font-size: 13px;
  line-height: 1.65;
}
.memory-list article > div {
  margin-top: 12px;
  display: flex;
  gap: 4px;
}
.memory-list button {
  min-height: 40px;
  padding: 0 9px;
  border: 0;
  border-radius: var(--radius-sm);
  color: var(--primary-strong);
  background: transparent;
}
.memory-list button:hover {
  background: var(--primary-soft);
}
.memory-list button.danger {
  color: var(--danger);
}
.memory-empty {
  padding: 42px 0;
  color: var(--ink-tertiary);
  text-align: center;
}
.delete-dialog {
  width: min(28rem, calc(100% - 2rem));
  max-height: calc(100dvh - 2rem);
  padding: 24px;
  overflow-y: auto;
  border: 1px solid var(--line-strong);
  border-radius: var(--radius-lg);
  color: var(--ink);
  background: var(--surface);
  box-shadow: var(--shadow-2);
}
.delete-dialog::backdrop {
  background: rgba(16, 24, 20, 0.6);
}
.delete-dialog h2 {
  margin: 0 0 16px;
  font-size: 22px;
}
.delete-dialog p {
  line-height: 1.65;
  overflow-wrap: anywhere;
}
.delete-dialog__title {
  max-height: 8rem;
  padding: 12px;
  overflow-y: auto;
  border-radius: var(--radius-md);
  background: var(--surface-soft);
}
#delete-dialog-description {
  color: var(--ink-secondary);
  font-size: 14px;
}
.delete-dialog__error {
  color: var(--danger);
}
.delete-dialog__actions {
  display: flex;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 24px;
}
.delete-dialog__confirm {
  border-color: var(--danger);
  color: var(--on-danger);
  background: var(--danger);
}
.sr-only {
  width: 1px;
  height: 1px;
  padding: 0;
  position: absolute;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
@media (max-width: 820px) {
  .assistant-shell :deep(.site-header__inner) {
    padding-right: 20px;
    padding-left: 20px;
  }
  .assistant-page {
    grid-template-columns: 1fr;
  }
  .conversation-panel {
    display: none;
    width: min(24rem, 90%);
    position: absolute;
    inset: 0 auto 0 0;
    z-index: 210;
    padding: 20px 12px;
    box-shadow: var(--shadow-2);
  }
  .conversation-panel.is-open {
    display: flex;
  }
  .history-close,
  .history-toggle.memory-button {
    display: flex;
  }
  .history-close {
    align-items: center;
    justify-content: center;
  }
  .history-scrim {
    display: block;
    position: absolute;
    z-index: 200;
    inset: 0;
    border: 0;
    background: rgba(16, 24, 20, 0.52);
  }
  .chat {
    padding: 16px 18px;
  }
  .chat-heading {
    align-items: flex-start;
    flex-direction: column;
    gap: 12px;
    padding-bottom: 12px;
  }
  .chat-heading .eyebrow,
  .chat-heading div > p:last-child {
    display: none;
  }
  .chat-heading h1 {
    font-size: 26px;
  }
  .messages {
    padding-top: 24px;
  }
  .message {
    width: 100%;
    max-width: none;
  }
  .message.user {
    width: auto;
    max-width: 94%;
  }
}
</style>
