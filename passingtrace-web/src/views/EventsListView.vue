<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
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
import { useAuthStore } from '@/stores/auth'
import { formatLocal } from '@/utils/datetime'

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

function buildQuery(cursor: number | null) {
  return {
    limit: 20,
    cursor: cursor ?? undefined,
    kind: filterKind.value === '' ? undefined : (filterKind.value as EventKindT),
    status: filterStatus.value === '' ? undefined : (filterStatus.value as EventStatusT),
  }
}

async function reload() {
  if (activeController) activeController.abort()
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
    if (reason instanceof HttpError && reason.status === 401) {
      error.value = '会话已失效，请重新登录。'
    } else {
      error.value = reason instanceof Error ? reason.message : '加载记录失败。'
    }
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
    if (controller.signal.aborted) return
    error.value = reason instanceof Error ? reason.message : '加载更多失败。'
  } finally {
    if (!controller.signal.aborted) loadingMore.value = false
  }
}

function summary(item: EventResponse): string {
  if (item.rawContent) return item.rawContent
  if (item.title) return item.title
  return '（无正文）'
}

function timeLabel(item: EventResponse): string {
  if (item.kind === EventKind.Plan) return `计划：${formatLocal(item.plannedAt)}`
  return `发生：${formatLocal(item.happenedAt)}`
}

onMounted(() => {
  if (auth.isAuthenticated) reload()
})
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

    <main class="events-page">
      <section class="events-header">
        <div>
          <p class="eyebrow">EVENTS</p>
          <h1>我的记录</h1>
          <p class="events-lede">按时间倒序展示。点击进入可编辑、删除。</p>
        </div>
        <RouterLink
          v-if="auth.isAuthenticated"
          class="button button-accent compact-button"
          to="/events/new"
          >+ 新建记录</RouterLink
        >
      </section>

      <section class="events-filters" aria-label="筛选">
        <label class="filter-field"
          ><span>类型</span>
          <select v-model="filterKind" @change="reload">
            <option value="">全部</option>
            <option :value="EventKind.Trace">{{ EventKindLabel[EventKind.Trace] }}</option>
            <option :value="EventKind.Plan">{{ EventKindLabel[EventKind.Plan] }}</option>
          </select>
        </label>
        <label class="filter-field"
          ><span>状态</span>
          <select v-model="filterStatus" @change="reload">
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
          </select>
        </label>
        <button class="text-button refresh" :disabled="loading" @click="reload">刷新</button>
      </section>

      <p v-if="!auth.isAuthenticated" class="empty-state">
        请先
        <button class="inline-link inline-login" @click="auth.login">登录</button> 后查看你的记录。
      </p>
      <p v-else-if="error" class="error-banner" role="alert">
        {{ error }}<button @click="reload">重试</button>
      </p>
      <p v-else-if="loading && items.length === 0" class="empty-state">正在加载记录…</p>
      <p v-else-if="isEmpty" class="empty-state">
        还没有记录。<RouterLink to="/events/new">写下第一条</RouterLink>。
      </p>

      <ul v-else class="event-list">
        <li v-for="item in items" :key="item.id" class="event-item">
          <RouterLink :to="`/events/${item.id}`" class="event-link">
            <div class="event-meta">
              <span class="badge kind" :data-kind="item.kind">{{ EventKindLabel[item.kind] }}</span>
              <span class="badge status" :data-status="item.status">{{
                EventStatusLabel[item.status]
              }}</span>
              <time class="event-time">{{ timeLabel(item) }}</time>
            </div>
            <h2 class="event-title">{{ item.title ?? '（无标题）' }}</h2>
            <p class="event-summary">{{ summary(item) }}</p>
            <span class="event-foot">
              <span>#{{ item.id }}</span>
              <span>{{ item.timezone }}</span>
            </span>
          </RouterLink>
        </li>
      </ul>

      <div v-if="items.length > 0" class="events-footer">
        <button
          v-if="canLoadMore"
          class="button button-dark compact-button"
          :disabled="loadingMore"
          @click="loadMore"
        >
          {{ loadingMore ? '加载中…' : '加载更多' }}
        </button>
        <p v-else class="end-note">已经到底了。</p>
      </div>
    </main>

    <footer>
      <span>PassingTrace © 2026</span>
      <span>记录 · 个人时间线</span>
    </footer>
  </div>
</template>

<style scoped>
.events-page {
  max-width: 1280px;
  margin: 0 auto;
  padding: 64px 42px 96px;
}
.events-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 24px;
  margin-bottom: 36px;
}
.events-header h1 {
  margin: 4px 0 8px;
  font-family: 'Noto Serif SC', serif;
  font-size: 38px;
  font-weight: 500;
  letter-spacing: -0.02em;
}
.events-lede {
  margin: 0;
  color: rgba(36, 35, 31, 0.55);
  font-size: 13px;
}
.events-filters {
  display: flex;
  align-items: center;
  gap: 18px;
  margin-bottom: 18px;
  padding-bottom: 18px;
  border-bottom: 1px solid var(--line);
}
.filter-field {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: rgba(36, 35, 31, 0.7);
}
.filter-field select {
  border: 1px solid var(--line);
  background: var(--paper);
  padding: 6px 10px;
  font: inherit;
  font-size: 13px;
  min-width: 110px;
}
.refresh {
  margin-left: auto;
  color: var(--red);
  font-size: 13px;
}
.event-list {
  list-style: none;
  margin: 0;
  padding: 0;
  border-top: 1px solid var(--line);
}
.event-item {
  border-bottom: 1px solid var(--line);
}
.event-link {
  display: block;
  padding: 22px 4px;
  transition: background 0.18s;
}
.event-link:hover {
  background: rgba(245, 240, 230, 0.55);
}
.event-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 11px;
  color: rgba(36, 35, 31, 0.55);
  margin-bottom: 8px;
}
.event-time {
  margin-left: auto;
  font-size: 12px;
  color: rgba(36, 35, 31, 0.6);
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
.event-title {
  margin: 0 0 4px;
  font-family: 'Noto Serif SC', serif;
  font-size: 19px;
  font-weight: 500;
}
.event-summary {
  margin: 0;
  color: rgba(36, 35, 31, 0.62);
  font-size: 13px;
  line-height: 1.7;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.event-foot {
  display: flex;
  justify-content: space-between;
  margin-top: 8px;
  font-size: 11px;
  color: rgba(36, 35, 31, 0.4);
}
.events-footer {
  display: flex;
  justify-content: center;
  margin-top: 32px;
}
.end-note {
  color: rgba(36, 35, 31, 0.4);
  font-size: 12px;
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
  .events-page {
    padding: 40px 20px 60px;
  }
  .events-header {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>
