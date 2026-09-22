<script setup lang="ts">
import { computed, nextTick, onActivated, onDeactivated, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import WebAppHeader from '@/components/WebAppHeader.vue'
import FriendsPanel from '@/components/FriendsPanel.vue'
import PersonAvatar from '@/components/PersonAvatar.vue'
import ShareDialog from '@/components/ShareDialog.vue'
import {
  socialApi,
  type DirectConversation,
  type DirectMessage,
  type Notification,
  type SendMessage,
} from '@/api/social'
import { eventsApi } from '@/api/events'
import { storylinesApi } from '@/api/storylines'
import { useAuthStore } from '@/stores/auth'
import { useSocialStore } from '@/stores/social'
import { randomUuid } from '@/utils/id'

defineOptions({ name: 'MessagesView' })
const auth = useAuthStore(),
  social = useSocialStore(),
  route = useRoute()
const conversations = ref<DirectConversation[]>([]),
  messages = ref<DirectMessage[]>([]),
  notifications = ref<Notification[]>([])
const cursor = ref<string | null>(null),
  older = ref<string | null>(null),
  busy = ref(false),
  loading = ref(false),
  error = ref('')
const pane = ref<HTMLElement | null>(null),
  draft = ref(''),
  tab = ref('chats'),
  chooser = ref(false),
  active = ref(true)
// Keep this chat's identity while the cached view is displaying a shared document.
const id = ref(typeof route.params.id === 'string' ? route.params.id : '')
const current = computed(() => conversations.value.find((c) => c.id === id.value))
const drafts = new Map<string, string>(),
  positions = new Map<string, number>(),
  readThrough = new Map<string, number>()
const failed = ref<{ conversationId: string; body: SendMessage } | null>(null)
const choices = ref<Array<{ id: string | number; title: string }>>([]),
  choiceKind = ref<'record' | 'storyline'>('record'),
  choiceCursor = ref<string | null>(null)
const selection = ref<{ eventId?: number; storylineId?: string } | null>(null)
const chooserDialog = ref<HTMLDialogElement | null>(null)
let generation = 0,
  syncing = false,
  syncAgain = false

async function list(more = false) {
  const page = await socialApi.conversations(more ? (cursor.value ?? undefined) : undefined)
  const existing = more
    ? conversations.value
    : conversations.value.filter((c) => !page.items.some((x) => x.id === c.id))
  conversations.value = [...page.items, ...existing].sort((a, b) =>
    b.updatedAt.localeCompare(a.updatedAt),
  )
  if (more || !cursor.value) cursor.value = page.nextCursor
}
async function markRead() {
  if (!active.value || tab.value !== 'chats' || document.visibilityState !== 'visible' || !id.value)
    return
  if (pane.value && pane.value.scrollHeight - pane.value.scrollTop - pane.value.clientHeight > 100)
    return
  const last = messages.value[messages.value.length - 1]?.id ?? 0
  if (last <= (readThrough.get(id.value) ?? 0)) return
  const target = id.value
  readThrough.set(target, last)
  try {
    await socialApi.read(target, last)
  } catch {
    readThrough.delete(target)
    return
  }
  if (current.value && id.value === target) current.value.unreadCount = 0
  void social.refresh().catch(() => {})
}
async function open() {
  const turn = ++generation,
    target = id.value
  error.value = ''
  messages.value = []
  older.value = null
  draft.value = drafts.get(target) ?? ''
  if (!target || !auth.isAuthenticated) return
  tab.value = 'chats'
  loading.value = true
  try {
    const [page, summary] = await Promise.all([
      socialApi.messages(target),
      socialApi.conversation(target),
    ])
    if (turn !== generation) return
    conversations.value = [...conversations.value.filter((c) => c.id !== target), summary]
    messages.value = page.items.filter((m) => m.id > summary.clearedThroughId)
    older.value = page.nextCursor
    await nextTick()
    if (pane.value) pane.value.scrollTop = positions.get(target) ?? pane.value.scrollHeight
    await markRead()
  } catch {
    if (turn === generation) error.value = '暂时无法打开聊天，请重试。'
  } finally {
    if (turn === generation) loading.value = false
  }
}
async function loadOlder() {
  if (!older.value || loading.value) return
  const target = id.value,
    turn = generation,
    height = pane.value?.scrollHeight ?? 0
  loading.value = true
  try {
    const page = await socialApi.messages(target, Number(older.value))
    if (turn !== generation) return
    const known = new Set(messages.value.map((m) => m.id))
    messages.value.unshift(...page.items.filter((m) => !known.has(m.id)))
    older.value = page.nextCursor
    await nextTick()
    if (pane.value) pane.value.scrollTop += pane.value.scrollHeight - height
  } catch {
    error.value = '较早的消息未能加载，请重试。'
  } finally {
    if (turn === generation) loading.value = false
  }
}
async function sync() {
  if (!active.value || !auth.isAuthenticated) return
  if (syncing) {
    syncAgain = true
    return
  }
  syncing = true
  try {
    do {
      syncAgain = false
      await list()
      const target = id.value,
        turn = generation
      if (!target || loading.value) continue
      const summary = await socialApi.conversation(target)
      if (turn !== generation) continue
      conversations.value = [...conversations.value.filter((c) => c.id !== target), summary]
      messages.value = messages.value.filter((m) => m.id > summary.clearedThroughId)
      // Fetch only new messages, following the cursor after an offline gap.
      let after = messages.value[messages.value.length - 1]?.id ?? 0
      let more = true
      const atBottom = pane.value
        ? pane.value.scrollHeight - pane.value.scrollTop - pane.value.clientHeight < 100
        : true
      while (more && turn === generation && active.value) {
        const page = await socialApi.messages(target, undefined, after)
        if (turn !== generation) break
        const known = new Set(messages.value.map((m) => m.id))
        messages.value.push(...page.items.filter((m) => !known.has(m.id)))
        const next = page.items[page.items.length - 1]?.id ?? after
        more = page.nextCursor !== null && next > after
        after = next
      }
      // Reload visible share cards after revocations without fetching other conversations.
      if (turn === generation && messages.value.some((m) => m.shareId)) {
        const ids = [...new Set(messages.value.flatMap((m) => (m.shareId ? [m.shareId] : [])))]
        for (let at = 0; at < ids.length; at += 100) {
          const rows = await socialApi.shareStatuses(target, ids.slice(at, at + 100))
          if (turn !== generation) break
          const updates = new Map(rows.map((s) => [s.id, s]))
          messages.value = messages.value.map((m) => {
            const update = m.shareId ? updates.get(m.shareId) : undefined
            return update ? { ...m, shareTitle: update.title, shareAvailable: update.available } : m
          })
        }
      }
      await nextTick()
      if (atBottom && pane.value) pane.value.scrollTop = pane.value.scrollHeight
      await markRead()
    } while (syncAgain && active.value)
  } catch {
    /* The connection indicator offers reconnect feedback; sending retains errors. */
  } finally {
    syncing = false
  }
}
async function send(retry = false) {
  if (busy.value || (!retry && !draft.value.trim())) return
  const pending = retry
    ? failed.value
    : {
        conversationId: id.value,
        body: { clientMessageId: randomUuid(), kind: 'text' as const, text: draft.value.trim() },
      }
  if (!pending) return
  busy.value = true
  error.value = ''
  failed.value = pending
  try {
    const message = await socialApi.send(pending.conversationId, pending.body)
    if (pending.conversationId === id.value) {
      if (!messages.value.some((m) => m.id === message.id)) messages.value.push(message)
      if (draft.value.trim() === pending.body.text) draft.value = ''
      await nextTick()
      if (pane.value) pane.value.scrollTop = pane.value.scrollHeight
    }
    failed.value = null
    await list()
  } catch {
    error.value = '这条消息未能确认发送，请点击重试。不会重复发送。'
  } finally {
    busy.value = false
  }
}
async function clear() {
  if (!window.confirm('仅清除你这边的聊天显示，不会删除对方消息或停止分享。继续吗？')) return
  try {
    await socialApi.clear(id.value)
    messages.value = []
    older.value = null
    await list()
  } catch {
    error.value = '清除失败，请稍后重试。'
  }
}
async function notice(more = false) {
  try {
    const rows = await socialApi.notifications(
      more ? notifications.value[notifications.value.length - 1]?.id : undefined,
    )
    notifications.value = more ? [...notifications.value, ...rows] : rows
    if (rows[0]) await socialApi.readNotifications(rows[0].id)
  } catch {
    error.value = '提醒暂时无法加载，请重试。'
  }
}
function noticeTarget(n: Notification) {
  return n.target &&
    /^\/(messages(?:\/[\da-f-]+)?|events|joint-records\/\d+|shares\/[\da-f-]+)(?:\?[^#]*)?$/i.test(
      n.target,
    )
    ? n.target
    : '/messages?tab=friends'
}
async function choose(kind: 'record' | 'storyline', more = false) {
  chooser.value = true
  choiceKind.value = kind
  try {
    const page =
      kind === 'record'
        ? await eventsApi.list({
            limit: 20,
            cursor: more && choiceCursor.value ? Number(choiceCursor.value) : undefined,
          })
        : await storylinesApi.list({
            limit: 20,
            cursor: more ? (choiceCursor.value ?? undefined) : undefined,
          })
    const rows = page.items.map((x) => ({ id: x.id, title: x.title || '未命名记录' }))
    choices.value = more ? [...choices.value, ...rows] : rows
    choiceCursor.value = page.nextCursor == null ? null : String(page.nextCursor)
  } catch {
    error.value = '你的内容暂时无法加载，请重试。'
  }
}
function savePosition() {
  if (id.value && pane.value) positions.set(id.value, pane.value.scrollTop)
}
function onScroll() {
  savePosition()
  void markRead()
}
function selectShare(item: { id: string | number }) {
  selection.value =
    choiceKind.value === 'record' ? { eventId: Number(item.id) } : { storylineId: String(item.id) }
  chooser.value = false
}
watch(draft, (value) => {
  if (id.value) drafts.set(id.value, value)
})
watch(
  () => route.path,
  (path) => {
    if (path !== '/messages' && !path.startsWith('/messages/')) return
    const target = typeof route.params.id === 'string' ? route.params.id : ''
    if (target === id.value) return
    savePosition()
    if (id.value) drafts.set(id.value, draft.value)
    id.value = target
    void open()
  },
)
watch(
  () => route.query.tab,
  (value) => {
    if (value === 'friends' || value === 'notifications') tab.value = value
  },
  { immediate: true },
)
watch(
  () => social.change,
  () => {
    void sync()
    if (tab.value === 'notifications') void notice()
  },
)
watch(tab, (value) => {
  if (value === 'notifications') void notice()
})
watch(chooser, async (value) => {
  await nextTick()
  if (value) chooserDialog.value?.showModal()
  else chooserDialog.value?.close()
})
onMounted(async () => {
  if (auth.isAuthenticated) {
    try {
      await list()
      await open()
    } catch {
      error.value = '消息暂时无法加载，请重试。'
    }
  }
})
onActivated(() => {
  active.value = true
  void sync()
})
onDeactivated(() => {
  savePosition()
  active.value = false
})
</script>

<template>
  <div class="messages-page">
    <WebAppHeader />
    <main v-if="!auth.isAuthenticated" class="login-prompt">
      <h1>和好友聊聊共同的经历</h1>
      <button class="button button-primary" @click="auth.login('/messages')">扫码登录</button>
    </main>
    <main v-else class="messages-workspace" :class="{ 'has-chat': id && tab === 'chats' }">
      <aside class="conversation-pane">
        <header>
          <h1>消息</h1>
          <small role="status">{{ social.online ? '已连接' : '重新连接中…' }}</small>
        </header>
        <nav aria-label="消息分类">
          <button
            v-for="t in [
              { id: 'chats', text: '聊天' },
              { id: 'friends', text: '好友' },
              { id: 'notifications', text: '提醒' },
            ]"
            :key="t.id"
            :aria-pressed="tab === t.id"
            @click="tab = t.id"
          >
            {{ t.text }}
          </button>
        </nav>
        <div class="conversation-list">
          <RouterLink
            v-for="c in conversations"
            :key="c.id"
            :to="`/messages/${c.id}`"
            class="conversation"
            :class="{ selected: c.id === id }"
            @click="tab = 'chats'"
          >
            <PersonAvatar :id="c.person.id" :has-avatar="c.person.hasAvatar" /><span
              class="conversation-text"
              ><strong>{{ c.person.nickname }}</strong
              ><small>{{ c.preview || '开始聊聊吧' }}</small></span
            ><b v-if="c.unreadCount" class="unread">{{
              c.unreadCount > 99 ? '99+' : c.unreadCount
            }}</b>
          </RouterLink>
          <p v-if="!conversations.length">还没有聊天，先去好友页添加一位朋友。</p>
          <button
            v-if="cursor"
            class="text-button"
            @click="list(true).catch(() => (error = '加载失败，请重试。'))"
          >
            加载更多会话
          </button>
        </div>
      </aside>
      <section v-if="tab === 'friends'" class="secondary-pane"><FriendsPanel /></section>
      <section v-else-if="tab === 'notifications'" class="secondary-pane">
        <h2>提醒</h2>
        <p v-if="!notifications.length">暂时没有新提醒</p>
        <RouterLink v-for="n in notifications" :key="n.id" :to="noticeTarget(n)" class="notice"
          >{{ n.text }}<small>{{ new Date(n.createdAt).toLocaleString() }}</small></RouterLink
        ><button v-if="notifications.length >= 30" class="text-button" @click="notice(true)">
          查看更早提醒
        </button>
      </section>
      <section v-else-if="id" class="chat-pane">
        <header>
          <RouterLink class="chat-back" to="/messages">返回消息</RouterLink>
          <h2>{{ current?.person.nickname || '聊天' }}</h2>
          <button class="text-button" @click="clear">清除我的历史</button>
        </header>
        <div
          ref="pane"
          class="message-scroll"
          aria-label="聊天内容"
          tabindex="0"
          @scroll.passive="onScroll"
        >
          <button v-if="older" :disabled="loading" class="older text-button" @click="loadOlder">
            {{ loading ? '正在加载…' : '查看更早消息' }}
          </button>
          <p v-if="loading && !messages.length">正在打开聊天…</p>
          <article
            v-for="m in messages"
            :key="m.id"
            class="message"
            :class="{ own: m.senderId === auth.user?.profile.sub }"
          >
            <p v-if="m.kind === 'text'" class="bubble">{{ m.text }}</p>
            <RouterLink
              v-else-if="m.shareAvailable && m.shareId"
              class="bubble share-card"
              :to="{ path: `/shares/${m.shareId}`, query: { from: `/messages/${id}` } }"
              ><small>{{ m.kind === 'record' ? '分享的记录' : '分享的故事线' }}</small
              ><strong>{{ m.shareTitle }}</strong
              ><span>查看发送时版本 →</span></RouterLink
            >
            <p v-else class="bubble unavailable">内容已不可查看</p>
            <small
              >{{ new Date(m.createdAt).toLocaleString()
              }}<template v-if="m.senderId === auth.user?.profile.sub">
                · {{ m.id <= (current?.peerReadThroughId ?? 0) ? '已读' : '已发送' }}</template
              ></small
            >
          </article>
        </div>
        <footer>
          <p v-if="error" role="alert">
            {{ error }}
            <button v-if="failed" class="text-button" :disabled="busy" @click="send(true)">
              重试发送</button
            ><button v-else class="text-button" @click="open">重新加载</button>
          </p>
          <p v-if="current?.canSend === false">好友关系已结束，无法继续发送消息。</p>
          <template v-else
            ><div class="share-actions">
              <button class="text-button" @click="choose('record')">分享记录</button
              ><button class="text-button" @click="choose('storyline')">分享故事线</button>
            </div>
            <form @submit.prevent="send()">
              <textarea
                v-model="draft"
                aria-label="聊天内容"
                placeholder="和好友聊聊…"
                maxlength="8000"
                rows="2"
                @keydown.ctrl.enter.prevent="send()"
              /><button class="button button-primary" :disabled="busy || !draft.trim()">
                {{ busy ? '发送中…' : '发送' }}
              </button>
            </form></template
          >
        </footer>
      </section>
      <section v-else class="empty-chat">
        <h2>把共同的经历聊起来</h2>
        <p>选择一位好友，聊聊天，也可以分享记录和故事线。</p>
        <button class="button button-secondary" @click="tab = 'friends'">查看好友</button>
      </section>
    </main>
    <dialog ref="chooserDialog" class="chooser" @cancel="chooser = false" aria-label="选择分享内容">
      <section>
        <header>
          <h2>选择自己的{{ choiceKind === 'record' ? '记录' : '故事线' }}</h2>
          <button class="text-button" @click="chooser = false">关闭</button>
        </header>
        <button v-for="c in choices" :key="c.id" class="choice" @click="selectShare(c)">
          {{ c.title }}</button
        ><button v-if="choiceCursor" class="text-button" @click="choose(choiceKind, true)">
          加载更多
        </button>
      </section>
    </dialog>
    <ShareDialog
      v-if="selection"
      v-bind="selection"
      :initial-friend-id="current?.friendshipId"
      @close="selection = null"
    />
  </div>
</template>

<style scoped>
.messages-page {
  height: 100dvh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: var(--canvas);
}
.messages-workspace {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: minmax(16rem, 24%) minmax(0, 1fr);
}
.conversation-pane {
  display: flex;
  flex-direction: column;
  min-height: 0;
  border-right: 1px solid var(--line);
  background: var(--surface-soft);
}
header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem 1.5rem;
  border-bottom: 1px solid var(--line);
}
h1,
h2 {
  margin: 0;
  font-size: 1.3rem;
}
nav {
  display: flex;
  gap: 0.5rem;
  padding: 0.75rem;
}
nav button {
  flex: 1;
  border: 0;
  border-radius: 0.6rem;
  min-height: 44px;
  background: transparent;
  color: var(--ink-secondary);
  cursor: pointer;
}
nav button[aria-pressed='true'] {
  background: var(--primary-soft);
  color: var(--primary-strong);
}
.conversation-list,
.secondary-pane {
  overflow: auto;
  min-height: 0;
  padding: 1rem;
  overscroll-behavior: contain;
}
.conversation {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1rem;
  border-radius: 0.8rem;
  color: var(--ink);
  text-decoration: none;
  content-visibility: auto;
  contain-intrinsic-size: auto 80px;
}
.conversation.selected {
  background: var(--primary-soft);
}
.conversation-text {
  min-width: 0;
  flex: 1;
}
.conversation small {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 100%;
  margin-top: 0.4rem;
}
small {
  color: var(--ink-secondary);
}
.unread {
  background: var(--primary);
  color: var(--on-primary);
  border-radius: 2rem;
  padding: 0.15rem 0.45rem;
  font-size: 0.8rem;
}
.chat-pane {
  display: flex;
  flex-direction: column;
  min-height: 0;
  min-width: 0;
}
.chat-back {
  display: none;
}
.message-scroll {
  flex: 1;
  overflow: auto;
  overscroll-behavior: contain;
  padding: clamp(1rem, 3vw, 3rem);
}
.message {
  display: flex;
  align-items: flex-start;
  flex-direction: column;
  margin-bottom: 1.5rem;
  content-visibility: auto;
  contain-intrinsic-size: auto 110px;
}
.message.own {
  align-items: flex-end;
}
.bubble {
  margin: 0 0 0.4rem;
  padding: 1rem 1.25rem;
  max-width: min(85%, 48rem);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  border: 1px solid var(--line);
  border-radius: 1rem;
  background: var(--surface);
  color: var(--ink);
}
.own .bubble {
  background: var(--primary-soft);
}
.share-card {
  display: grid;
  gap: 0.7rem;
  min-width: min(20rem, 80%);
  text-decoration: none;
}
.share-card span {
  color: var(--primary-strong);
}
.unavailable {
  color: var(--ink-secondary);
}
.older {
  display: block;
  margin: auto;
}
footer {
  padding: 1rem 1.5rem;
  border-top: 1px solid var(--line);
}
footer form {
  display: flex;
  align-items: end;
  gap: 1rem;
}
textarea {
  flex: 1;
  min-width: 0;
  padding: 0.75rem;
  border: 1px solid var(--line);
  border-radius: 0.8rem;
  background: var(--surface);
  color: var(--ink);
  resize: vertical;
  font: inherit;
}
.share-actions {
  display: flex;
  gap: 1rem;
  margin-bottom: 0.5rem;
}
[role='alert'] {
  color: var(--danger);
}
.empty-chat,
.login-prompt {
  padding: 3rem;
  align-self: center;
  text-align: center;
}
.notice {
  display: block;
  padding: 1.5rem;
  border-bottom: 1px solid var(--line);
  color: var(--ink);
}
.notice small {
  display: block;
  margin-top: 0.5rem;
}
.chooser {
  border: 1px solid var(--line);
  border-radius: 1rem;
  padding: 0;
  background: var(--surface);
  color: var(--ink);
  width: min(38rem, calc(100vw - 2rem));
}
.chooser::backdrop {
  background: rgb(0 0 0 / 0.45);
}
.chooser section {
  background: var(--surface);
  padding: 1rem;
  width: 100%;
  max-height: 80dvh;
  overflow: auto;
  border-radius: 1rem;
}
.choice {
  display: block;
  width: 100%;
  text-align: left;
  padding: 1rem;
  background: transparent;
  color: var(--ink);
  border: 0;
  border-bottom: 1px solid var(--line);
  cursor: pointer;
}
@media (max-width: 760px) {
  .messages-workspace {
    grid-template-columns: 1fr;
  }
  .has-chat .conversation-pane {
    display: none;
  }
  .empty-chat {
    display: none;
  }
  .chat-back {
    display: inline;
  }
  .message-scroll {
    padding: 1rem;
  }
  .secondary-pane {
    position: absolute;
    inset: 12rem 0 0;
    background: var(--canvas);
  }
  .bubble {
    max-width: 92%;
  }
  footer {
    padding: 0.75rem;
  }
}
</style>
