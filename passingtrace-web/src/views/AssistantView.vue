<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'

import { aiApi, type ConversationSummary, type EvidenceBundle, type UserMemory } from '@/api/ai'
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
const error = ref<string | null>(null)
const showMemories = ref(false)

async function load() {
  error.value = null
  conversations.value = await aiApi.listConversations()
  memories.value = await aiApi.listMemories()
  if (!currentId.value && conversations.value[0]) await open(conversations.value[0].id)
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

async function send() {
  const text = question.value.trim()
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
        if (event.data.replacement) answer.content = event.data.text
        else answer.content += event.data.text
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

onMounted(() => {
  if (auth.isAuthenticated)
    void load().catch((reason) => {
      error.value = String(reason)
    })
})
</script>

<template>
  <div class="app-shell">
    <header class="topbar">
      <RouterLink class="brand" to="/"
        ><span class="brand-mark">P</span><span>PassingTrace</span></RouterLink
      >
      <nav class="nav-links">
        <RouterLink to="/events">记录</RouterLink><RouterLink to="/assistant">AI 助手</RouterLink>
      </nav>
      <button class="text-button" @click="showMemories = !showMemories">
        我的记忆 {{ memories.length }}
      </button>
    </header>
    <main class="assistant-page">
      <aside>
        <button class="button button-accent compact-button new-chat" @click="create">
          ＋ 新对话
        </button>
        <button
          v-for="conversation in conversations"
          :key="conversation.id"
          class="conversation"
          :class="{ active: currentId === conversation.id }"
          @click="open(conversation.id)"
        >
          {{ conversation.title }}
        </button>
      </aside>
      <section class="chat">
        <header>
          <p class="eyebrow">PRIVATE RECORD ASSISTANT</p>
          <h1>问问你的记录</h1>
          <p>回答只使用你的记录、结构化统计与有证据的记忆。</p>
        </header>
        <div class="messages">
          <p v-if="messages.length === 0" class="empty">
            例如：我最近去过哪些地方？上个月记录了多少笔花费？
          </p>
          <article
            v-for="(message, index) in messages"
            :key="index"
            :class="message.role.toLowerCase()"
          >
            <strong>{{ message.role === 'User' ? '你' : 'PassingTrace' }}</strong>
            <p>{{ message.content || '正在思考…' }}</p>
            <div v-if="message.evidence?.records?.length" class="evidence">
              <span>证据</span>
              <RouterLink
                v-for="record in message.evidence.records"
                :key="record.eventId"
                :to="`/events/${record.eventId}`"
              >
                Event #{{ record.eventId }} · {{ record.title || '无标题' }}
              </RouterLink>
            </div>
          </article>
        </div>
        <p v-if="error" class="error-banner">{{ error }}</p>
        <form class="composer" @submit.prevent="send">
          <textarea
            v-model="question"
            rows="2"
            maxlength="8000"
            placeholder="询问你的经历、计划、花费或趋势…"
          />
          <button class="button button-accent" :disabled="busy">
            {{ busy ? '回答中…' : '发送' }}
          </button>
        </form>
      </section>
      <aside v-if="showMemories" class="memory-panel">
        <h2>我的长期记忆</h2>
        <p>自动记忆都有 Event 证据，你可以确认、修正或遗忘。</p>
        <article v-for="memory in memories" :key="memory.id">
          <span>{{ memory.type }} · {{ memory.status }}</span>
          <p>{{ memory.content }}</p>
          <small
            >置信度 {{ Math.round(memory.confidence * 100) }}% · 证据
            {{ memory.evidenceEventIds.join(', ') }}</small
          >
          <div>
            <button @click="confirmMemory(memory)">确认</button
            ><button @click="editMemory(memory)">修正</button
            ><button @click="forgetMemory(memory)">遗忘</button>
          </div>
        </article>
      </aside>
    </main>
  </div>
</template>

<style scoped>
.assistant-page {
  display: grid;
  grid-template-columns: 220px minmax(0, 760px);
  justify-content: center;
  min-height: calc(100vh - 76px);
}
aside {
  padding: 32px 18px;
  border-right: 1px solid var(--line);
}
.new-chat {
  width: 100%;
  margin-bottom: 20px;
}
.conversation {
  width: 100%;
  padding: 10px;
  border: 0;
  border-bottom: 1px solid var(--line);
  background: transparent;
  text-align: left;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.conversation.active {
  color: var(--red);
  background: rgba(171, 66, 47, 0.06);
}
.chat {
  display: flex;
  flex-direction: column;
  min-height: calc(100vh - 76px);
  padding: 42px;
}
.chat h1 {
  margin: 4px 0;
  font-family: 'Noto Serif SC', serif;
  font-size: 34px;
  font-weight: 500;
}
.chat header > p:last-child {
  margin: 0;
  color: rgba(36, 35, 31, 0.55);
  font-size: 13px;
}
.messages {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 18px;
  padding: 32px 0;
}
.empty {
  margin: auto;
  color: rgba(36, 35, 31, 0.42);
  text-align: center;
}
article {
  padding: 16px 18px;
  border: 1px solid var(--line);
}
article.user {
  align-self: flex-end;
  max-width: 78%;
  background: var(--ink);
  color: white;
}
article.assistant {
  align-self: stretch;
  background: rgba(245, 240, 230, 0.58);
}
article strong {
  font-size: 11px;
  letter-spacing: 0.1em;
}
article p {
  margin: 8px 0 0;
  white-space: pre-wrap;
  line-height: 1.75;
}
.evidence {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 14px;
  padding-top: 12px;
  border-top: 1px solid var(--line);
  font-size: 11px;
}
.evidence a {
  color: var(--red);
}
.composer {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 10px;
  padding-top: 16px;
  border-top: 1px solid var(--line);
}
.composer textarea {
  resize: none;
  padding: 12px;
  border: 1px solid var(--line);
  background: var(--paper);
  font: inherit;
}
.memory-panel {
  position: fixed;
  z-index: 10;
  top: 76px;
  right: 0;
  bottom: 0;
  width: min(390px, 92vw);
  overflow: auto;
  border-left: 1px solid var(--line);
  background: var(--paper);
  box-shadow: -14px 0 34px rgba(0, 0, 0, 0.08);
}
.memory-panel h2 {
  font-family: 'Noto Serif SC', serif;
}
.memory-panel > p {
  color: rgba(36, 35, 31, 0.55);
  font-size: 12px;
}
.memory-panel article {
  margin: 12px 0;
  background: white;
}
.memory-panel article > span,
.memory-panel small {
  color: rgba(36, 35, 31, 0.5);
  font-size: 10px;
}
.memory-panel article div {
  display: flex;
  gap: 8px;
  margin-top: 10px;
}
.memory-panel button {
  border: 0;
  background: transparent;
  color: var(--red);
}
@media (max-width: 800px) {
  .assistant-page {
    grid-template-columns: 1fr;
  }
  .assistant-page > aside:first-child {
    display: none;
  }
  .chat {
    padding: 24px 18px;
  }
}
</style>
