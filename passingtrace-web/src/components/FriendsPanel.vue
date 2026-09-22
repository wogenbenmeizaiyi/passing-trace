<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useSocialStore } from '@/stores/social'
import { socialApi, type Friend, type FriendRequest } from '@/api/social'
import PersonAvatar from './PersonAvatar.vue'
import FriendCodeCard from './FriendCodeCard.vue'
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
const selected = ref('')
const remark = ref('')
const label = ref('朋友')
const proposed = ref('亲密朋友')
const active = computed(() => friends.value.find((f) => f.id === selected.value))
const matches = computed(() =>
  friends.value.filter((f) => `${f.person.nickname} ${f.remark} ${f.label}`.includes(search.value)),
)
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
  }
}
async function act(action: () => Promise<unknown>) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    await action()
    await load()
    notice.value = '已更新'
  } catch (e) {
    error.value = e instanceof Error ? e.message : '操作未完成，请重试。'
  } finally {
    busy.value = false
  }
}
function select(f: Friend) {
  selected.value = f.id
  remark.value = f.remark
  label.value = f.label
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
onMounted(async () => {
  await load()
  if (typeof route.query.friend === 'string') {
    const f = friends.value.find((x) => x.id === route.query.friend)
    if (f) select(f)
  }
})
</script>
<template>
  <section class="friends-panel">
    <header>
      <h2>好友</h2>
      <button class="text-button" @click="load">刷新</button>
    </header>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="notice" role="status">{{ notice }}</p>
    <form class="add-friend" @submit.prevent="act(() => socialApi.request(code))">
      <label>添加好友<input v-model="code" placeholder="输入好友码" maxlength="100" /></label
      ><button class="button button-primary" :disabled="busy || !code.trim()">发送申请</button>
    </form>
    <FriendCodeCard />
    <section v-if="requests.length">
      <h3>好友申请</h3>
      <div v-for="r in requests" :key="r.id" class="friend-row">
        <PersonAvatar :id="r.person.id" :has-avatar="r.person.hasAvatar" /><strong>{{
          r.person.nickname
        }}</strong
        ><template v-if="r.direction === 'received'"
          ><button :disabled="busy" @click="act(() => socialApi.decide(r.id, 'accept'))">
            接受</button
          ><button :disabled="busy" @click="act(() => socialApi.decide(r.id, 'reject'))">
            拒绝
          </button></template
        ><button v-else :disabled="busy" @click="act(() => socialApi.decide(r.id, 'withdraw'))">
          撤回申请
        </button>
      </div>
    </section>
    <label>查找我的好友<input v-model="search" placeholder="昵称、备注或关系" /></label>
    <p v-if="!matches.length">还没有匹配的好友。</p>
    <div class="friends-layout">
      <div>
        <button
          v-for="f in matches"
          :key="f.id"
          class="friend-row friend-select"
          :class="{ selected: selected === f.id }"
          @click="select(f)"
        >
          <PersonAvatar :id="f.person.id" :has-avatar="f.person.hasAvatar" /><span
            ><strong>{{ f.remark || f.person.nickname }}</strong
            ><small
              >{{ f.label
              }}<template v-if="f.relationship"> · 双方确认：{{ f.relationship }}</template></small
            ></span
          >
        </button>
      </div>
      <section v-if="active" class="friend-detail">
        <h3>{{ active.person.nickname }}</h3>
        <p>{{ active.person.bio || '还没有填写简介' }}</p>
        <button class="button button-primary" :disabled="busy" @click="chat(active)">发消息</button>
        <form @submit.prevent="act(() => socialApi.preference(active!.id, remark, label))">
          <label>我的备注<input v-model="remark" maxlength="100" /></label
          ><label>我的关系标签<input v-model="label" list="friend-labels" maxlength="30" /></label
          ><datalist id="friend-labels">
            <option
              v-for="value in ['朋友', '亲密朋友', '恋人', '家人', '同事', '同学']"
              :key="value"
              :value="value"
            /></datalist
          ><small>仅你和你的 AI 可见。</small
          ><button class="button button-secondary" :disabled="busy">保存</button>
        </form>
        <h4>双方关系</h4>
        <p>{{ active.relationship ? `已确认：${active.relationship}` : '尚未设置' }}</p>
        <div v-if="active.proposedRelationship">
          <p>待确认：{{ active.proposedRelationship }}</p>
          <template v-if="active.relationshipRequestedBy !== auth.user?.profile.sub"
            ><button
              :disabled="busy"
              @click="act(() => socialApi.relationshipDecision(active!, 'accept'))"
            >
              确认关系</button
            ><button
              :disabled="busy"
              @click="act(() => socialApi.relationshipDecision(active!, 'reject'))"
            >
              拒绝
            </button></template
          >
          <p v-else>等待对方确认</p>
        </div>
        <label
          >申请关系<select v-model="proposed">
            <option>亲密朋友</option>
            <option>恋人</option>
            <option>家人</option>
          </select></label
        ><button :disabled="busy" @click="act(() => socialApi.relationship(active!, proposed))">
          请对方确认</button
        ><button
          v-if="active.relationship || active.proposedRelationship"
          :disabled="busy"
          @click="act(() => socialApi.relationshipDecision(active!, 'clear'))"
        >
          解除／取消关系
        </button>
        <footer>
          <button :disabled="busy" @click="remove(active, false)">删除好友</button
          ><button :disabled="busy" @click="remove(active, true)">拉黑</button>
        </footer>
      </section>
    </div>
    <details v-if="blocked.length">
      <summary>已拉黑的用户</summary>
      <div v-for="id in blocked" :key="id" class="friend-row">
        <span>{{ blockedNames[id] || '账号资料暂不可用' }}</span
        ><button :disabled="busy" @click="act(() => socialApi.unblock(id))">解除拉黑</button>
      </div>
    </details>
  </section>
</template>
<style scoped>
.friends-panel {
  padding: clamp(1rem, 3vw, 2.5rem);
  overflow: auto;
  min-height: 0;
}
header,
.friend-row,
.add-friend {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}
header {
  justify-content: space-between;
}
.add-friend > label,
.friend-row > strong {
  flex: 1;
  min-width: 0;
}
label {
  display: grid;
  gap: 0.5rem;
  margin-block: 1rem;
}
input,
select {
  min-width: 0;
  width: 100%;
  border: 1px solid var(--line);
  color: var(--ink);
  background: var(--surface);
  padding: 0.75rem;
  border-radius: 0.7rem;
}
.friends-layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 1.5rem;
}
.friend-row {
  width: 100%;
  padding: 0.8rem;
  border-bottom: 1px solid var(--line);
}
.friend-select {
  cursor: pointer;
  text-align: left;
  background: none;
  border: 0;
  border-radius: 0.75rem;
  color: var(--ink);
  min-height: 64px;
}
.friend-select.selected {
  background: var(--primary-soft);
}
.friend-row small {
  display: block;
  margin-top: 0.3rem;
  color: var(--ink-secondary);
}
.friend-detail {
  padding: 1rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
}
button:not(.button):not(.friend-select) {
  color: var(--primary-strong);
  cursor: pointer;
  border: 1px solid var(--line);
  background: var(--surface);
  padding: 0.65rem;
  border-radius: 0.5rem;
}
footer {
  display: flex;
  gap: 0.75rem;
  border-top: 1px solid var(--line);
  margin-top: 1.5rem;
  padding-top: 1rem;
}
small,
p {
  color: var(--ink-secondary);
  overflow-wrap: anywhere;
}
@media (max-width: 760px) {
  .friends-layout {
    grid-template-columns: 1fr;
  }
  .add-friend {
    align-items: end;
  }
}
</style>
