<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

import { eventsApi } from '@/api/events'
import { mediaApi } from '@/api/media'
import { HttpError } from '@/api/http-client'
import {
  EventKind,
  EventKindLabel,
  EventStatus,
  type EventKind as EventKindT,
  type EventResponse,
  type MediaResponse,
  type UpdateEventRequest,
} from '@/api/events-types'
import { useAuthStore } from '@/stores/auth'
import { defaultTimezone, toDatetimeLocal, toIsoWithOffset } from '@/utils/datetime'
import { randomUuid } from '@/utils/id'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

type Mode = 'create' | 'edit'
const mode = computed<Mode>(() => (eventId.value !== null ? 'edit' : 'create'))

const eventId = computed<number | null>(() => {
  const raw = route.params.id
  const id = Number(Array.isArray(raw) ? raw[0] : raw)
  return Number.isFinite(id) && id > 0 ? id : null
})

const form = ref({
  kind: EventKind.Trace as EventKindT,
  title: '',
  rawContent: '',
  /** 发生时间（trace）或计划时间（plan），datetime-local 形式。 */
  when: '',
  timezone: defaultTimezone(),
})

const loaded = ref<EventResponse | null>(null)
const loading = ref(false)
const submitting = ref(false)
const error = ref<string | null>(null)
const fieldErrors = ref<Record<string, string>>({})
interface AttachmentDraft {
  key: string
  file?: File
  media?: MediaResponse
  progress: number
  uploading: boolean
  error?: string
}
const attachments = ref<AttachmentDraft[]>([])

/** 创建时生成一次，重试复用；成功后丢弃。 */
let idempotencyKey: string | null = null

let activeController: AbortController | null = null

function validate(): boolean {
  const errors: Record<string, string> = {}
  if (!form.value.title.trim() && !form.value.rawContent.trim() && attachments.value.length === 0) {
    errors['content'] = '标题、正文和附件至少需要一项。'
  }
  if (attachments.value.some((item) => item.uploading || !item.media)) {
    errors['media'] = '请等待所有附件上传成功，失败的附件可重试或移除。'
  }
  if (!form.value.timezone.trim()) {
    errors['timezone'] = '请填写 IANA 时区名，例如 Asia/Tokyo。'
  } else {
    try {
      new Intl.DateTimeFormat('en-US', { timeZone: form.value.timezone.trim() }).format(new Date())
    } catch {
      errors['timezone'] = '时区名无法识别，请使用 IANA 名（例：Asia/Tokyo）。'
    }
  }
  if (form.value.when) {
    const probe = toIsoWithOffset(form.value.when, form.value.timezone.trim())
    if (!probe) errors['when'] = '时间格式不正确。'
  }
  fieldErrors.value = errors
  return Object.keys(errors).length === 0
}

async function load() {
  if (mode.value !== 'edit' || eventId.value === null) return
  if (activeController) activeController.abort()
  const controller = new AbortController()
  activeController = controller
  loading.value = true
  error.value = null
  try {
    const item = await eventsApi.get(eventId.value, { signal: controller.signal })
    if (controller.signal.aborted) return
    loaded.value = item
    form.value = {
      kind: item.kind,
      title: item.title ?? '',
      rawContent: item.rawContent ?? '',
      when: toDatetimeLocal(item.kind === EventKind.Plan ? item.plannedAt : item.happenedAt),
      timezone: item.timezone,
    }
    attachments.value = item.media
      .slice()
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map((media) => ({ key: media.id, media, progress: 100, uploading: false }))
  } catch (reason) {
    if (controller.signal.aborted) return
    if (reason instanceof HttpError && reason.status === 404) {
      error.value = '记录不存在或已被删除。'
    } else if (reason instanceof HttpError && reason.status === 401) {
      error.value = '会话已失效，请重新登录。'
    } else {
      error.value = reason instanceof Error ? reason.message : '加载记录失败。'
    }
  } finally {
    if (!controller.signal.aborted) loading.value = false
  }
}

async function submit() {
  if (submitting.value) return
  if (!validate()) return
  submitting.value = true
  error.value = null
  try {
    if (mode.value === 'create') {
      if (!idempotencyKey) idempotencyKey = randomUuid()
      const isoWhen = form.value.when
        ? toIsoWithOffset(form.value.when, form.value.timezone.trim())
        : null
      const payload = {
        kind: form.value.kind,
        title: form.value.title.trim() || null,
        rawContent: form.value.rawContent.trim() || null,
        ...(form.value.kind === EventKind.Plan
          ? { plannedAt: isoWhen, happenedAt: null }
          : { happenedAt: isoWhen, plannedAt: null }),
        timezone: form.value.timezone.trim(),
        mediaIds: attachments.value.map((item) => item.media!.id),
      }
      const created = await eventsApi.create(payload, idempotencyKey)
      idempotencyKey = null
      await router.replace(`/events/${created.id}`)
    } else {
      if (!loaded.value) return
      const isoWhen = form.value.when
        ? toIsoWithOffset(form.value.when, form.value.timezone.trim())
        : null
      const payload: UpdateEventRequest = {
        title: form.value.title.trim() || null,
        rawContent: form.value.rawContent.trim() || null,
        ...(form.value.kind === EventKind.Plan
          ? { plannedAt: isoWhen, happenedAt: null }
          : { happenedAt: isoWhen, plannedAt: null }),
        timezone: form.value.timezone.trim(),
        mediaIds: attachments.value.map((item) => item.media!.id),
      }
      const updated = await eventsApi.update(loaded.value.id, payload, loaded.value.version)
      loaded.value = updated
      await router.replace(`/events/${updated.id}`)
    }
  } catch (reason) {
    if (reason instanceof HttpError && reason.status === 409) {
      if (mode.value === 'create') {
        // 创建场景的 409 是幂等键冲突（同一 key 但内容不同），按规范提示并丢弃 key。
        idempotencyKey = null
        error.value = '同一条请求被重试了多次但内容不一致，请重新提交。'
      } else {
        error.value = '内容已被他人修改，正在为你拉取最新版本…'
        // 重新拉取详情，让用户基于最新内容再决定
        await load()
        error.value = '内容已被他人修改，请核对最新版本后再保存。'
      }
    } else if (reason instanceof HttpError && reason.status === 428) {
      error.value = '本地版本信息缺失，正在重新加载…'
      if (mode.value === 'edit') await load()
    } else if (reason instanceof HttpError && reason.status === 401) {
      error.value = '会话已失效，请重新登录。'
    } else if (reason instanceof HttpError && reason.status === 400) {
      error.value = reason.problem?.detail ?? '请求格式不合法，请检查表单。'
    } else {
      error.value = reason instanceof Error ? reason.message : '保存失败。'
    }
  } finally {
    submitting.value = false
  }
}

async function selectFiles(event: Event) {
  const input = event.target as HTMLInputElement
  const files = [...(input.files ?? [])]
  input.value = ''
  if (attachments.value.length + files.length > 10) {
    fieldErrors.value.media = '每条记录最多 10 个附件。'
    return
  }
  for (const file of files) {
    const draft: AttachmentDraft = {
      key: `${file.name}-${file.lastModified}-${crypto.randomUUID()}`,
      file,
      progress: 0,
      uploading: false,
    }
    attachments.value.push(draft)
    void uploadAttachment(draft)
  }
}

async function uploadAttachment(draft: AttachmentDraft) {
  if (!draft.file || draft.uploading) return
  draft.uploading = true
  draft.error = undefined
  try {
    draft.media = await mediaApi.upload(draft.file, (progress) => {
      draft.progress = progress.percent
    })
    draft.progress = 100
    delete fieldErrors.value.media
  } catch (reason) {
    draft.error = reason instanceof Error ? reason.message : '上传失败。'
  } finally {
    draft.uploading = false
  }
}

function removeAttachment(index: number) {
  attachments.value.splice(index, 1)
}

function moveAttachment(index: number, direction: -1 | 1) {
  const target = index + direction
  if (target < 0 || target >= attachments.value.length) return
  const [item] = attachments.value.splice(index, 1)
  if (item) attachments.value.splice(target, 0, item)
}

const kindOptions = [
  { value: EventKind.Trace, label: EventKindLabel[EventKind.Trace] },
  { value: EventKind.Plan, label: EventKindLabel[EventKind.Plan] },
]

const whenLabel = computed(() => (form.value.kind === EventKind.Plan ? '计划时间' : '发生时间'))

const statusHint = computed(() => {
  if (mode.value === 'create' && form.value.kind === EventKind.Plan) {
    return '新建计划时状态默认为"待执行"。'
  }
  if (mode.value === 'edit' && loaded.value) {
    return `当前状态：${loaded.value.status === EventStatus.Planned ? '待执行' : loaded.value.status === EventStatus.Completed ? '已完成' : '已取消'}`
  }
  return null
})

onMounted(() => {
  if (mode.value === 'edit' && auth.isAuthenticated) void load()
})
watch(
  () => auth.isAuthenticated,
  (now) => {
    if (now && mode.value === 'edit' && !loaded.value) void load()
  },
)
onUnmounted(() => {
  if (activeController) activeController.abort()
})
</script>

<template>
  <div class="app-shell">
    <header class="topbar">
      <RouterLink class="brand" to="/" aria-label="PassingTrace 首页"
        ><span class="brand-mark">P</span><span>PassingTrace</span></RouterLink
      >
      <nav class="nav-links" aria-label="主导航">
        <RouterLink to="/events">记录</RouterLink>
        <RouterLink to="/assistant">AI 助手</RouterLink>
      </nav>
      <div class="account-actions">
        <template v-if="auth.isAuthenticated"
          ><span class="signed-user"><i></i>{{ auth.username }}</span
          ><button class="text-button" :disabled="auth.busy" @click="auth.logout">
            退出
          </button></template
        >
        <template v-else
          ><button
            class="button button-dark compact-button"
            :disabled="auth.busy"
            @click="auth.login"
          >
            登录
          </button></template
        >
      </div>
    </header>

    <main class="form-page">
      <p class="back-link">
        <RouterLink :to="mode === 'edit' && loaded ? `/events/${loaded.id}` : '/events'"
          >← 返回</RouterLink
        >
      </p>

      <p v-if="!auth.isAuthenticated" class="empty-state">
        请先 <button class="inline-link inline-login" @click="auth.login">登录</button> 后再{{
          mode === 'create' ? '新建' : '编辑'
        }}。
      </p>

      <template v-else>
        <header class="form-header">
          <p class="eyebrow">{{ mode === 'create' ? 'NEW EVENT' : 'EDIT EVENT' }}</p>
          <h1>{{ mode === 'create' ? '写下今天的一条记录' : '编辑这条记录' }}</h1>
          <p v-if="statusHint" class="status-hint">{{ statusHint }}</p>
        </header>

        <p v-if="loading" class="empty-state">正在加载…</p>

        <form v-else class="event-form" @submit.prevent="submit">
          <fieldset class="form-field">
            <legend>类型</legend>
            <div class="radio-row">
              <label
                v-for="opt in kindOptions"
                :key="opt.value"
                class="radio-pill"
                :class="{ active: form.kind === opt.value, disabled: mode === 'edit' }"
              >
                <input
                  type="radio"
                  name="kind"
                  :value="opt.value"
                  v-model="form.kind"
                  :disabled="mode === 'edit'"
                />
                {{ opt.label }}
              </label>
            </div>
            <p v-if="mode === 'edit'" class="field-hint">类型创建后不可修改。</p>
          </fieldset>

          <label class="form-field">
            <span class="field-label">标题</span>
            <input
              v-model="form.title"
              type="text"
              maxlength="200"
              placeholder="一句话标题（可与正文同时为空则非法）"
              autocomplete="off"
            />
          </label>

          <label class="form-field">
            <span class="field-label">正文</span>
            <textarea
              v-model="form.rawContent"
              rows="6"
              placeholder="把当下想到的、看到的、吃到的写下来…"
            />
          </label>
          <p v-if="fieldErrors.content" class="field-error">{{ fieldErrors.content }}</p>

          <fieldset class="form-field media-field">
            <legend>图片、视频或文件</legend>
            <label class="media-picker">
              <input type="file" multiple @change="selectFiles" />
              <span>＋ 选择附件</span>
              <small>最多 10 个；图片 20MB、视频 1GB、其他文件 200MB</small>
            </label>
            <ol v-if="attachments.length" class="attachment-list">
              <li v-for="(item, index) in attachments" :key="item.key">
                <span class="attachment-icon">{{
                  item.media?.kind === 1 ? '图' : item.media?.kind === 2 ? '影' : '件'
                }}</span>
                <span class="attachment-name">{{ item.media?.fileName ?? item.file?.name }}</span>
                <span v-if="item.uploading" class="attachment-progress">{{ item.progress }}%</span>
                <span v-else-if="item.error" class="attachment-error">{{ item.error }}</span>
                <button
                  v-if="item.error"
                  type="button"
                  class="text-button"
                  @click="uploadAttachment(item)"
                >
                  重试
                </button>
                <button
                  type="button"
                  class="text-button"
                  :disabled="index === 0"
                  @click="moveAttachment(index, -1)"
                >
                  ↑
                </button>
                <button
                  type="button"
                  class="text-button"
                  :disabled="index === attachments.length - 1"
                  @click="moveAttachment(index, 1)"
                >
                  ↓
                </button>
                <button type="button" class="text-button danger" @click="removeAttachment(index)">
                  移除
                </button>
                <progress v-if="item.uploading" :value="item.progress" max="100" />
              </li>
            </ol>
            <p v-if="fieldErrors.media" class="field-error">{{ fieldErrors.media }}</p>
          </fieldset>

          <label class="form-field">
            <span class="field-label">{{ whenLabel }}</span>
            <input v-model="form.when" type="datetime-local" />
            <p v-if="fieldErrors.when" class="field-error">{{ fieldErrors.when }}</p>
          </label>

          <label class="form-field">
            <span class="field-label">时区 (IANA)</span>
            <input
              v-model="form.timezone"
              type="text"
              placeholder="Asia/Tokyo"
              autocomplete="off"
              spellcheck="false"
            />
            <p class="field-hint">
              例如 <code>Asia/Tokyo</code> / <code>Asia/Shanghai</code> / <code>UTC</code>。
            </p>
            <p v-if="fieldErrors.timezone" class="field-error">{{ fieldErrors.timezone }}</p>
          </label>

          <p v-if="error" class="error-banner" role="alert">{{ error }}</p>

          <div class="form-actions">
            <button
              type="button"
              class="button ghost compact-button"
              :disabled="submitting"
              @click="router.back()"
            >
              取消
            </button>
            <button
              type="submit"
              class="button button-accent compact-button"
              :disabled="submitting"
            >
              {{ submitting ? '保存中…' : mode === 'create' ? '创建记录' : '保存修改' }}
            </button>
          </div>
        </form>
      </template>
    </main>

    <footer>
      <span>PassingTrace © 2026</span>
      <span>记录 · 个人时间线</span>
    </footer>
  </div>
</template>

<style scoped>
.form-page {
  max-width: 720px;
  margin: 0 auto;
  padding: 48px 42px 96px;
}
.back-link {
  margin: 0 0 20px;
  font-size: 12px;
  color: rgba(36, 35, 31, 0.55);
}
.back-link a:hover {
  color: var(--red);
}
.form-header {
  margin-bottom: 28px;
}
.form-header h1 {
  margin: 4px 0 0;
  font-family: 'Noto Serif SC', serif;
  font-size: clamp(26px, 3vw, 36px);
  font-weight: 500;
  letter-spacing: -0.02em;
}
.status-hint {
  margin: 10px 0 0;
  color: rgba(36, 35, 31, 0.55);
  font-size: 12px;
}
.event-form {
  display: flex;
  flex-direction: column;
  gap: 20px;
}
.form-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  border: 0;
  padding: 0;
  margin: 0;
}
.form-field > .field-label,
.form-field > legend {
  font-size: 11px;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: rgba(36, 35, 31, 0.55);
  padding: 0;
}
.form-field input,
.form-field textarea {
  border: 1px solid var(--line);
  background: var(--paper);
  padding: 10px 12px;
  font: inherit;
  font-size: 14px;
  color: var(--ink);
  transition:
    border-color 0.18s,
    background 0.18s;
}
.form-field input:focus,
.form-field textarea:focus {
  outline: none;
  border-color: var(--red);
  background: #fdf9f1;
}
.form-field textarea {
  resize: vertical;
  min-height: 120px;
  line-height: 1.7;
}
.field-hint {
  margin: 0;
  color: rgba(36, 35, 31, 0.45);
  font-size: 11px;
}
.field-hint code {
  font-family: 'DM Sans', monospace;
  background: rgba(36, 35, 31, 0.05);
  padding: 1px 5px;
  border-radius: 3px;
}
.field-error {
  margin: 0;
  color: #b33225;
  font-size: 12px;
}
.media-picker {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 14px;
  border: 1px dashed rgba(36, 35, 31, 0.28);
  cursor: pointer;
}
.media-picker input {
  position: absolute;
  opacity: 0;
  pointer-events: none;
}
.media-picker span {
  color: var(--red);
  font-weight: 700;
}
.media-picker small {
  color: rgba(36, 35, 31, 0.48);
}
.attachment-list {
  display: grid;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.attachment-list li {
  position: relative;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px;
  border: 1px solid var(--line);
}
.attachment-icon {
  display: grid;
  place-items: center;
  width: 30px;
  height: 30px;
  background: var(--ink);
  color: white;
  font-size: 11px;
}
.attachment-name {
  min-width: 0;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
}
.attachment-progress {
  color: var(--sage);
  font-size: 12px;
}
.attachment-error {
  max-width: 180px;
  color: #b33225;
  font-size: 11px;
}
.attachment-list progress {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  width: 100%;
  height: 3px;
}
.text-button.danger {
  color: #b33225;
}
.radio-row {
  display: flex;
  gap: 10px;
}
.radio-pill {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  border: 1px solid var(--line);
  border-radius: 999px;
  font-size: 13px;
  cursor: pointer;
  user-select: none;
}
.radio-pill input {
  position: absolute;
  opacity: 0;
  pointer-events: none;
}
.radio-pill.active {
  border-color: var(--red);
  color: var(--red);
}
.radio-pill.disabled {
  cursor: not-allowed;
  opacity: 0.55;
}
.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 12px;
}
.button.ghost {
  background: transparent;
  color: var(--ink);
  border: 1px solid var(--line);
}
.empty-state {
  text-align: center;
  padding: 48px 0;
  color: rgba(36, 35, 31, 0.55);
  font-size: 14px;
}
.inline-login {
  border-bottom: 1px solid currentColor;
  color: var(--red);
}
@media (max-width: 800px) {
  .form-page {
    padding: 32px 20px 60px;
  }
  .form-actions {
    flex-direction: column-reverse;
    align-items: stretch;
  }
}
</style>
