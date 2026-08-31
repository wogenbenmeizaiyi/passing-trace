<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

import { eventsApi } from '@/api/events'
import { mediaApi } from '@/api/media'
import { aiApi, type SemanticResult } from '@/api/ai'
import { HttpError } from '@/api/http-client'
import {
  EventKind,
  EventKindLabel,
  EventStatus,
  EventStatusLabel,
  type EventResponse,
} from '@/api/events-types'
import { useAuthStore } from '@/stores/auth'
import { formatLocal } from '@/utils/datetime'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const item = ref<EventResponse | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const confirmDelete = ref(false)
const deleting = ref(false)
const editMode = ref(false)
const mediaUrls = ref<Record<string, string>>({})
const semantic = ref<SemanticResult | null>(null)
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
  try {
    item.value = await eventsApi.get(eventId.value, { signal: controller.signal })
    const accesses = await Promise.all(
      item.value.media.map(async (media) => {
        const access = await mediaApi.access(media.id, { signal: controller.signal })
        return [media.id, access.url] as const
      }),
    )
    mediaUrls.value = Object.fromEntries(accesses)
    semantic.value = await aiApi.getSemantic(item.value.id)
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

async function reparse() {
  if (!item.value || reparsing.value) return
  reparsing.value = true
  try {
    await aiApi.reparse(item.value.id)
    semantic.value = {
      ...(semantic.value ?? (await aiApi.getSemantic(item.value.id))),
      status: 'Pending',
    }
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
  url.searchParams.set('src', 'PassingTrace')
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

    <main class="detail-page">
      <p class="back-link">
        <RouterLink to="/events">← 返回记录列表</RouterLink>
      </p>

      <p v-if="!auth.isAuthenticated" class="empty-state">
        请先 <button class="inline-link inline-login" @click="auth.login">登录</button> 后查看。
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

        <h1 class="detail-title">{{ item.title ?? '（无标题）' }}</h1>
        <div
          v-if="
            item.effectiveClassification.primaryCategory || item.effectiveClassification.tags.length
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

        <dl class="source">
          <div v-if="item.kind === EventKind.Trace">
            <dt>发生时间</dt>
            <dd>
              {{ fmt(item.happenedAt) }} <small>({{ item.timezone }})</small>
            </dd>
          </div>
          <div v-if="item.kind === EventKind.Plan">
            <dt>计划时间</dt>
            <dd>
              {{ fmt(item.plannedAt) }} <small>({{ item.timezone }})</small>
            </dd>
          </div>
          <div v-if="item.completedAt">
            <dt>完成时间</dt>
            <dd>{{ fmt(item.completedAt) }}</dd>
          </div>
          <div>
            <dt>时区</dt>
            <dd>{{ item.timezone }}</dd>
          </div>
          <div v-if="item.status === EventStatus.Planned">
            <dt>状态</dt>
            <dd>待执行</dd>
          </div>
        </dl>

        <section class="raw-content">
          <p class="section-label">原始记录</p>
          <p v-if="item.rawContent" class="raw-content-body">{{ item.rawContent }}</p>
          <p v-else class="raw-content-empty">（未填写正文）</p>
        </section>

        <section v-if="item.media.length" class="media-section">
          <p class="section-label">附件</p>
          <div class="media-grid">
            <figure v-for="media in item.media" :key="media.id" :class="{ file: media.kind === 3 }">
              <img v-if="media.kind === 1" :src="mediaUrls[media.id]" :alt="media.fileName" />
              <video
                v-else-if="media.kind === 2"
                :src="mediaUrls[media.id]"
                controls
                preload="metadata"
              />
              <a v-else :href="mediaUrls[media.id]" target="_blank" rel="noopener">下载文件</a>
              <figcaption>
                {{ media.fileName }} · {{ (media.size / 1024 / 1024).toFixed(1) }}MB
              </figcaption>
            </figure>
          </div>
        </section>

        <section class="semantic-card" :data-status="semantic?.status ?? item.semanticStatus">
          <div>
            <p class="section-label">AI 分析 · {{ semantic?.status ?? item.semanticStatus }}</p>
            <p v-if="semantic?.summary ?? item.semanticSummary">
              {{ semantic?.summary ?? item.semanticSummary }}
            </p>
            <p v-else>Worker 会在后台分析正文与图片，记录保存不需要等待。</p>
            <small v-if="semantic?.model"
              >{{ semantic.model }} · {{ semantic.pipelineVersion }}</small
            >
            <small v-if="semantic?.error" class="semantic-error">{{ semantic.error }}</small>
          </div>
          <button class="text-button" :disabled="reparsing" @click="reparse">
            {{ reparsing ? '已排队' : '重新分析' }}
          </button>
        </section>
        <section v-if="item.locations.length" class="semantic-card">
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
      </template>
    </main>

    <footer>
      <span>PassingTrace © 2026</span>
      <span>记录 · 个人时间线</span>
    </footer>
  </div>
</template>

<style scoped>
.detail-page {
  max-width: 880px;
  margin: 0 auto;
  padding: 48px 42px 96px;
}
.back-link {
  margin: 0 0 24px;
  font-size: 12px;
  color: rgba(36, 35, 31, 0.55);
}
.back-link a:hover {
  color: var(--red);
}
.detail-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
  margin-bottom: 12px;
}
.detail-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 11px;
  color: rgba(36, 35, 31, 0.55);
}
.detail-id {
  font-family: Georgia, serif;
  font-size: 13px;
  color: rgba(36, 35, 31, 0.45);
}
.detail-actions {
  display: flex;
  gap: 10px;
}
.detail-title {
  margin: 0 0 28px;
  font-family: 'Noto Serif SC', serif;
  font-size: clamp(28px, 4vw, 42px);
  font-weight: 500;
  line-height: 1.3;
  letter-spacing: -0.02em;
}
.source {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 16px 32px;
  margin: 0 0 36px;
  padding: 18px 20px;
  background: rgba(245, 240, 230, 0.55);
  border: 1px solid var(--line);
}
.source div {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 13px;
}
.source dt {
  font-size: 10px;
  letter-spacing: 0.14em;
  color: rgba(36, 35, 31, 0.5);
  text-transform: uppercase;
}
.source dd {
  margin: 0;
  font-family: 'Noto Serif SC', serif;
  font-size: 15px;
}
.source dd small {
  margin-left: 6px;
  font-family: 'DM Sans', sans-serif;
  color: rgba(36, 35, 31, 0.45);
  font-size: 11px;
}
.section-label {
  margin: 0 0 8px;
  font-size: 10px;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: rgba(36, 35, 31, 0.5);
}
.raw-content {
  margin-bottom: 36px;
}
.raw-content-body {
  margin: 0;
  font-family: 'Noto Serif SC', serif;
  font-size: 16px;
  line-height: 1.9;
  white-space: pre-wrap;
  word-break: break-word;
}
.raw-content-empty {
  margin: 0;
  color: rgba(36, 35, 31, 0.4);
  font-size: 13px;
}
.media-section {
  margin-bottom: 36px;
}
.media-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}
.media-grid figure {
  margin: 0;
  border: 1px solid var(--line);
  background: rgba(245, 240, 230, 0.42);
}
.media-grid img,
.media-grid video {
  display: block;
  width: 100%;
  max-height: 420px;
  object-fit: contain;
  background: #171713;
}
.media-grid figure.file {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 18px;
}
.media-grid figure.file a {
  color: var(--red);
}
.media-grid figcaption {
  padding: 8px 10px;
  color: rgba(36, 35, 31, 0.55);
  font-size: 11px;
}
.semantic-card {
  display: flex;
  justify-content: space-between;
  gap: 18px;
  margin-bottom: 36px;
  padding: 20px;
  border-left: 3px solid var(--sage);
  background: rgba(117, 129, 104, 0.08);
}
.semantic-card p {
  margin: 5px 0;
  line-height: 1.7;
}
.semantic-card small {
  display: block;
  color: rgba(36, 35, 31, 0.48);
}
.semantic-card .semantic-error {
  color: #b33225;
}
.source-meta {
  border-top: 1px solid var(--line);
  padding-top: 24px;
}
.source-meta dl {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 10px 24px;
  margin: 0;
  font-size: 12px;
}
.source-meta dt {
  color: rgba(36, 35, 31, 0.5);
}
.source-meta dd {
  margin: 0;
  font-family: Georgia, serif;
  color: rgba(36, 35, 31, 0.78);
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
  color: var(--red);
}
.badge.status[data-status='0'] {
  color: var(--sage);
}
.badge.status[data-status='1'] {
  color: #2e6a4a;
}
.badge.status[data-status='2'] {
  color: rgba(36, 35, 31, 0.4);
}
.button.ghost {
  background: transparent;
  color: var(--ink);
  border: 1px solid var(--line);
}
.button.danger-confirm {
  color: #fff;
  background: #b33225;
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
  .detail-page {
    padding: 32px 20px 60px;
  }
  .source,
  .source-meta dl {
    grid-template-columns: 1fr;
  }
  .media-grid {
    grid-template-columns: 1fr;
  }
  .detail-header {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>
