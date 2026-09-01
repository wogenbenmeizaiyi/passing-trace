<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'

import { aiApi, type ConversationSummary, type EvidenceBundle, type UserMemory } from '@/api/ai'
import BrandMark from '@/components/BrandMark.vue'
import WebAppHeader from '@/components/WebAppHeader.vue'
import { useAuthStore } from '@/stores/auth'

interface ChatItem {
  role: 'User' | 'Assistant'
  content: string
  evidence?: EvidenceBundle | null
}

const auth = useAuthStore()
const conversations = ref<ConversationSummary[]>([])
const currentId = ref<string | null>(null)
const messages = ref<ChatItem[]>([])
const memories = ref<UserMemory[]>([])
const question = ref('')
const busy = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)
const showMemories = ref(false)
const suggestions = ['帮我总结这个月的生活', '我最近去过哪些地方？', '最近有哪些计划还没有完成？']

async function load() {
  loading.value = true
  error.value = null
  try {
    conversations.value = await aiApi.listConversations()
    memories.value = await aiApi.listMemories()
    if (!currentId.value && conversations.value[0]) await open(conversations.value[0].id)
  } finally {
    loading.value = false
  }
}

async function create() {
  const value = await aiApi.createConversation()
  conversations.value.unshift(value)
  currentId.value = value.id
  messages.value = []
}

async function open(id: string) {
  const value = await aiApi.getConversation(id)
  currentId.value = id
  messages.value = value.messages.map((item) => ({
    role: item.role === 'User' ? 'User' : 'Assistant',
    content: item.content,
    evidence: item.evidence,
  }))
}

async function send(textOverride?: string) {
  const text = (textOverride ?? question.value).trim()
  if (!text || busy.value) return
  if (!currentId.value) await create()
  messages.value.push({ role: 'User', content: text })
  const answer: ChatItem = { role: 'Assistant', content: '' }
  messages.value.push(answer)
  question.value = ''
  busy.value = true
  error.value = null
  try {
    await aiApi.sendMessage(currentId.value!, text, (event) => {
      if (event.type === 'delta') {
        answer.content = event.data.replacement ? event.data.text : answer.content + event.data.text
      } else if (event.type === 'evidence') {
        answer.evidence = event.data
      } else if (event.type === 'error') {
        error.value = event.data.message
      }
    })
    conversations.value = await aiApi.listConversations()
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '发送失败。'
  } finally {
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
  if (auth.isAuthenticated)
    void load().catch((reason) => {
      error.value = reason instanceof Error ? reason.message : String(reason)
    })
})
watch(
  () => auth.isAuthenticated,
  (authenticated) => {
    if (authenticated && conversations.value.length === 0) void load()
  },
)
</script>

<template>
  <div class="app-shell assistant-shell">
    <WebAppHeader />

    <main v-if="auth.isAuthenticated" class="assistant-page">
      <aside class="conversation-panel" aria-label="对话历史">
        <div class="conversation-panel__heading">
          <p class="eyebrow">CONVERSATIONS</p>
          <h2>对话</h2>
        </div>
        <button class="button button-primary new-chat" @click="create">
          <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
            <path d="M12 5v14M5 12h14" />
          </svg>
          新对话
        </button>
        <div class="conversation-list">
          <button
            v-for="conversation in conversations"
            :key="conversation.id"
            class="conversation"
            :class="{ active: currentId === conversation.id }"
            :aria-pressed="currentId === conversation.id"
            @click="open(conversation.id)"
          >
            <span>{{ conversation.title }}</span>
            <small>{{ new Date(conversation.updatedAt).toLocaleDateString('zh-CN') }}</small>
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
          <button class="memory-button" :aria-expanded="showMemories" @click="showMemories = true">
            <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
              <path d="M5 5h11a3 3 0 0 1 3 3v11H8a3 3 0 0 1-3-3V5Z" />
              <path d="M8 19a3 3 0 0 1 0-6h11M9 8h6" />
            </svg>
            我的记忆 <span>{{ memories.length }}</span>
          </button>
        </header>

        <div class="messages" aria-live="polite">
          <section v-if="messages.length === 0" class="assistant-empty">
            <span class="assistant-empty__mark"><BrandMark /></span>
            <h2>想从哪一段生活问起？</h2>
            <p>我会先检索你的记录，再用可点击的记录标题说明依据。</p>
            <div class="suggestions">
              <button v-for="suggestion in suggestions" :key="suggestion" @click="send(suggestion)">
                {{ suggestion }}
                <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
                  <path d="m9 5 7 7-7 7" />
                </svg>
              </button>
            </div>
          </section>

          <article
            v-for="(message, index) in messages"
            :key="index"
            class="message"
            :class="message.role.toLowerCase()"
          >
            <span v-if="message.role === 'Assistant'" class="message-mark"><BrandMark /></span>
            <div class="message-body">
              <strong>{{ message.role === 'User' ? '你' : 'PassingTrace' }}</strong>
              <p>{{ message.content || '正在检索你的记录…' }}</p>
              <div v-if="message.evidence?.records?.length" class="evidence">
                <span>回答依据</span>
                <RouterLink
                  v-for="record in message.evidence.records"
                  :key="record.eventId"
                  :to="`/events/${record.eventId}`"
                >
                  <strong>{{ record.title || '未命名记录' }}</strong
                  ><small>打开记录</small>
                </RouterLink>
              </div>
            </div>
          </article>
        </div>

        <p v-if="error" class="error-banner" role="alert">{{ error }}</p>
        <form class="composer" @submit.prevent="send()">
          <label class="sr-only" for="assistant-question">询问你的记录</label>
          <textarea
            id="assistant-question"
            v-model="question"
            rows="1"
            maxlength="8000"
            placeholder="询问你的经历、计划、花费或趋势…"
            @input="resizeComposer"
            @keydown.enter.exact.prevent="send()"
          />
          <button class="composer-send" :disabled="busy || !question.trim()" aria-label="发送问题">
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
      <button class="button button-primary" @click="auth.login('/assistant')">扫码登录</button>
    </main>

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
  min-height: 100dvh;
}
.assistant-page {
  width: min(1280px, 100%);
  min-height: calc(100dvh - 72px);
  margin: 0 auto;
  display: grid;
  grid-template-columns: 260px minmax(0, 840px);
  justify-content: center;
  background: var(--surface);
}
.conversation-panel {
  min-width: 0;
  padding: 34px 18px;
  border-right: 1px solid var(--line);
  background: var(--surface-soft);
}
.conversation-panel__heading {
  padding: 0 8px 22px;
}
.conversation-panel h2 {
  margin: 0;
  font-size: 24px;
  letter-spacing: -0.035em;
}
.new-chat {
  width: 100%;
}
.new-chat .ui-icon {
  width: 18px;
  height: 18px;
}
.conversation-list {
  margin-top: 22px;
  display: grid;
  gap: 5px;
}
.conversation {
  width: 100%;
  min-height: 58px;
  padding: 10px 12px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 3px;
  border: 1px solid transparent;
  border-radius: var(--radius-md);
  color: var(--ink-secondary);
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
  overflow: hidden;
  font-size: 12px;
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.conversation small {
  color: var(--ink-tertiary);
  font-size: 9px;
}
.conversation-empty {
  padding: 24px 10px;
  color: var(--ink-tertiary);
  font-size: 11px;
  text-align: center;
}
.chat {
  min-width: 0;
  min-height: calc(100dvh - 72px);
  padding: 36px 42px 24px;
  display: flex;
  flex-direction: column;
  background: var(--surface);
}
.chat-heading {
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
  padding: 32px 0 20px;
  display: flex;
  flex-direction: column;
  gap: 20px;
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
  max-width: 86%;
  display: flex;
  align-items: flex-start;
  gap: 10px;
}
.message.user {
  align-self: flex-end;
}
.message-mark {
  width: 34px;
  height: 34px;
  flex: 0 0 auto;
}
.message-body {
  min-width: 0;
  padding: 15px 17px;
  border: 1px solid var(--line);
  border-radius: 5px 18px 18px;
  background: var(--surface-soft);
}
.message.user .message-body {
  border-color: var(--primary);
  border-radius: 18px 18px 5px;
  color: var(--on-primary);
  background: var(--primary);
}
.message-body > strong {
  font-size: 10px;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}
.message-body > p {
  margin: 7px 0 0;
  overflow-wrap: anywhere;
  font-size: 14px;
  line-height: 1.75;
  white-space: pre-wrap;
}
.evidence {
  margin-top: 16px;
  padding-top: 13px;
  display: grid;
  gap: 8px;
  border-top: 1px solid var(--line);
}
.evidence > span {
  color: var(--ink-tertiary);
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.08em;
}
.evidence a {
  min-height: 48px;
  padding: 10px 12px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  color: var(--primary-strong);
  background: var(--surface);
}
.evidence a strong {
  overflow: hidden;
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.evidence a small {
  color: var(--ink-tertiary);
  font-size: 9px;
  white-space: nowrap;
}
.composer {
  min-height: 64px;
  padding: 8px;
  position: sticky;
  bottom: 16px;
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
  min-height: calc(100dvh - 72px);
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
  .assistant-page {
    grid-template-columns: 1fr;
  }
  .conversation-panel {
    display: none;
  }
  .chat {
    min-height: calc(100dvh - 124px);
    padding: 28px 18px 16px;
  }
  .chat-heading {
    align-items: flex-start;
    flex-direction: column;
  }
  .messages {
    padding-top: 24px;
  }
  .message {
    max-width: 94%;
  }
  .composer {
    bottom: 8px;
  }
}
</style>
