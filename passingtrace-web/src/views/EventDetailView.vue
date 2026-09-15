<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

import { eventsApi } from '@/api/events'
import { mediaApi } from '@/api/media'
import { aiApi, type SemanticResult } from '@/api/ai'
import { HttpError } from '@/api/http-client'
import WebAppHeader from '@/components/WebAppHeader.vue'
import {
  EventKind,
  EventKindLabel,
  EventStatus,
  EventStatusLabel,
  type EventResponse,
} from '@/api/events-types'
import { useAuthStore } from '@/stores/auth'
import { conversationIdFromQuery } from '@/utils/assistant-navigation'
import { formatLocal } from '@/utils/datetime'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const conversationId = computed(() => conversationIdFromQuery(route.query.conversation))
const conversationRoute = computed(() => ({
  path: '/assistant',
  query: { conversation: conversationId.value },
}))

const item = ref<EventResponse | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const confirmDelete = ref(false)
const deleting = ref(false)
const editMode = ref(false)
const mediaUrls = ref<Record<string, string>>({})
const mediaErrors = ref<Record<string, string>>({})
const semantic = ref<SemanticResult | null>(null)
const showSemantic = ref(false)
const semanticLoading = ref(false)
const semanticError = ref<string | null>(null)
const reparsing = ref(false)

let activeController: AbortController | null = null

const eventId = computed(() => {
  const raw = route.params.id
  const id = Number(Array.isArray(raw) ? raw[0] : raw)
  return Number.isFinite(id) ? id : null
})

async function load() {
  if (eventId.value === null) {
    error.value = '无效的事件 id。'
    return
  }
  if (activeController) activeController.abort()
  const controller = new AbortController()
  activeController = controller
  loading.value = true
  error.value = null
  semantic.value = null
  showSemantic.value = false
  semanticError.value = null
  for (const url of Object.values(mediaUrls.value)) URL.revokeObjectURL(url)
  mediaUrls.value = {}
  mediaErrors.value = {}
  try {
    item.value = await eventsApi.get(eventId.value, { signal: controller.signal })
    const accesses = await Promise.all(
      item.value.media.map(async (media) => {
        try {
          const access = await mediaApi.access(media.id, { signal: controller.signal })
          return [media.id, access.url] as const
        } catch (reason) {
          if (!controller.signal.aborted) {
            mediaErrors.value = {
              ...mediaErrors.value,
              [media.id]: reason instanceof Error ? reason.message : '附件暂时无法读取。',
            }
          }
          return null
        }
      }),
    )
    mediaUrls.value = Object.fromEntries(accesses.filter((entry) => entry !== null))
  } catch (reason) {
    if (controller.signal.aborted) return
    if (reason instanceof HttpError && reason.status === 404) {
      error.value = '记录不存在或已被删除。'
    } else if (reason instanceof HttpError && reason.status === 401) {
      error.value = '会话已失效，请重新登录。'
    } else {
      error.value = reason instanceof Error ? reason.message : '加载记录失败。'
    }
    item.value = null
  } finally {
    if (!controller.signal.aborted) loading.value = false
  }
}

async function toggleSemantic() {
  showSemantic.value = !showSemantic.value
  if (!showSemantic.value || semantic.value || semanticLoading.value || !item.value) return

  semanticLoading.value = true
  semanticError.value = null
  try {
    semantic.value = await aiApi.getSemantic(item.value.id)
  } catch (reason) {
    semanticError.value = reason instanceof Error ? reason.message : '详细分析暂时无法读取。'
  } finally {
    semanticLoading.value = false
  }
}

async function reparse() {
  if (!item.value || reparsing.value) return
  reparsing.value = true
  semanticError.value = null
  try {
    await aiApi.reparse(item.value.id)
    semantic.value = {
      ...(semantic.value ?? (await aiApi.getSemantic(item.value.id))),
      status: 'Pending',
    }
  } catch (reason) {
    semanticError.value = reason instanceof Error ? reason.message : '重新分析失败，请稍后重试。'
  } finally {
    reparsing.value = false
  }
}

async function onDelete() {
  if (!item.value) return
  if (!confirmDelete.value) {
    confirmDelete.value = true
    return
  }
  deleting.value = true
  error.value = null
  try {
    await eventsApi.remove(item.value.id, item.value.version)
    await router.replace('/events')
  } catch (reason) {
    if (reason instanceof HttpError && reason.status === 409) {
      error.value = '内容已被他人修改，请刷新后重试。'
      await load()
    } else if (reason instanceof HttpError && reason.status === 404) {
      await router.replace('/events')
    } else if (reason instanceof HttpError && reason.status === 428) {
      error.value = '本地版本信息缺失，请刷新页面。'
    } else {
      error.value = reason instanceof Error ? reason.message : '删除失败。'
    }
  } finally {
    deleting.value = false
    confirmDelete.value = false
  }
}

function startEdit() {
  if (!item.value) return
  editMode.value = true
  void router.push(`/events/${item.value.id}/edit`)
}

function fmt(iso: string | null): string {
  return formatLocal(iso)
}

async function navigateToLocation() {
  const event = item.value
  const location = event?.locations[0]
  if (!event || !location?.id) return
  const target = await eventsApi.navigationTarget(event.id, location.id)
  const url = new URL('https://uri.amap.com/navigation')
  url.searchParams.set('to', `${target.longitude},${target.latitude},${target.name}`)
  url.searchParams.set('mode', 'car')
  url.searchParams.set('src', '星期八')
  window.open(url.toString(), '_blank', 'noopener')
}

onMounted(() => {
  if (auth.isAuthenticated) void load()
})
watch(
  () => auth.isAuthenticated,
  (now) => {
    if (now) void load()
  },
)
onUnmounted(() => {
  if (activeController) activeController.abort()
  for (const url of Object.values(mediaUrls.value)) URL.revokeObjectURL(url)
})
</script>

<template>
  <div class="app-shell">
    <WebAppHeader />

    <main class="detail-page workspace-main">
      <nav class="back-link" aria-label="返回导航">
        <RouterLink
          v-if="conversationId"
          class="conversation-return"
          :to="conversationRoute"
          replace
        >
          <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
            <path d="m12 5-7 7 7 7M5 12h14" />
          </svg>
          返回原对话
        </RouterLink>
        <RouterLink to="/events" :class="{ 'secondary-return': conversationId }">
          <svg v-if="!conversationId" class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
            <path d="m12 5-7 7 7 7M5 12h14" />
          </svg>
          {{ conversationId ? '查看记录列表' : '返回记录列表' }}
        </RouterLink>
      </nav>

      <p v-if="!auth.isAuthenticated" class="empty-state">
        请先 <button class="inline-link inline-login" @click="auth.login()">登录</button> 后查看。
      </p>
      <p v-else-if="loading" class="empty-state">正在加载…</p>
      <p v-else-if="error" class="error-banner" role="alert">
        {{ error }}<button @click="load">重试</button>
      </p>

      <template v-else-if="item">
        <header class="detail-header">
          <div class="detail-meta">
            <span class="badge kind" :data-kind="item.kind">{{ EventKindLabel[item.kind] }}</span>
            <span class="badge status" :data-status="item.status">{{
              EventStatusLabel[item.status]
            }}</span>
            <span class="detail-id">#{{ item.id }}</span>
          </div>
          <div class="detail-actions">
            <button class="button button-dark compact-button" @click="startEdit">编辑</button>
            <button
              class="button compact-button"
              :class="confirmDelete ? 'danger-confirm' : 'ghost'"
              :disabled="deleting"
              @click="onDelete"
            >
              {{ confirmDelete ? (deleting ? '删除中…' : '再次点击确认删除') : '删除' }}
            </button>
          </div>
        </header>

        <div class="detail-workspace">
          <article class="detail-content" aria-label="记录内容">
            <div class="detail-title-row">
              <h1 class="detail-title">{{ item.title ?? '（无标题）' }}</h1>
              <button
                class="semantic-toggle"
                :class="{ active: showSemantic }"
                type="button"
                :aria-label="showSemantic ? '收起 AI 分析' : '查看 AI 分析'"
                :aria-expanded="showSemantic"
                aria-controls="event-semantic-panel"
                :title="showSemantic ? '收起 AI 分析' : '查看 AI 分析'"
                @click="toggleSemantic"
              >
                <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
                  <path
                    d="M12 3c.8 4.5 2.5 6.2 7 7-4.5.8-6.2 2.5-7 7-.8-4.5-2.5-6.2-7-7 4.5-.8 6.2-2.5 7-7Z"
                  />
                  <path
                    d="M19 16c.3 1.8 1.2 2.7 3 3-1.8.3-2.7 1.2-3 3-.3-1.8-1.2-2.7-3-3 1.8-.3 2.7-1.2 3-3Z"
                  />
                </svg>
              </button>
            </div>
            <div
              v-if="
                item.effectiveClassification.primaryCategory ||
                item.effectiveClassification.tags.length
              "
              class="label-row"
            >
              <span v-if="item.effectiveClassification.primaryCategory" class="badge kind">{{
                item.effectiveClassification.primaryCategory.displayName
              }}</span>
              <span
                v-for="tag in item.effectiveClassification.tags"
                :key="tag.taxonomyKey ?? tag.displayName"
                class="badge status"
              >
                {{ tag.origin === 'ai' ? '✦ ' : '' }}{{ tag.displayName }}
              </span>
            </div>

            <Transition name="semantic-panel">
              <section
                v-if="showSemantic"
                id="event-semantic-panel"
                class="semantic-card"
                :data-status="semantic?.status ?? item.semanticStatus"
                aria-label="AI 分析详情"
                aria-live="polite"
              >
                <div>
                  <p class="section-label">
                    AI 分析 · {{ semantic?.status ?? item.semanticStatus }}
                  </p>
                  <p v-if="semantic?.summary ?? item.semanticSummary">
                    {{ semantic?.summary ?? item.semanticSummary }}
                  </p>
                  <p v-else-if="semanticLoading">正在读取详细分析…</p>
                  <p v-else>Worker 会在后台分析正文与图片，记录保存不需要等待。</p>
                  <small v-if="semantic?.model"
                    >{{ semantic.model }} · {{ semantic.pipelineVersion }}</small
                  >
                  <small v-if="semantic?.error" class="semantic-error">{{ semantic.error }}</small>
                  <small v-if="semanticError" class="semantic-error">{{ semanticError }}</small>
                </div>
                <button
                  class="text-button"
                  :disabled="reparsing || semanticLoading"
                  @click="reparse"
                >
                  {{ reparsing ? '已排队' : '重新分析' }}
                </button>
              </section>
            </Transition>

            <section class="raw-content">
              <p class="section-label">原始记录</p>
              <p v-if="item.rawContent" class="raw-content-body">{{ item.rawContent }}</p>
              <p v-else class="raw-content-empty">（未填写正文）</p>
            </section>

            <section v-if="item.media.length" class="media-section">
              <p class="section-label">附件</p>
              <div class="media-grid">
                <figure
                  v-for="media in item.media"
                  :key="media.id"
                  :class="{ file: media.kind === 3 }"
                >
                  <img
                    v-if="media.kind === 1 && mediaUrls[media.id]"
                    :src="mediaUrls[media.id]"
                    :alt="media.fileName"
                  />
                  <video
                    v-else-if="media.kind === 2 && mediaUrls[media.id]"
                    :src="mediaUrls[media.id]"
                    controls
                    preload="metadata"
                  />
                  <a
                    v-else-if="mediaUrls[media.id]"
                    :href="mediaUrls[media.id]"
                    :download="media.fileName"
                    >下载文件</a
                  >
                  <div v-else class="media-unavailable" role="status">
                    <span>附件暂时无法加载</span>
                    <button type="button" @click="load">重试</button>
                  </div>
                  <figcaption>
                    {{ media.fileName }} · {{ (media.size / 1024 / 1024).toFixed(1) }}MB
                    <small v-if="mediaErrors[media.id]">{{ mediaErrors[media.id] }}</small>
                  </figcaption>
                </figure>
              </div>
            </section>
          </article>
          <aside class="detail-context" aria-label="记录信息">
            <section class="context-section">
              <h2 class="context-title">记录信息</h2>
              <dl class="source">
                <div v-if="item.kind === EventKind.Trace">
                  <dt>发生时间</dt>
                  <dd>{{ fmt(item.happenedAt) }}</dd>
                </div>
                <div v-if="item.kind === EventKind.Plan">
                  <dt>预定时间</dt>
                  <dd>{{ fmt(item.plannedAt) }}</dd>
                </div>
                <div v-if="item.completedAt">
                  <dt>完成时间</dt>
                  <dd>{{ fmt(item.completedAt) }}</dd>
                </div>
                <div v-if="item.status === EventStatus.Planned">
                  <dt>状态</dt>
                  <dd>待执行</dd>
                </div>
              </dl>
            </section>

            <section v-if="item.locations.length" class="semantic-card location-card">
              <div>
                <p class="section-label">地点</p>
                <strong>{{ item.locations[0]?.name }}</strong>
                <p>{{ item.locations[0]?.address }}</p>
              </div>
              <button
                v-if="item.locations[0]?.latitude != null"
                class="button button-dark compact-button"
                @click="navigateToLocation"
              >
                导航到这里
              </button>
            </section>

            <section class="source-meta">
              <p class="section-label">Source 修订</p>
              <dl>
                <div>
                  <dt>Source 修订版本</dt>
                  <dd>{{ item.sourceRevision }}</dd>
                </div>
                <div>
                  <dt>并发令牌 (version)</dt>
                  <dd>{{ item.version }}</dd>
                </div>
                <div>
                  <dt>可见性</dt>
                  <dd>仅自己可见</dd>
                </div>
                <div>
                  <dt>创建时间</dt>
                  <dd>{{ fmt(item.createdAt) }}</dd>
                </div>
                <div>
                  <dt>最后更新</dt>
                  <dd>{{ fmt(item.updatedAt) }}</dd>
                </div>
              </dl>
            </section>
          </aside>
        </div>
      </template>
    </main>

    <footer>
      <span>星期八 © 2026</span>
      <span>记录 · 个人时间线</span>
    </footer>
  </div>
</template>

<style scoped>
.detail-workspace {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: var(--workspace-gap);
  align-items: start;
}
.detail-content,
.detail-context {
  min-width: 0;
}
.detail-content {
  padding: clamp(20px, 2.5vw, 40px);
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  background: var(--surface);
}
.detail-context {
  display: grid;
  gap: 24px;
  padding: clamp(20px, 1.75vw, 28px);
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  background: var(--surface-soft);
  overflow-wrap: anywhere;
}
.context-title {
  margin: 0 0 20px;
  font-size: 18px;
}
.back-link {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px 24px;
  margin: 0 0 24px;
  font-size: 14px;
  color: var(--ink-secondary);
}
.back-link a {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  min-height: 44px;
  border-radius: var(--radius-md);
}
.back-link .conversation-return {
  padding: 0 16px;
  border: 1px solid var(--line-strong);
  color: var(--primary-strong);
  background: var(--surface);
  font-weight: 650;
}
.back-link .secondary-return {
  font-size: 13px;
}
.back-link a:hover {
  color: var(--primary-strong);
  text-decoration: underline;
  text-underline-offset: 4px;
}
.back-link a:focus-visible {
  outline: 2px solid var(--primary-strong);
  outline-offset: 4px;
}
.detail-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
  margin-bottom: 24px;
}
.detail-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  font-size: 11px;
  color: var(--ink-tertiary);
}
.detail-id {
  font-size: 13px;
  color: var(--ink-tertiary);
}
.detail-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}
.detail-title-row {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  margin: 0 0 28px;
}
.detail-title {
  min-width: 0;
  flex: 1;
  margin: 0;
  font-size: clamp(28px, 4vw, 42px);
  font-weight: 750;
  line-height: 1.2;
  letter-spacing: -0.045em;
  overflow-wrap: anywhere;
}
.semantic-toggle {
  width: 44px;
  height: 44px;
  flex: 0 0 44px;
  display: grid;
  place-items: center;
  border: 1px solid transparent;
  border-radius: var(--radius-md);
  color: var(--ink-tertiary);
  background: transparent;
  transition:
    color var(--motion-fast) var(--ease-out),
    border-color var(--motion-fast) var(--ease-out),
    background-color var(--motion-fast) var(--ease-out);
}
.semantic-toggle:hover {
  color: var(--primary-strong);
  background: var(--surface-soft);
}
.semantic-toggle.active {
  color: var(--primary-strong);
  border-color: color-mix(in srgb, var(--primary) 32%, var(--line));
  background: var(--primary-soft);
}
.label-row {
  margin: -14px 0 28px;
  display: flex;
  flex-wrap: wrap;
  gap: 7px;
}
.source {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 16px 32px;
  margin: 0;
}
.source div {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 13px;
}
.source dt {
  font-size: 12px;
  color: var(--ink-tertiary);
  text-transform: uppercase;
}
.source dd {
  margin: 0;
  font-weight: 700;
  font-size: 15px;
}
.source dd small {
  margin-left: 6px;
  color: var(--ink-tertiary);
  font-size: 11px;
}
.section-label {
  margin: 0 0 8px;
  font-size: 12px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--ink-tertiary);
}
.raw-content {
  margin-bottom: 36px;
  padding-top: 24px;
  border-top: 1px solid var(--line);
}
.raw-content-body {
  margin: 0;
  font-size: 16px;
  line-height: 1.8;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  max-width: 78ch;
}
.raw-content-empty {
  margin: 0;
  color: var(--ink-tertiary);
  font-size: 13px;
}
.media-section {
  margin-top: 32px;
}
.media-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 260px), 1fr));
  gap: 12px;
}
.media-grid figure {
  overflow: hidden;
  margin: 0;
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-1);
}
.media-grid img,
.media-grid video {
  display: block;
  width: 100%;
  max-height: 420px;
  object-fit: contain;
  aspect-ratio: 4 / 3;
  background: var(--surface-soft);
}
.media-unavailable {
  min-height: 180px;
  display: grid;
  place-content: center;
  justify-items: center;
  gap: 10px;
  padding: 24px;
  color: var(--ink-secondary);
  background: var(--surface-soft);
}
.media-unavailable button {
  min-height: 40px;
  padding: 0 16px;
  border: 1px solid var(--line-strong);
  border-radius: var(--radius-md);
  color: var(--primary-strong);
  background: var(--surface);
  cursor: pointer;
}
.media-grid figure.file {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 18px;
}
.media-grid figure.file a {
  color: var(--primary-strong);
}
.media-grid figcaption {
  padding: 8px 10px;
  color: var(--ink-tertiary);
  font-size: 11px;
  overflow-wrap: anywhere;
}
.media-grid figcaption small {
  display: block;
  margin-top: 4px;
  color: var(--danger);
  overflow-wrap: anywhere;
}
.semantic-card {
  display: flex;
  justify-content: space-between;
  gap: 18px;
  margin-bottom: 36px;
  padding: 20px;
  border: 1px solid color-mix(in srgb, var(--primary) 25%, var(--line));
  border-radius: var(--radius-lg);
  background: var(--primary-soft);
}
.semantic-card > div {
  min-width: 0;
  overflow-wrap: anywhere;
}
.location-card {
  flex-direction: column;
  align-items: flex-start;
  margin-bottom: 0;
}
.semantic-card p {
  margin: 5px 0;
  line-height: 1.7;
}
.semantic-card small {
  display: block;
  color: var(--ink-secondary);
}
.semantic-card .semantic-error {
  color: var(--danger);
}
.semantic-panel-enter-active,
.semantic-panel-leave-active {
  transition:
    opacity var(--motion-fast) var(--ease-out),
    transform var(--motion-fast) var(--ease-out);
}
.semantic-panel-enter-from,
.semantic-panel-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}
@media (prefers-reduced-motion: reduce) {
  .semantic-toggle,
  .semantic-panel-enter-active,
  .semantic-panel-leave-active {
    transition: none;
  }
}
.source-meta {
  border-top: 1px solid var(--line);
  padding-top: 24px;
}
.source-meta dl {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 10px 24px;
  margin: 0;
  font-size: 12px;
}
.source-meta dt {
  color: var(--ink-tertiary);
}
.source-meta dd {
  margin: 0;
  color: var(--ink-secondary);
  font-variant-numeric: tabular-nums;
}
.badge {
  display: inline-block;
  padding: 2px 8px;
  font-size: 10px;
  letter-spacing: 0.08em;
  border: 1px solid currentColor;
  border-radius: 999px;
}
.badge.kind {
  border-color: transparent;
  color: var(--primary-strong);
  background: var(--primary-soft);
}
.badge.status[data-status='0'] {
  color: var(--sage);
}
.badge.status[data-status='1'] {
  color: var(--success);
}
.badge.status[data-status='2'] {
  color: var(--ink-tertiary);
}
.button.ghost {
  background: transparent;
  color: var(--ink);
  border: 1px solid var(--line);
}
.button.danger-confirm {
  color: var(--on-primary);
  background: var(--danger);
}
.empty-state {
  text-align: center;
  padding: 48px 0;
  color: var(--ink-secondary);
  font-size: 14px;
}
.inline-login {
  border-bottom: 1px solid currentColor;
  color: var(--red);
}
@media (min-width: 1024px) {
  .detail-workspace {
    grid-template-columns: minmax(0, 2.25fr) minmax(0, 1fr);
  }
}
@media (max-width: 600px) {
  .detail-content,
  .detail-context {
    padding: 18px;
  }
  .semantic-card {
    flex-direction: column;
    align-items: flex-start;
  }
  .detail-header {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>
