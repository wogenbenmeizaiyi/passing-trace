<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useSocialStore } from '@/stores/social'
import { socialApi, type Friend, type FriendRequest } from '@/api/social'
import PersonAvatar from './PersonAvatar.vue'
import FriendCodeCard from './FriendCodeCard.vue'
import SocialIcon from './SocialIcon.vue'
import SocialPaneDivider from './SocialPaneDivider.vue'
import { friendGroups } from '@/utils/social-presentation'
const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const updates = useSocialStore()
const friends = ref<Friend[]>([])
const requests = ref<FriendRequest[]>([])
const blocked = ref<string[]>([])
const blockedNames = ref<Record<string, string>>({})
const code = ref(typeof route.query.code === 'string' ? route.query.code : '')
const search = ref('')
const error = ref('')
const notice = ref('')
const busy = ref(false)
const loaded = ref(false)
const selected = ref('')
const remark = ref('')
const label = ref('朋友')
const proposed = ref('亲密朋友')
const editing = ref<'preference' | 'relationship' | ''>('')
const editor = ref<HTMLElement>()
const preferenceButton = ref<HTMLButtonElement>()
const relationshipButton = ref<HTMLButtonElement>()
async function edit(mode: 'preference' | 'relationship') {
  remark.value = active.value?.remark || ''
  label.value = active.value?.label || '朋友'
  editing.value = mode
  await nextTick()
  editor.value?.querySelector<HTMLInputElement | HTMLSelectElement>('input, select')?.focus()
}
async function closeEditor() {
  const mode = editing.value
  editing.value = ''
  await nextTick()
  ;(mode === 'preference' ? preferenceButton.value : relationshipButton.value)?.focus()
}
function closeMenu(event: MouseEvent) {
  const menu = (event.currentTarget as HTMLElement).closest('details')
  if ((event.target as HTMLElement).closest('button') && menu) menu.open = false
}
async function savePreference() {
  if (!active.value) return
  await act(
    () => socialApi.preference(active.value!.id, remark.value, label.value),
    '备注和标签已保存',
  )
  if (!error.value) await closeEditor()
}
const panel = computed(() =>
  typeof route.query.panel === 'string' ? route.query.panel : route.query.code ? 'add' : '',
)
const collapseKey = computed(() => `passingtrace:friend-groups:${auth.user?.profile.sub ?? ''}`)
const collapsed = ref<string[]>([])
const visibleCounts = ref<Record<string, number>>({})
try {
  const stored: unknown = JSON.parse(localStorage.getItem(collapseKey.value) || '[]')
  if (Array.isArray(stored))
    collapsed.value = stored.filter((value): value is string => typeof value === 'string')
} catch {
  /* Optional preference. */
}
const groups = computed(() => friendGroups(friends.value, search.value))
const pendingCount = computed(() => requests.value.filter((r) => r.direction === 'received').length)
function navigate(panelName = '', friend?: string) {
  return router.push({
    path: '/messages',
    query: {
      tab: 'friends',
      ...(panelName ? { panel: panelName } : {}),
      ...(friend ? { friend } : {}),
    },
  })
}
function toggleGroup(label: string) {
  collapsed.value = collapsed.value.includes(label)
    ? collapsed.value.filter((x) => x !== label)
    : [...collapsed.value, label]
  try {
    localStorage.setItem(collapseKey.value, JSON.stringify(collapsed.value))
  } catch {
    /* Storage may be disabled. */
  }
}
const active = computed(() => friends.value.find((f) => f.id === selected.value))
async function load() {
  try {
    ;[friends.value, requests.value, blocked.value] = await Promise.all([
      socialApi.friends(),
      socialApi.requests(),
      socialApi.blocks(),
    ])
    const names: Record<string, string> = {}
    for (let at = 0; at < blocked.value.length; at += 100) {
      for (const person of await socialApi.profiles(blocked.value.slice(at, at + 100)))
        names[person.id] = person.nickname
    }
    blockedNames.value = names
  } catch {
    error.value = '好友列表暂时无法加载，请重试。'
  } finally {
    loaded.value = true
  }
}
async function act(action: () => Promise<unknown>, success = '已更新') {
  if (busy.value) return
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    await action()
    await load()
    notice.value = success
  } catch (e) {
    error.value = e instanceof Error ? e.message : '操作未完成，请重试。'
  } finally {
    busy.value = false
  }
}
function select(f: Friend) {
  selected.value = f.id
  editing.value = ''
  remark.value = f.remark
  label.value = f.label
  void navigate('', f.id)
}
async function chat(f: Friend) {
  await act(async () => {
    const c = await socialApi.open(f.id)
    await router.push(`/messages/${c.id}`)
  })
}
function remove(f: Friend, block: boolean) {
  if (
    window.confirm(
      block
        ? '拉黑后将停止消息和共同内容访问。确定拉黑？'
        : '删除好友会停止双方分享和共同记录的访问。确定删除？',
    )
  )
    void act(() => socialApi.remove(f.id, block))
}
watch(() => updates.change, load)
watch(
  () => route.query.friend,
  (value) => {
    selected.value = typeof value === 'string' ? value : ''
    editing.value = ''
    const friend = active.value
    if (friend) {
      remark.value = friend.remark
      label.value = friend.label
    }
  },
)
watch(panel, () => {
  error.value = ''
  notice.value = ''
})
onMounted(async () => {
  await load()
  if (typeof route.query.friend === 'string') {
    const f = friends.value.find((x) => x.id === route.query.friend)
    if (f) select(f)
  }
})
</script>
<template>
  <section class="friends-panel" :class="{ 'has-detail': active || panel }">
    <aside class="contacts-pane" aria-label="联系人">
      <header class="contacts-header">
        <h2>好友</h2>
        <div class="heading">
          <button
            class="icon-button"
            title="添加好友"
            aria-label="添加好友"
            @click="navigate('add')"
          >
            <SocialIcon name="add" /><span class="tooltip">添加好友</span>
          </button>
          <details class="more" @click="closeMenu">
            <summary title="更多" aria-label="更多好友操作"><SocialIcon name="more" /></summary>
            <div>
              <button @click="load">刷新好友</button
              ><button @click="navigate('blocks')">已拉黑的用户</button>
            </div>
          </details>
        </div>
      </header>
      <label class="search"
        ><span class="sr-only">查找我的好友</span>
        <input v-model="search" placeholder="搜索昵称、备注或关系" type="search" />
      </label>
      <div class="contacts-scroll">
        <p v-if="error && !active && !panel" role="alert" class="list-hint">
          {{ error }}<button class="text-button" @click="load">重试</button>
        </p>
        <button
          class="request-entry"
          :class="{ selected: panel === 'requests' }"
          @click="navigate('requests')"
        >
          <span class="request-icon"><SocialIcon name="friends" /></span><strong>新的好友</strong>
          <span v-if="pendingCount" class="count">{{ pendingCount }}</span
          ><SocialIcon name="chevron" />
        </button>
        <p v-if="!loaded" role="status" class="list-hint">正在加载好友…</p>
        <div v-else-if="!friends.length && !error" class="empty">
          <p>还没有好友</p>
          <button class="text-button" @click="navigate('add')">添加第一位好友</button>
        </div>
        <div v-else-if="!groups.length" class="empty">
          <p>没有找到匹配的好友</p>
          <button class="text-button" @click="search = ''">清空搜索</button>
        </div>
        <section v-for="group in groups" :key="group.label" class="friend-group">
          <button
            class="group-heading"
            :aria-expanded="!!search.trim() || !collapsed.includes(group.label)"
            @click="toggleGroup(group.label)"
          >
            <SocialIcon
              name="chevron"
              class="group-arrow"
              :class="{ expanded: search.trim() || !collapsed.includes(group.label) }"
            />
            <span>{{ group.label }}</span
            ><small>{{ group.items.length }}</small>
          </button>
          <template v-if="search.trim() || !collapsed.includes(group.label)">
            <button
              v-for="f in group.items.slice(0, visibleCounts[group.label] || 50)"
              :key="f.id"
              class="friend-row friend-select"
              :class="{ selected: selected === f.id && !panel }"
              :aria-pressed="selected === f.id && !panel"
              @click="select(f)"
            >
              <PersonAvatar :id="f.person.id" :has-avatar="f.person.hasAvatar" />
              <span class="friend-name"
                ><strong>{{ f.remark || f.person.nickname }}</strong>
                <small v-if="f.relationship">双方确认 · {{ f.relationship }}</small>
                <small v-else-if="f.remark">{{ f.person.nickname }}</small>
              </span>
            </button>
            <button
              v-if="group.items.length > (visibleCounts[group.label] || 50)"
              class="text-button load-more"
              @click="visibleCounts[group.label] = (visibleCounts[group.label] || 50) + 50"
            >
              显示更多好友
            </button>
          </template>
        </section>
      </div>
    </aside>
    <SocialPaneDivider
      :storage-key="`passingtrace:contacts-width:${auth.user?.profile.sub ?? ''}`"
    />
    <section class="contact-workspace" :aria-label="panel ? '好友管理' : '好友资料'">
      <header v-if="panel || active" class="detail-header">
        <button class="icon-button" title="返回好友" aria-label="返回好友" @click="navigate()">
          <SocialIcon name="back" />
        </button>
        <h2>
          {{
            panel === 'add'
              ? '添加好友'
              : panel === 'requests'
                ? '新的好友'
                : panel === 'blocks'
                  ? '已拉黑的用户'
                  : '好友资料'
          }}
        </h2>
      </header>
      <div class="detail-scroll">
        <div class="detail-content">
          <p v-if="error" role="alert">{{ error }}</p>
          <p v-if="notice" role="status" class="feedback">{{ notice }}</p>
          <template v-if="panel === 'add'">
            <h3>通过好友码找到朋友</h3>
            <p>输入对方的好友码，发送一份好友申请。</p>
            <form
              class="add-friend"
              @submit.prevent="
                act(() => socialApi.request(code.trim()), '好友申请已发送，等待对方确认。')
              "
            >
              <label>好友码<input v-model="code" placeholder="输入好友码" maxlength="100" /></label>
              <button class="compact-primary" :disabled="busy || !code.trim()">
                {{ busy ? '正在发送…' : '发送申请' }}
              </button>
            </form>
            <FriendCodeCard />
          </template>
          <section v-else-if="panel === 'requests'">
            <p v-if="!requests.length">暂时没有待处理的好友申请。</p>
            <div v-for="r in requests" :key="r.id" class="request-row">
              <PersonAvatar :id="r.person.id" :has-avatar="r.person.hasAvatar" /><strong>{{
                r.person.nickname
              }}</strong>
              <template v-if="r.direction === 'received'">
                <button
                  class="text-button"
                  :disabled="busy"
                  @click="act(() => socialApi.decide(r.id, 'accept'))"
                >
                  接受
                </button>
                <button
                  class="text-button muted"
                  :disabled="busy"
                  @click="act(() => socialApi.decide(r.id, 'reject'))"
                >
                  拒绝
                </button>
              </template>
              <button
                v-else
                class="text-button"
                :disabled="busy"
                @click="act(() => socialApi.decide(r.id, 'withdraw'))"
              >
                撤回申请
              </button>
            </div>
          </section>
          <section v-else-if="panel === 'blocks'">
            <p v-if="!blocked.length">没有拉黑的用户。</p>
            <div v-for="id in blocked" :key="id" class="request-row">
              <span>{{ blockedNames[id] || '账号资料暂不可用' }}</span>
              <button
                class="text-button"
                :disabled="busy"
                @click="act(() => socialApi.unblock(id))"
              >
                解除拉黑
              </button>
            </div>
          </section>
          <article v-else-if="active" class="friend-detail">
            <div class="profile-heading">
              <PersonAvatar
                class="profile-avatar"
                :size="64"
                :id="active.person.id"
                :has-avatar="active.person.hasAvatar"
              />
              <div class="profile-name">
                <h3>{{ active.remark || active.person.nickname }}</h3>
                <p v-if="active.remark">昵称：{{ active.person.nickname }}</p>
                <p class="bio">{{ active.person.bio || '还没有填写简介' }}</p>
              </div>
              <details class="more profile-more" @click="closeMenu">
                <summary title="更多" aria-label="更多资料操作"><SocialIcon name="more" /></summary>
                <div>
                  <button :disabled="busy" @click="edit('preference')">编辑备注与标签</button>
                  <button :disabled="busy" @click="edit('relationship')">设置双方关系</button>
                  <button class="danger" :disabled="busy" @click="remove(active, false)">
                    删除好友
                  </button>
                  <button class="danger" :disabled="busy" @click="remove(active, true)">
                    拉黑
                  </button>
                </div>
              </details>
            </div>
            <section class="profile-section">
              <div class="section-heading">
                <h4>我的设置</h4>
                <button
                  v-if="editing !== 'preference'"
                  ref="preferenceButton"
                  class="text-button"
                  @click="edit('preference')"
                >
                  编辑
                </button>
              </div>
              <form
                v-if="editing === 'preference'"
                ref="editor"
                class="preference-form"
                @submit.prevent="savePreference"
              >
                <label>我的备注<input v-model="remark" maxlength="100" /></label>
                <label
                  >我的关系标签<input v-model="label" list="friend-labels" maxlength="30"
                /></label>
                <datalist id="friend-labels">
                  <option
                    v-for="value in ['朋友', '亲密朋友', '恋人', '家人', '同事', '同学']"
                    :key="value"
                    :value="value"
                  />
                </datalist>
                <div class="form-actions">
                  <button type="button" class="text-button muted" @click="closeEditor">取消</button>
                  <button class="compact-primary" :disabled="busy">
                    {{ busy ? '保存中…' : '保存' }}
                  </button>
                </div>
              </form>
              <dl v-else class="profile-facts">
                <div>
                  <dt>备注</dt>
                  <dd>{{ active.remark || '未设置' }}</dd>
                </div>
                <div>
                  <dt>关系标签</dt>
                  <dd>{{ active.label || '朋友' }}</dd>
                </div>
              </dl>
              <small>仅你和你的 AI 可见</small>
            </section>
            <section class="profile-section">
              <div class="section-heading">
                <h4>双方关系</h4>
                <button
                  v-if="editing !== 'relationship'"
                  ref="relationshipButton"
                  class="text-button"
                  @click="edit('relationship')"
                >
                  管理
                </button>
              </div>
              <p class="relationship-value">{{ active.relationship || '尚未设置' }}</p>
              <div v-if="active.proposedRelationship" class="pending-relationship">
                <p>待确认：{{ active.proposedRelationship }}</p>
                <template v-if="active.relationshipRequestedBy !== auth.user?.profile.sub">
                  <button
                    class="text-button"
                    :disabled="busy"
                    @click="act(() => socialApi.relationshipDecision(active!, 'accept'))"
                  >
                    确认关系
                  </button>
                  <button
                    class="text-button muted"
                    :disabled="busy"
                    @click="act(() => socialApi.relationshipDecision(active!, 'reject'))"
                  >
                    拒绝
                  </button> </template
                ><small v-else>等待对方确认</small>
              </div>
              <div v-if="editing === 'relationship'" ref="editor" class="relationship-form">
                <label
                  >申请关系<select v-model="proposed">
                    <option>亲密朋友</option>
                    <option>恋人</option>
                    <option>家人</option>
                  </select></label
                >
                <small>对方确认后生效，仅你们双方可见。</small>
                <div class="form-actions">
                  <button
                    v-if="active.relationship || active.proposedRelationship"
                    class="text-button danger"
                    :disabled="busy"
                    @click="act(() => socialApi.relationshipDecision(active!, 'clear'))"
                  >
                    解除／取消关系
                  </button>
                  <button class="text-button muted" @click="closeEditor">收起</button>
                  <button
                    class="compact-primary"
                    :disabled="busy"
                    @click="act(() => socialApi.relationship(active!, proposed))"
                  >
                    请对方确认
                  </button>
                </div>
              </div>
            </section>
            <footer class="profile-actions">
              <button class="compact-primary" :disabled="busy" @click="chat(active)">
                <SocialIcon name="chat" />发消息
              </button>
            </footer>
          </article>
          <div v-else class="empty-profile">
            <SocialIcon name="friends" />
            <h3>和朋友，一起记录</h3>
            <p>从左侧选择一位好友，查看资料或开始聊天。</p>
          </div>
        </div>
      </div>
    </section>
  </section>
</template>
<style scoped>
.friends-panel {
  flex: 1;
  min-height: 0;
  min-width: 0;
  display: grid;
  grid-template-columns: clamp(240px, var(--social-list-width, 300px), 40%) 8px minmax(0, 1fr);
}
.contacts-pane {
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
  background: var(--surface-soft);
}
.contacts-header,
.heading,
.section-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
}
.contacts-header {
  padding: 1rem 1.25rem 0.5rem;
}
h2 {
  font-size: 1.15rem;
  margin: 0;
}
h3 {
  margin: 0;
  font-size: 1.35rem;
  line-height: 1.5;
}
h4 {
  font-size: 0.9rem;
  margin: 0;
  color: var(--ink-secondary);
  font-weight: 500;
}
.search {
  margin: 0.5rem 1rem 0.75rem;
}
input,
select {
  width: 100%;
  min-width: 0;
  padding: 0.6rem 0.75rem;
  border: 1px solid var(--line);
  border-radius: 0.5rem;
  background: var(--surface);
  color: var(--ink);
  font: inherit;
}
.search input {
  font-size: 0.875rem;
}
.contacts-scroll,
.detail-scroll {
  min-height: 0;
  overflow: auto;
  overscroll-behavior: contain;
}
.contacts-scroll {
  flex: 1;
  padding: 0 0.5rem 1rem;
}
.request-entry,
.friend-select,
.group-heading {
  width: 100%;
  border: 0;
  background: transparent;
  color: var(--ink);
  cursor: pointer;
  text-align: left;
  display: flex;
  align-items: center;
}
.request-entry {
  gap: 0.75rem;
  padding: 0.75rem;
  min-height: 64px;
  border-bottom: 1px solid var(--line);
}
.request-entry strong {
  flex: 1;
  font-size: 0.9rem;
}
.request-entry > svg {
  width: 16px;
  color: var(--ink-secondary);
}
.request-icon {
  display: grid;
  place-items: center;
  width: 38px;
  height: 38px;
  border-radius: 0.6rem;
  background: var(--primary-soft);
  color: var(--primary-strong);
}
.count {
  background: var(--primary);
  color: var(--on-primary);
  padding: 0.1rem 0.4rem;
  border-radius: 1rem;
  font-size: 0.75rem;
}
.group-heading {
  gap: 0.35rem;
  padding: 0.4rem 0.6rem;
  min-height: 38px;
  margin-top: 0.65rem;
  color: var(--ink-secondary);
  font-size: 0.8rem;
}
.group-heading small {
  margin-left: auto;
}
.group-arrow {
  width: 14px;
  height: 14px;
}
.group-arrow.expanded {
  transform: rotate(90deg);
}
.friend-select {
  padding: 0.65rem 0.75rem;
  gap: 0.75rem;
  min-height: 64px;
  border-radius: 0.5rem;
  content-visibility: auto;
  contain-intrinsic-size: auto 68px;
}
.friend-name {
  min-width: 0;
}
.friend-name strong {
  display: block;
  font-size: 0.9rem;
  overflow-wrap: anywhere;
}
.friend-name small {
  display: block;
  margin-top: 0.2rem;
  font-size: 0.75rem;
}
.selected,
.friend-select:hover,
.request-entry:hover {
  background: var(--primary-soft);
}
.contact-workspace {
  min-width: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.detail-header {
  padding: 0.7rem 1.5rem;
  min-height: 65px;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  border-bottom: 1px solid var(--line);
}
.detail-header .icon-button {
  display: none;
}
.detail-scroll {
  flex: 1;
}
.detail-content {
  width: min(100%, 600px);
  margin: 0 auto;
  padding: clamp(1.5rem, 4vw, 4rem) 2rem;
}
.profile-heading {
  display: flex;
  align-items: flex-start;
  gap: 1.25rem;
  padding-bottom: 1.75rem;
}
.profile-avatar {
  width: 64px;
  height: 64px;
  flex: 0 0 64px;
}
.profile-name {
  flex: 1;
  min-width: 0;
}
.profile-name p {
  margin: 0.35rem 0 0;
  font-size: 0.875rem;
  line-height: 1.6;
}
.profile-more {
  margin-left: auto;
}
.profile-section {
  border-top: 1px solid var(--line);
  padding: 1.25rem 0;
}
.section-heading {
  margin-bottom: 0.6rem;
  min-height: 32px;
}
.profile-facts {
  margin: 0.5rem 0 1rem;
}
.profile-facts > div {
  display: grid;
  grid-template-columns: 6rem minmax(0, 1fr);
  gap: 1rem;
  margin-block: 0.8rem;
  font-size: 0.9rem;
}
dt {
  color: var(--ink-secondary);
}
dd {
  margin: 0;
  overflow-wrap: anywhere;
}
.relationship-value {
  margin: 0.4rem 0;
  color: var(--ink);
  font-size: 0.95rem;
}
label {
  display: grid;
  gap: 0.5rem;
  font-size: 0.875rem;
}
.preference-form,
.relationship-form {
  display: grid;
  gap: 1rem;
  margin-block: 0.75rem;
}
.form-actions {
  display: flex;
  gap: 0.75rem;
  justify-content: flex-end;
  flex-wrap: wrap;
  align-items: center;
}
.profile-actions {
  border-top: 1px solid var(--line);
  padding-top: 1.5rem;
  display: flex;
  justify-content: center;
}
button.compact-primary {
  padding: 0.5rem 1.1rem;
  min-height: 36px;
  display: inline-flex;
  justify-content: center;
  align-items: center;
  gap: 0.5rem;
  font: inherit;
  font-size: 0.875rem;
  font-weight: 600;
  background: var(--primary);
  color: var(--on-primary);
  border: 0;
  border-radius: 0.45rem;
  cursor: pointer;
  white-space: nowrap;
}
.profile-actions button {
  min-width: 9rem;
}
.text-button {
  border: 0;
  padding: 0.3rem 0.4rem;
  background: transparent;
  color: var(--primary-strong);
  font: inherit;
  font-size: 0.875rem;
  min-height: 32px;
  cursor: pointer;
}
.text-button:hover {
  background: var(--primary-soft);
  border-radius: 0.35rem;
}
.text-button.muted {
  color: var(--ink-secondary);
}
.danger,
.text-button.danger {
  color: var(--danger);
}
.icon-button,
.more summary {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border: 0;
  border-radius: 0.4rem;
  background: transparent;
  color: var(--ink-secondary);
  cursor: pointer;
  position: relative;
  list-style: none;
}
.icon-button:hover,
.more summary:hover {
  background: var(--primary-soft);
  color: var(--primary-strong);
}
.more {
  position: relative;
  flex-shrink: 0;
}
.more > div {
  position: absolute;
  right: 0;
  top: calc(100% + 0.25rem);
  z-index: 5;
  width: 11rem;
  padding: 0.4rem;
  background: var(--surface);
  border: 1px solid var(--line);
  border-radius: 0.6rem;
  box-shadow: 0 6px 20px rgb(0 0 0 / 0.08);
  display: grid;
}
.more button {
  padding: 0.6rem 0.75rem;
  border: 0;
  background: transparent;
  text-align: left;
  color: var(--ink);
  cursor: pointer;
  border-radius: 0.3rem;
  font: inherit;
  font-size: 0.85rem;
}
.more button.danger {
  color: var(--danger);
}
.more button:hover {
  background: var(--surface-soft);
}
.tooltip {
  display: none;
  position: absolute;
  top: 100%;
  right: 0;
  z-index: 5;
  white-space: nowrap;
  padding: 0.4rem;
  font-size: 0.8rem;
  border: 1px solid var(--line);
  background: var(--surface);
}
.icon-button:hover .tooltip,
.icon-button:focus-visible .tooltip {
  display: block;
}
.add-friend {
  display: flex;
  gap: 0.75rem;
  align-items: end;
  margin-block: 1.5rem 2rem;
}
.add-friend label {
  flex: 1;
  min-width: 0;
}
.add-friend button {
  min-height: 42px;
}
.request-row {
  display: flex;
  align-items: center;
  gap: 0.65rem;
  padding: 1rem 0;
  border-bottom: 1px solid var(--line);
  flex-wrap: wrap;
}
.request-row strong,
.request-row > span {
  flex: 1;
}
.empty {
  padding: 1rem;
  text-align: center;
  font-size: 0.875rem;
}
.empty-profile {
  padding-block: 12vh;
  text-align: center;
}
.empty-profile > svg {
  width: 48px;
  height: 48px;
  color: var(--primary-strong);
  opacity: 0.6;
  margin-bottom: 1rem;
}
.empty-profile h3 {
  font-size: 1.15rem;
}
.empty-profile p {
  font-size: 0.9rem;
}
small,
p {
  color: var(--ink-secondary);
  overflow-wrap: anywhere;
}
small {
  font-size: 0.8rem;
}
.list-hint {
  padding: 1rem;
}
.load-more {
  margin: 0.5rem;
}
.feedback {
  color: var(--primary-strong);
  font-size: 0.875rem;
}
[role='alert'] {
  color: var(--danger);
}
button:disabled {
  opacity: 0.55;
  cursor: default;
}
button:focus-visible,
summary:focus-visible,
input:focus-visible,
select:focus-visible {
  outline: 2px solid var(--primary);
  outline-offset: 2px;
}
.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  overflow: hidden;
  clip-path: inset(50%);
  white-space: nowrap;
}
@media (max-width: 760px) {
  .friends-panel {
    grid-template-columns: minmax(0, 1fr);
  }
  .contact-workspace {
    display: none;
  }
  .has-detail .contacts-pane {
    display: none;
  }
  .has-detail .contact-workspace {
    display: flex;
  }
  .detail-header .icon-button {
    display: inline-flex;
  }
  .detail-header {
    padding-inline: 0.75rem;
  }
  .detail-content {
    padding: 1.5rem 1rem;
  }
  .profile-heading {
    gap: 0.75rem;
  }
  .profile-avatar {
    flex-basis: 64px;
  }
  .icon-button,
  .more summary {
    width: 44px;
    height: 44px;
  }
  .text-button,
  button.compact-primary {
    min-height: 44px;
  }
  .add-friend {
    flex-wrap: wrap;
  }
  .add-friend label {
    flex-basis: 100%;
  }
}
</style>
