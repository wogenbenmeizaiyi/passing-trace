<script setup lang="ts">
import { computed, nextTick, onActivated, onDeactivated, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import SocialIcon from '@/components/SocialIcon.vue'
import SocialPaneDivider from '@/components/SocialPaneDivider.vue'
import { showMessageTime, socialTime } from '@/utils/social-presentation'
import WebAppHeader from '@/components/WebAppHeader.vue'
import FriendsPanel from '@/components/FriendsPanel.vue'
import PersonAvatar from '@/components/PersonAvatar.vue'
import ShareDialog from '@/components/ShareDialog.vue'
import {
  socialApi,
  type DirectConversation,
  type DirectMessage,
  type Notification,
  type NotificationSummary,
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
const router = useRouter()
const notificationSummary = ref<NotificationSummary>({ unreadCount: 0, latest: null })
const noticesMore = ref(false)
const noticesLoaded = ref(false)
const myAvatar = ref(false)
async function refreshNotices() {
  try {
    notificationSummary.value = await socialApi.notificationSummary()
  } catch {
    /* Chat remains usable independently. */
  }
}
function navigateTab(value: string) {
  savePosition()
  return router.push({ path: '/messages', query: value === 'chats' ? {} : { tab: value } })
}
function composeKey(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey && !event.isComposing && event.keyCode !== 229) {
    event.preventDefault()
    void send()
  }
}
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
      await refreshNotices()
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
    noticesMore.value = rows.length === 30
    if (rows[0]) await socialApi.readNotifications(rows[0].id)
    await refreshNotices()
  } catch {
    error.value = '提醒暂时无法加载，请重试。'
  } finally {
    noticesLoaded.value = true
  }
}
function noticeTarget(n: Notification) {
  if (n.kind === 'friend-request') return '/messages?tab=friends&panel=requests'
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
    if (route.path !== '/messages' && !route.path.startsWith('/messages/')) return
    tab.value = value === 'friends' || value === 'notifications' ? value : 'chats'
    if (value === 'notifications') void notice()
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
watch(chooser, async (value) => {
  await nextTick()
  if (value) chooserDialog.value?.showModal()
  else chooserDialog.value?.close()
})
onMounted(async () => {
  if (auth.isAuthenticated) {
    try {
      await list()
      await refreshNotices()
      try {
        myAvatar.value = (await socialApi.me()).profile.hasAvatar
      } catch {
        /* Avatar is optional. */
      }
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
      <nav class="social-nav" aria-label="消息分类">
        <button
          v-for="t in [
            { id: 'chats', text: '聊天', icon: 'chat' as const },
            { id: 'friends', text: '好友', icon: 'friends' as const },
          ]"
          :key="t.id"
          :aria-pressed="tab === t.id || (t.id === 'chats' && tab === 'notifications')"
          @click="navigateTab(t.id)"
        >
          <SocialIcon :name="t.icon" />{{ t.text }}
        </button>
      </nav>
      <div class="social-content" :class="{ 'chat-layout': tab === 'chats' }">
        <p v-if="social.reconnecting" class="connection-warning" role="status">
          新消息同步暂时中断，正在重试
        </p>
        <aside v-if="tab === 'chats'" class="conversation-pane">
          <header><h1>消息</h1></header>
          <div class="conversation-list">
            <button
              v-if="notificationSummary.latest"
              class="conversation notification-entry"
              @click="navigateTab('notifications')"
            >
              <span class="notice-icon"><SocialIcon name="notice" /></span
              ><span class="conversation-text"
                ><strong>好友通知</strong><small>{{ notificationSummary.latest.text }}</small></span
              ><b v-if="notificationSummary.unreadCount" class="unread">{{
                notificationSummary.unreadCount > 99 ? '99+' : notificationSummary.unreadCount
              }}</b>
            </button>
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
              ><span class="conversation-meta"
                ><time>{{ socialTime(c.updatedAt) }}</time
                ><b v-if="c.unreadCount" class="unread">{{
                  c.unreadCount > 99 ? '99+' : c.unreadCount
                }}</b></span
              >
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
        <SocialPaneDivider
          v-if="tab === 'chats'"
          :storage-key="`passingtrace:chat-list-width:${auth.user?.profile.sub ?? ''}`"
        />
        <KeepAlive><FriendsPanel v-if="tab === 'friends'" /></KeepAlive>
        <section v-if="tab === 'notifications'" class="secondary-pane">
          <header>
            <button class="icon-button" aria-label="返回消息" @click="navigateTab('chats')">
              <SocialIcon name="back" />
            </button>
            <h2>好友通知</h2>
          </header>
          <p v-if="error" role="alert">{{ error }}<button @click="notice()">重试</button></p>
          <p v-if="!noticesLoaded" role="status">正在加载通知…</p>
          <p v-else-if="!notifications.length && !error">暂时没有好友通知</p>
          <RouterLink v-for="n in notifications" :key="n.id" :to="noticeTarget(n)" class="notice"
            >{{ n.text }}<small>{{ new Date(n.createdAt).toLocaleString() }}</small></RouterLink
          ><button v-if="noticesMore" class="text-button" @click="notice(true)">
            查看更早通知
          </button>
        </section>
        <section v-else-if="tab === 'chats' && id" class="chat-pane">
          <header>
            <RouterLink class="chat-back" to="/messages">返回消息</RouterLink>
            <h2>{{ current?.person.nickname || '聊天' }}</h2>
            <details class="more">
              <summary aria-label="更多聊天操作" title="更多"><SocialIcon name="more" /></summary>
              <div><button class="text-button" @click="clear">清除我的聊天记录</button></div>
            </details>
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
            <template v-for="(m, index) in messages" :key="m.id">
              <div
                v-if="showMessageTime(m.createdAt, messages[index - 1]?.createdAt)"
                class="time-divider"
              >
                {{ socialTime(m.createdAt, true) }}
              </div>
              <article class="message" :class="{ own: m.senderId === auth.user?.profile.sub }">
                <PersonAvatar
                  :id="m.senderId"
                  :has-avatar="
                    m.senderId === auth.user?.profile.sub ? myAvatar : !!current?.person.hasAvatar
                  "
                />
                <div class="message-body">
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
                  <small v-if="m.senderId === auth.user?.profile.sub">{{
                    m.id <= (current?.peerReadThroughId ?? 0) ? '已读' : '已发送'
                  }}</small>
                </div>
              </article>
            </template>
          </div>
          <footer>
            <p v-if="error" role="alert">
              {{ error }}
              <button v-if="failed" class="text-button" :disabled="busy" @click="send(true)">
                重试发送</button
              ><button v-else class="text-button" @click="open">重新加载</button>
            </p>
            <p v-if="current?.canSend === false">好友关系已结束，无法继续发送消息。</p>
            <template v-else>
              <form class="chat-composer" @submit.prevent="send()">
                <textarea
                  v-model="draft"
                  aria-label="聊天内容"
                  placeholder="和好友聊聊…"
                  maxlength="8000"
                  rows="4"
                  @keydown="composeKey"
                />
                <div class="composer-toolbar">
                  <div class="composer-tools" aria-label="分享工具">
                    <button
                      type="button"
                      class="composer-tool"
                      title="分享记录"
                      aria-label="分享记录"
                      @click="choose('record')"
                    >
                      <SocialIcon name="record" />
                    </button>
                    <button
                      type="button"
                      class="composer-tool"
                      title="分享故事线"
                      aria-label="分享故事线"
                      @click="choose('storyline')"
                    >
                      <SocialIcon name="storyline" />
                    </button>
                  </div>
                  <span class="compose-hint">Enter 发送，Shift + Enter 换行</span>
                  <button class="send-button" :disabled="busy || !draft.trim()">
                    {{ busy ? '发送中…' : '发送' }}
                  </button>
                </div>
              </form></template
            >
          </footer>
        </section>
        <section v-else-if="tab === 'chats'" class="empty-chat">
          <h2>把共同的经历聊起来</h2>
          <p>选择一位好友，聊聊天，也可以分享记录和故事线。</p>
          <button class="button button-secondary" @click="navigateTab('friends')">查看好友</button>
        </section>
      </div>
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
  grid-template-columns: 4.5rem minmax(0, 1fr);
}
.social-nav {
  flex-direction: column;
  border-right: 1px solid var(--line);
  background: var(--surface-soft);
}
.social-nav button {
  flex: 0 0 auto;
  display: grid;
  justify-items: center;
  gap: 0.4rem;
  padding: 0.75rem 0.25rem;
}
.social-content {
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
  position: relative;
}
.social-content.chat-layout {
  display: grid;
  grid-template-columns: clamp(240px, var(--social-list-width, 300px), 40%) 8px minmax(0, 1fr);
  grid-template-rows: minmax(0, 1fr);
}
.connection-warning {
  position: absolute;
  top: 0;
  right: 0;
  z-index: 4;
  margin: 0;
  padding: 0.75rem;
  background: var(--surface);
  border: 1px solid var(--line);
  color: var(--ink-secondary);
}
.notification-entry {
  width: 100%;
  text-align: left;
  border: 0;
  border-bottom: 1px solid var(--line);
  background: none;
  cursor: pointer;
}
.notice-icon {
  padding: 0.65rem;
  border-radius: 0.75rem;
  background: var(--primary-soft);
  color: var(--primary-strong);
}
.conversation-meta {
  display: grid;
  justify-items: end;
  gap: 0.6rem;
  font-size: 0.75rem;
  color: var(--ink-secondary);
}
.conversation-meta time {
  white-space: nowrap;
}
.conversation-pane {
  display: flex;
  flex-direction: column;
  min-height: 0;
  background: var(--surface-soft);
}
.messages-workspace header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.85rem 1.25rem;
  min-height: 65px;
  border-bottom: 1px solid var(--line);
}
h1,
h2 {
  margin: 0;
  font-size: 1.15rem;
}
nav {
  display: flex;
  gap: 0.5rem;
  padding: 0.5rem;
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
  padding: 0.5rem;
  overscroll-behavior: contain;
}
.conversation {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem;
  border-radius: 0.5rem;
  font-size: 0.9rem;
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
.conversation-text strong {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
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
  min-height: 0;
  overflow: auto;
  overscroll-behavior: contain;
  padding: 1.5rem clamp(1rem, 2vw, 2rem);
}
.message {
  display: flex;
  align-items: flex-start;
  flex-direction: row;
  gap: 0.75rem;
  margin-bottom: 1.1rem;
  content-visibility: auto;
  contain-intrinsic-size: auto 110px;
}
.message.own {
  flex-direction: row-reverse;
}
.message-body {
  min-width: 0;
  max-width: min(80%, 42rem);
  display: flex;
  flex-direction: column;
  align-items: flex-start;
}
.own .message-body {
  align-items: flex-end;
}
.time-divider {
  text-align: center;
  font-size: 0.8rem;
  color: var(--ink-secondary);
  margin: 1rem 0;
}
.bubble {
  margin: 0 0 0.4rem;
  padding: 0.7rem 1rem;
  max-width: 100%;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  border: 1px solid var(--line);
  border-radius: 0.65rem;
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
  padding: 0.5rem 1.25rem 1rem;
  background: transparent;
  flex-shrink: 0;
}
.chat-composer {
  display: flex;
  flex-direction: column;
  min-height: 150px;
  height: clamp(150px, 20vh, 200px);
  padding: 0.75rem;
  border: 1px solid var(--line);
  border-radius: 1rem;
  background: var(--surface);
}
.chat-composer textarea {
  flex: 1;
  min-width: 0;
  width: 100%;
  min-height: 60px;
  padding: 0.5rem 0.25rem;
  border: 0;
  border-radius: 0;
  background: transparent;
  color: var(--ink);
  resize: none;
  font: inherit;
  line-height: 1.6;
}
.chat-composer textarea:focus,
.chat-composer textarea:focus-visible {
  outline: none;
  box-shadow: none;
}
.composer-toolbar,
.composer-tools {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}
.composer-toolbar {
  padding-top: 0.5rem;
}
.composer-tools {
  gap: 0.25rem;
  margin-right: auto;
}
.composer-tool {
  display: grid;
  place-items: center;
  width: 36px;
  height: 36px;
  padding: 0;
  border: 0;
  border-radius: 0.4rem;
  background: transparent;
  color: var(--ink-secondary);
  cursor: pointer;
}
.composer-tool:hover {
  background: var(--primary-soft);
  color: var(--primary-strong);
}
.compose-hint {
  font-size: 0.75rem;
  color: var(--ink-secondary);
}
.send-button {
  min-width: 76px;
  min-height: 34px;
  padding: 0.4rem 1rem;
  border: 0;
  border-radius: 0.4rem;
  background: var(--primary);
  color: var(--on-primary);
  font: inherit;
  font-size: 0.875rem;
  cursor: pointer;
}
.send-button:disabled {
  background: var(--surface-soft);
  color: var(--ink-secondary);
  opacity: 0.7;
  cursor: default;
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
.icon-button,
.more summary {
  min-width: 36px;
  min-height: 36px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  color: var(--ink);
  background: transparent;
  border: 0;
  border-radius: 0.4rem;
}
.more {
  position: relative;
}
.more summary {
  list-style: none;
}
.more > div {
  position: absolute;
  z-index: 5;
  right: 0;
  top: 100%;
  min-width: 11rem;
  background: var(--surface);
  border: 1px solid var(--line);
  padding: 0.75rem;
  border-radius: 0.75rem;
  display: grid;
  gap: 0.75rem;
}
.attachments > div {
  top: auto;
  bottom: calc(100% + 0.5rem);
  left: 0;
  right: auto;
}
button:focus-visible,
summary:focus-visible {
  outline: 2px solid var(--primary);
  outline-offset: 3px;
}
@media (max-width: 760px) {
  .messages-workspace {
    grid-template-columns: 1fr;
    grid-template-rows: auto minmax(0, 1fr);
  }
  .social-nav {
    flex-direction: row;
    padding: 0.4rem;
    border-right: 0;
    border-bottom: 1px solid var(--line);
  }
  .social-nav button {
    flex: 1;
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .social-content.chat-layout {
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
    background: var(--canvas);
  }
  .bubble {
    max-width: 92%;
  }
  footer {
    padding: 0.75rem;
  }
  .chat-composer {
    height: 150px;
    min-height: 140px;
  }
  .compose-hint {
    display: none;
  }
  .composer-tool,
  .send-button {
    min-height: 44px;
    min-width: 44px;
  }
}
</style>
