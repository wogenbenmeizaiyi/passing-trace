<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { socialApi, type Friend, type SharedDocument } from '@/api/social'
import { randomUuid } from '@/utils/id'
const props = defineProps<{ eventId?: number; storylineId?: string; initialFriendId?: string }>()
const emit = defineEmits<{ close: [] }>()
const router = useRouter()
const friends = ref<Friend[]>([])
const friendId = ref(props.initialFriendId ?? '')
const modal = ref<HTMLDialogElement | null>(null)
const document = ref<SharedDocument | null>(null)
const busy = ref(false)
const loading = ref(false)
const loadErrors = ref<string[]>([])
const error = ref('')
const sent = ref<Array<{ id: string; recipientId: string; revokedAt: string | null }>>([])
let clientMessageId = randomUuid()
const payload = {
  clientMessageId,
  kind: props.eventId ? ('record' as const) : ('storyline' as const),
  eventId: props.eventId,
  storylineId: props.storylineId,
}
onMounted(() => {
  modal.value?.showModal()
  void load()
})
async function load() {
  if (loading.value || busy.value) return
  loading.value = true
  loadErrors.value = []
  const [people, preview, history] = await Promise.allSettled([
    socialApi.friends(),
    socialApi.preview(payload),
    socialApi.shares(props.eventId, props.storylineId),
  ])
  friends.value = people.status === 'fulfilled' ? people.value : []
  document.value = preview.status === 'fulfilled' ? preview.value : null
  sent.value = history.status === 'fulfilled' ? history.value : []
  if (people.status === 'rejected') loadErrors.value.push('好友列表暂时无法加载。')
  if (preview.status === 'rejected') loadErrors.value.push('分享内容暂时无法加载。')
  if (history.status === 'rejected')
    loadErrors.value.push('已发送的分享暂时无法加载，不影响本次分享。')
  loading.value = false
}
watch(friendId, () => {
  clientMessageId = randomUuid()
  payload.clientMessageId = clientMessageId
})
async function send() {
  if (
    !friends.value.some((f) => f.id === friendId.value) ||
    !document.value ||
    busy.value ||
    loading.value
  )
    return
  busy.value = true
  error.value = ''
  try {
    const c = await socialApi.open(friendId.value)
    await socialApi.send(c.id, payload)
    emit('close')
    await router.push(`/messages/${c.id}`)
  } catch {
    error.value = '暂时未能发送，请稍后重试。'
  } finally {
    busy.value = false
  }
}
async function revoke(id: string) {
  try {
    await socialApi.revoke(id)
    sent.value = await socialApi.shares(props.eventId, props.storylineId)
  } catch {
    error.value = '暂时无法停止分享，请重试。'
  }
}
</script>
<template>
  <dialog
    ref="modal"
    class="share-dialog"
    aria-labelledby="share-title"
    @cancel.prevent="emit('close')"
  >
    <header>
      <h2 id="share-title">分享给好友</h2>
      <button class="text-button" aria-label="关闭分享" @click="emit('close')">关闭</button>
    </header>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="loading" role="status">正在准备分享内容…</p>
    <div v-else-if="loadErrors.length" class="load-error" role="alert">
      <p v-for="message in loadErrors" :key="message">{{ message }}</p>
      <button class="text-button" :disabled="busy" @click="load">重新加载</button>
    </div>
    <template v-if="document"
      ><h3>{{ document.title }}</h3>
      <p>
        {{
          document.kind === 'storyline'
            ? `包含 ${document.records.length} 个记录节点及附件`
            : '包含这条记录的内容及附件'
        }}
      </p>
      <details>
        <summary>查看将分享的内容</summary>
        <article v-for="record in document.records" :key="record.eventId" class="preview-record">
          <strong>{{ record.available ? record.title : '内容已不可查看' }}</strong>
          <template v-if="record.available">
            <p>{{ record.content }}</p>
            <small v-if="record.media.length"
              >附件：{{ record.media.map((m) => m.name).join('、') }}</small
            >
          </template>
        </article>
      </details>
      <p>好友看到的是发送时的版本，后续修改不会自动展示。</p></template
    >
    <label
      >接收好友<select v-model="friendId" :disabled="busy || loading || !friends.length">
        <option value="">选择一位好友</option>
        <option v-for="f in friends" :key="f.id" :value="f.id">
          {{ f.remark || f.person.nickname }}
        </option>
      </select></label
    >
    <p v-if="!loading && !friends.length && !loadErrors.length">
      还没有可以分享的好友，先去消息页添加一位吧。
    </p>
    <button
      class="button button-primary"
      :disabled="busy || loading || !friends.some((f) => f.id === friendId) || !document"
      @click="send"
    >
      {{ busy ? '正在发送…' : '确认分享' }}
    </button>
    <details v-if="sent.length">
      <summary>管理已发送的分享</summary>
      <div v-for="s in sent" :key="s.id" class="sent-share">
        <span>{{
          friends.find((f) => f.person.id === s.recipientId)?.person.nickname || '接收好友'
        }}</span
        ><span v-if="s.revokedAt">已停止</span
        ><button v-else class="text-button" @click="revoke(s.id)">停止分享</button>
      </div>
    </details>
  </dialog>
</template>
<style scoped>
.preview-record {
  margin-block: 1rem;
  padding: 0.75rem;
  border: 1px solid var(--line);
  border-radius: 0.6rem;
  overflow-wrap: anywhere;
}
.preview-record p {
  white-space: pre-wrap;
}
.share-dialog::backdrop {
  background: rgb(0 0 0 / 0.45);
}
.share-dialog {
  width: min(calc(100% - 2rem), 32rem);
  max-height: 85dvh;
  overflow: auto;
  background: var(--surface);
  color: var(--ink);
  padding: 1.5rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-2);
}
.load-error {
  margin-block: 1rem;
}
.load-error p {
  margin-block: 0.5rem;
}
header,
.sent-share {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}
label {
  display: grid;
  gap: 0.5rem;
  margin-block: 1rem;
}
select {
  padding: 0.8rem;
  background: var(--surface-soft);
  color: var(--ink);
  border: 1px solid var(--line);
  border-radius: 0.6rem;
}
p {
  color: var(--ink-secondary);
  line-height: 1.6;
}
details {
  margin-top: 1.5rem;
}
summary {
  cursor: pointer;
  padding: 0.5rem 0;
}
</style>
