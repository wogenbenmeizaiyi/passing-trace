<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'

import { eventsApi } from '@/api/events'
import { HttpError } from '@/api/http-client'
import {
  EventKind,
  EventKindLabel,
  EventStatus,
  EventStatusLabel,
  type EventKind as EventKindT,
  type EventResponse,
  type EventStatus as EventStatusT,
} from '@/api/events-types'
import WebAppHeader from '@/components/WebAppHeader.vue'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const items = ref<EventResponse[]>([])
const nextCursor = ref<number | null>(null)
const loading = ref(false)
const loadingMore = ref(false)
const error = ref<string | null>(null)
const filterKind = ref<EventKindT | ''>('')
const filterStatus = ref<EventStatusT | ''>('')
let activeController: AbortController | null = null

const canLoadMore = computed(
  () => nextCursor.value !== null && !loading.value && !loadingMore.value,
)
const isEmpty = computed(() => !loading.value && items.value.length === 0)
const monthLabel = computed(() => {
  const first = items.value[0]
  const date = first ? eventDate(first) : new Date()
  return date.toLocaleDateString('zh-CN', { year: 'numeric', month: 'long' })
})
const groupedItems = computed(() => {
  const groups = new Map<
    string,
    { key: string; title: string; subtitle: string; items: EventResponse[] }
  >()
  for (const item of items.value) {
    const date = eventDate(item)
    const key = Number.isNaN(date.getTime()) ? 'unknown' : date.toLocaleDateString('en-CA')
    let group = groups.get(key)
    if (!group) {
      group = {
        key,
        title: dayTitle(date),
        subtitle: Number.isNaN(date.getTime())
          ? '未指定日期'
          : date.toLocaleDateString('zh-CN', { month: 'long', day: 'numeric', weekday: 'short' }),
        items: [],
      }
      groups.set(key, group)
    }
    group.items.push(item)
  }
  return [...groups.values()]
})

function buildQuery(cursor: number | null) {
  return {
    limit: 20,
    cursor: cursor ?? undefined,
    kind: filterKind.value === '' ? undefined : filterKind.value,
    status: filterStatus.value === '' ? undefined : filterStatus.value,
  }
}

async function reload() {
  activeController?.abort()
  const controller = new AbortController()
  activeController = controller
  loading.value = true
  error.value = null
  try {
    const page = await eventsApi.list(buildQuery(null), { signal: controller.signal })
    if (controller.signal.aborted) return
    items.value = page.items
    nextCursor.value = page.nextCursor
  } catch (reason) {
    if (controller.signal.aborted) return
    error.value =
      reason instanceof HttpError && reason.status === 401
        ? '会话已失效，请重新登录。'
        : reason instanceof Error
          ? reason.message
          : '加载记录失败。'
    items.value = []
    nextCursor.value = null
  } finally {
    if (!controller.signal.aborted) loading.value = false
  }
}

async function loadMore() {
  if (!canLoadMore.value) return
  const controller = new AbortController()
  activeController = controller
  loadingMore.value = true
  error.value = null
  try {
    const page = await eventsApi.list(buildQuery(nextCursor.value), { signal: controller.signal })
    if (controller.signal.aborted) return
    items.value = [...items.value, ...page.items]
    nextCursor.value = page.nextCursor
  } catch (reason) {
    if (!controller.signal.aborted)
      error.value = reason instanceof Error ? reason.message : '加载更多失败。'
  } finally {
    if (!controller.signal.aborted) loadingMore.value = false
  }
}

function eventDate(item: EventResponse) {
  return new Date(
    (item.kind === EventKind.Plan ? item.plannedAt : item.happenedAt) ?? item.createdAt,
  )
}

function dayTitle(date: Date) {
  if (Number.isNaN(date.getTime())) return '未定'
  const today = new Date()
  const candidate = new Date(date.getFullYear(), date.getMonth(), date.getDate())
  const current = new Date(today.getFullYear(), today.getMonth(), today.getDate())
  const difference = Math.round((current.getTime() - candidate.getTime()) / 86_400_000)
  if (difference === 0) return '今天'
  if (difference === 1) return '昨天'
  return `${date.getDate()} 日`
}

function timeLabel(item: EventResponse) {
  const date = eventDate(item)
  return Number.isNaN(date.getTime())
    ? '时间未定'
    : date.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })
}

function summary(item: EventResponse) {
  return item.rawContent || item.semanticSummary || '这条记录没有填写正文。'
}

onMounted(() => {
  if (auth.isAuthenticated) void reload()
})
watch(
  () => auth.isAuthenticated,
  (authenticated) => {
    if (authenticated && items.value.length === 0) void reload()
  },
)
onUnmounted(() => activeController?.abort())
</script>

<template>
  <div class="app-shell">
    <WebAppHeader />

    <main class="records-page">
      <header class="records-heading">
        <div>
          <p class="eyebrow">YOUR TIMELINE</p>
          <h1>{{ monthLabel }}</h1>
          <p>按时间收好每一条痕迹和计划。</p>
        </div>
        <RouterLink v-if="auth.isAuthenticated" class="button button-primary" to="/events/new">
          <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
            <path d="M12 5v14M5 12h14" />
          </svg>
          记一笔
        </RouterLink>
      </header>

      <section class="record-toolbar" aria-label="记录筛选">
        <label
          ><span>类型</span
          ><select v-model="filterKind" @change="reload">
            <option value="">全部</option>
            <option :value="EventKind.Trace">{{ EventKindLabel[EventKind.Trace] }}</option>
            <option :value="EventKind.Plan">{{ EventKindLabel[EventKind.Plan] }}</option>
          </select></label
        >
        <label
          ><span>状态</span
          ><select v-model="filterStatus" @change="reload">
            <option value="">全部</option>
            <option :value="EventStatus.Planned">
              {{ EventStatusLabel[EventStatus.Planned] }}
            </option>
            <option :value="EventStatus.Completed">
              {{ EventStatusLabel[EventStatus.Completed] }}
            </option>
            <option :value="EventStatus.Cancelled">
              {{ EventStatusLabel[EventStatus.Cancelled] }}
            </option>
          </select></label
        >
        <button class="toolbar-refresh" :disabled="loading" @click="reload">
          <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
            <path d="M20 11a8 8 0 1 0-2.3 5.7M20 5v6h-6" />
          </svg>
          刷新
        </button>
      </section>

      <section v-if="!auth.isAuthenticated" class="empty-panel">
        <h2>登录后查看你的时间线</h2>
        <p>网页端会跳转到安全登录页，并支持手机扫码批准。</p>
        <button class="button button-primary" @click="auth.login('/events')">扫码登录</button>
      </section>
      <section v-else-if="error" class="error-banner" role="alert">
        <span>{{ error }}</span
        ><button @click="reload">重试</button>
      </section>
      <section v-else-if="loading && items.length === 0" class="empty-panel" aria-live="polite">
        <span class="loading-ring" aria-hidden="true"></span>
        <h2>正在整理时间线</h2>
      </section>
      <section v-else-if="isEmpty" class="empty-panel">
        <h2>记忆盒还是空的</h2>
        <p>从一段文字、一张照片或一个地点开始。</p>
        <RouterLink class="button button-primary" to="/events/new">写下第一条</RouterLink>
      </section>

      <div v-else class="day-groups">
        <section v-for="group in groupedItems" :key="group.key" class="day-group">
          <header class="day-heading">
            <strong>{{ group.title }}</strong
            ><span>{{ group.subtitle }}</span>
          </header>
          <ul class="record-timeline">
            <li v-for="item in group.items" :key="item.id">
              <RouterLink class="record-card" :to="`/events/${item.id}`">
                <div class="record-card__meta">
                  <span>{{ timeLabel(item) }}</span>
                  <span>{{ EventKindLabel[item.kind] }} · {{ EventStatusLabel[item.status] }}</span>
                </div>
                <h2>{{ item.title || '未命名记录' }}</h2>
                <p>{{ summary(item) }}</p>
                <footer class="record-card__footer">
                  <span class="record-tags">
                    <span
                      v-if="item.effectiveClassification.primaryCategory"
                      class="record-tag record-tag--category"
                      >{{ item.effectiveClassification.primaryCategory.displayName }}</span
                    >
                    <span
                      v-for="tag in item.effectiveClassification.tags.slice(0, 2)"
                      :key="tag.taxonomyKey ?? tag.displayName"
                      class="record-tag"
                      >{{ tag.origin === 'ai' ? '✦ ' : '' }}{{ tag.displayName }}</span
                    >
                  </span>
                  <span class="record-context">
                    <span v-if="item.locations[0]">{{ item.locations[0].name }}</span>
                    <span v-if="item.media.length">{{ item.media.length }} 个附件</span>
                  </span>
                </footer>
              </RouterLink>
            </li>
          </ul>
        </section>
      </div>

      <div v-if="items.length" class="load-more">
        <button
          v-if="canLoadMore"
          class="button button-secondary"
          :disabled="loadingMore"
          @click="loadMore"
        >
          {{ loadingMore ? '加载中…' : '加载更多' }}
        </button>
        <p v-else>已经看到这段时间线的起点。</p>
      </div>
    </main>

    <footer><span>PassingTrace © 2026</span><span>记录 · 理解 · 回望</span></footer>
  </div>
</template>

<style scoped>
.records-page {
  width: min(1040px, calc(100% - 48px));
  margin: 0 auto;
  padding: 64px 0 104px;
}
.records-heading {
  margin-bottom: 34px;
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 24px;
}
.records-heading h1 {
  margin: 0;
  font-size: clamp(36px, 5vw, 54px);
  line-height: 1.12;
  letter-spacing: -0.055em;
}
.records-heading div > p:last-child {
  margin: 10px 0 0;
  color: var(--ink-secondary);
}
.record-toolbar {
  min-height: 72px;
  margin-bottom: 44px;
  padding: 12px 16px;
  display: flex;
  align-items: center;
  gap: 12px;
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-1);
}
.record-toolbar label {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--ink-tertiary);
  font-size: 12px;
}
.record-toolbar select {
  min-height: 42px;
  padding: 0 36px 0 12px;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  color: var(--ink);
  background: var(--surface-soft);
}
.toolbar-refresh {
  min-height: 44px;
  margin-left: auto;
  padding: 0 10px;
  display: flex;
  align-items: center;
  gap: 7px;
  border: 0;
  border-radius: var(--radius-md);
  color: var(--primary-strong);
  background: transparent;
}
.toolbar-refresh:hover {
  background: var(--primary-soft);
}
.toolbar-refresh .ui-icon {
  width: 18px;
  height: 18px;
}
.day-groups {
  display: grid;
  gap: 42px;
}
.day-heading {
  margin: 0 0 14px 26px;
  display: flex;
  align-items: baseline;
  gap: 9px;
}
.day-heading strong {
  font-size: 17px;
}
.day-heading span {
  color: var(--ink-tertiary);
  font-size: 12px;
}
.record-timeline {
  margin: 0;
  padding: 0 0 0 26px;
  position: relative;
  display: grid;
  gap: 14px;
  list-style: none;
}
.record-timeline::before {
  content: '';
  position: absolute;
  top: 10px;
  bottom: 10px;
  left: 5px;
  width: 1px;
  background: var(--line-strong);
}
.record-timeline li {
  position: relative;
}
.record-timeline li::before {
  content: '';
  position: absolute;
  z-index: 1;
  top: 25px;
  left: -25px;
  width: 9px;
  height: 9px;
  border: 3px solid var(--canvas);
  border-radius: 50%;
  background: var(--primary);
  box-shadow: 0 0 0 1px var(--primary);
}
.record-card {
  padding: 20px 22px;
  display: block;
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-1);
  transition:
    border-color var(--motion-fast),
    background var(--motion-fast);
}
.record-card:hover {
  border-color: color-mix(in srgb, var(--primary) 45%, var(--line));
  background: var(--surface-tint);
}
.record-card__meta {
  margin-bottom: 9px;
  display: flex;
  justify-content: space-between;
  gap: 16px;
  color: var(--ink-tertiary);
  font-size: 11px;
}
.record-card h2 {
  margin: 0;
  font-size: 18px;
  letter-spacing: -0.02em;
}
.record-card > p {
  margin: 8px 0 0;
  overflow: hidden;
  color: var(--ink-secondary);
  font-size: 13px;
  line-height: 1.65;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}
.record-card__footer {
  width: auto;
  min-height: 0;
  margin: 15px 0 0;
  padding: 0;
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 16px;
  border: 0;
}
.record-tags {
  min-width: 0;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.record-tag {
  min-height: 26px;
  padding: 3px 9px;
  display: inline-flex;
  align-items: center;
  border: 1px solid var(--line);
  border-radius: 999px;
  color: var(--ink-secondary);
  background: var(--surface-soft);
  font-size: 10px;
}
.record-tag--category {
  border-color: transparent;
  color: var(--primary-strong);
  background: var(--primary-soft);
  font-weight: 700;
}
.record-context {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 10px;
  color: var(--ink-tertiary);
  font-size: 10px;
}
.empty-panel {
  min-height: 300px;
  padding: 48px 24px;
  display: grid;
  place-items: center;
  align-content: center;
  gap: 10px;
  border: 1px dashed var(--line-strong);
  border-radius: var(--radius-xl);
  text-align: center;
  background: color-mix(in srgb, var(--surface) 68%, transparent);
}
.empty-panel h2,
.empty-panel p {
  margin: 0;
}
.empty-panel h2 {
  font-size: 22px;
}
.empty-panel p {
  margin-bottom: 12px;
  color: var(--ink-secondary);
}
.loading-ring {
  width: 38px;
  height: 38px;
  margin-bottom: 8px;
  border: 2px solid var(--primary-soft);
  border-top-color: var(--primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
.load-more {
  margin-top: 40px;
  text-align: center;
}
.load-more p {
  color: var(--ink-tertiary);
  font-size: 12px;
}
@media (max-width: 700px) {
  .records-page {
    width: calc(100% - 32px);
    padding: 40px 0 72px;
  }
  .records-heading {
    align-items: flex-start;
    flex-direction: column;
  }
  .record-toolbar {
    align-items: stretch;
    flex-direction: column;
  }
  .record-toolbar label {
    justify-content: space-between;
  }
  .record-toolbar select {
    flex: 1;
  }
  .toolbar-refresh {
    margin-left: 0;
    justify-content: center;
  }
  .record-card__footer {
    align-items: flex-start;
    flex-direction: column;
  }
  .record-context {
    justify-content: flex-start;
  }
}
</style>
