<script setup lang="ts">
import ShareDialog from '@/components/ShareDialog.vue'
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import WebAppHeader from '@/components/WebAppHeader.vue'
import { storylinesApi } from '@/api/storylines'
import { StorylineStatus, type StorylineRevisionResponse } from '@/api/storylines-types'
import { conversationIdFromQuery, recordFromConversation } from '@/utils/assistant-navigation'

const route = useRoute()
const shareOpen = ref(false)
const conversationId = computed(() => conversationIdFromQuery(route.query.conversation))
const conversationRoute = computed(() => ({
  path: '/assistant',
  query: { conversation: conversationId.value },
}))
const item = ref<StorylineRevisionResponse | null>(null)
const error = ref('')
const loading = ref(true)
const stages = computed(
  () =>
    item.value?.stages.map((stage) => ({
      stage,
      nodes: item
        .value!.outline.filter((x) => x.stageKey === stage.key)
        .sort((a, b) => a.topologicalOrder - b.topologicalOrder)
        .map((x) => ({ outline: x, node: item.value!.nodes.find((n) => n.key === x.nodeKey)! })),
    })) ?? [],
)
const ungrouped = computed(
  () =>
    item.value?.outline
      .filter((x) => !x.stageKey)
      .sort((a, b) => a.topologicalOrder - b.topologicalOrder)
      .map((x) => ({ outline: x, node: item.value!.nodes.find((n) => n.key === x.nodeKey)! })) ??
    [],
)
function date(value: string | null) {
  return value
    ? new Date(value).toLocaleString('zh-CN', {
        month: 'long',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      })
    : '时间未定'
}
onMounted(async () => {
  try {
    const revision = Number(route.params.revision)
    item.value =
      Number.isInteger(revision) && revision > 0
        ? await storylinesApi.revision(String(route.params.id), revision)
        : await storylinesApi.get(String(route.params.id))
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '加载失败。'
  } finally {
    loading.value = false
  }
})
</script>
<template>
  <div class="app-shell">
    <WebAppHeader />
    <main class="workspace-main story-detail">
      <p v-if="error" class="error-banner">{{ error }}</p>
      <div v-else-if="loading" class="detail-loading"><span class="loading-ring"></span></div>
      <template v-else-if="item">
        <ShareDialog v-if="shareOpen" :storyline-id="item.id" @close="shareOpen = false" />
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
          <RouterLink to="/storylines" :class="{ 'secondary-return': conversationId }">
            <svg v-if="!conversationId" class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
              <path d="m12 5-7 7 7 7M5 12h14" />
            </svg>
            {{ conversationId ? '查看故事线列表' : '返回故事线列表' }}
          </RouterLink>
        </nav>
        <header class="detail-hero">
          <div>
            <p class="eyebrow">
              {{ item.categoryLabel }} ·
              {{ item.status === StorylineStatus.Completed ? '已完成' : '正在发生' }}
            </p>
            <h1>{{ item.title }}</h1>
            <p>{{ item.description || '这段经历还没有补充说明。' }}</p>
            <div class="hero-tags">
              <span v-for="tag in item.tags" :key="tag">{{ tag }}</span>
            </div>
          </div>
          <div class="hero-actions">
            <button
              v-if="!route.params.revision"
              class="button button-secondary"
              @click="shareOpen = true"
            >
              分享给好友
            </button>
            <RouterLink class="button button-primary" :to="`/storylines/${item.id}/edit`"
              >整理故事线</RouterLink
            >
          </div>
        </header>
        <div class="story-workspace">
          <aside class="story-sidebar" aria-label="故事线概览">
            <section class="story-overview">
              <h2>经历概览</h2>
              <dl>
                <div>
                  <dt>记录节点</dt>
                  <dd>{{ item.nodes.length }} 个</dd>
                </div>
                <div>
                  <dt>阶段</dt>
                  <dd>{{ item.stages.length }} 个</dd>
                </div>
                <div>
                  <dt>当前版本</dt>
                  <dd>修订 {{ item.revision }}</dd>
                </div>
                <div v-if="item.rangeStart">
                  <dt>开始时间</dt>
                  <dd>{{ date(item.rangeStart) }}</dd>
                </div>
                <div v-if="item.rangeEnd">
                  <dt>结束时间</dt>
                  <dd>{{ date(item.rangeEnd) }}</dd>
                </div>
              </dl>
              <p v-if="item.layoutState === 2" class="arrangement-note">
                还有节点等待在网页画布中整理。
              </p>
            </section>
            <nav
              v-if="stages.length || ungrouped.length"
              class="stage-directory"
              aria-label="阶段目录"
            >
              <h2>阶段目录</h2>
              <a v-for="group in stages" :key="group.stage.key" :href="`#stage-${group.stage.key}`">
                <span class="stage-directory__number">{{
                  String(group.stage.semanticOrder + 1).padStart(2, '0')
                }}</span>
                <span>{{ group.stage.title }}</span>
                <small>{{ group.nodes.length }}</small>
              </a>
              <a v-if="ungrouped.length" href="#stage-ungrouped">
                <span class="stage-directory__number" aria-hidden="true">—</span>
                <span>未分组节点</span><small>{{ ungrouped.length }}</small>
              </a>
            </nav>
          </aside>
          <section class="vertical-story" aria-label="故事线纵向时间线">
            <div v-for="group in stages" :key="group.stage.key" class="stage-block">
              <header :id="`stage-${group.stage.key}`" tabindex="-1">
                <span>{{ String(group.stage.semanticOrder + 1).padStart(2, '0') }}</span>
                <div>
                  <h2>{{ group.stage.title }}</h2>
                  <p>{{ group.nodes.length }} 个节点</p>
                </div>
              </header>
              <div class="stage-line">
                <RouterLink
                  v-for="entry in group.nodes"
                  :key="entry.node.key"
                  :to="recordFromConversation(entry.node.eventId, conversationId)"
                  class="timeline-node"
                  :class="{
                    'timeline-node--important': entry.node.emphasis === 2,
                    'timeline-node--deleted': entry.node.revisionState === 'deleted',
                  }"
                  ><span class="node-dot"></span>
                  <div class="timeline-node__content">
                    <p class="node-label">
                      <span v-if="entry.outline.startsBranch">分支</span
                      ><span v-if="entry.outline.isMerge"
                        >来自 {{ entry.outline.incomingCount }} 条路径</span
                      ><span v-if="entry.node.revisionState === 'updated'">内容已更新</span>
                    </p>
                    <h3>{{ entry.node.title }}</h3>
                    <p>{{ entry.node.rawContent || '这条记录没有正文。' }}</p>
                  </div>
                  <footer class="timeline-node__meta">
                    <span>{{ date(entry.node.occurredAt) }}</span
                    ><span v-if="entry.node.place">{{ entry.node.place }}</span>
                  </footer>
                </RouterLink>
              </div>
            </div>
            <div v-if="ungrouped.length" class="stage-block">
              <header id="stage-ungrouped" tabindex="-1">
                <span>—</span>
                <div>
                  <h2>未分组节点</h2>
                  <p>可在编辑器中放入阶段</p>
                </div>
              </header>
              <div class="stage-line">
                <RouterLink
                  v-for="entry in ungrouped"
                  :key="entry.node.key"
                  :to="recordFromConversation(entry.node.eventId, conversationId)"
                  class="timeline-node"
                  ><span class="node-dot"></span>
                  <div class="timeline-node__content">
                    <h3>{{ entry.node.title }}</h3>
                    <p>{{ entry.node.rawContent || '这条记录没有正文。' }}</p>
                  </div>
                  <footer class="timeline-node__meta">
                    <span>{{ date(entry.node.occurredAt) }}</span>
                    <span v-if="entry.node.place">{{ entry.node.place }}</span>
                  </footer></RouterLink
                >
              </div>
            </div>
          </section>
        </div>
      </template>
    </main>
  </div>
</template>
<style scoped>
.back-link {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px 24px;
  margin-bottom: 16px;
  color: var(--ink-secondary);
  font-size: 13px;
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
.detail-hero {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: var(--workspace-gap);
  padding-bottom: 32px;
  border-bottom: 1px solid var(--line);
}
.detail-hero > div {
  min-width: 0;
  overflow-wrap: anywhere;
}
.hero-actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  flex-shrink: 0;
}
.detail-hero h1 {
  margin: 0;
  font-size: clamp(38px, 6vw, 68px);
  letter-spacing: -0.06em;
  line-height: 1;
}
.detail-hero > div > p:not(.eyebrow) {
  max-width: 70ch;
  color: var(--ink-secondary);
}
.hero-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.hero-tags span {
  padding: 4px 9px;
  border-radius: 99px;
  background: var(--primary-soft);
  color: var(--primary-strong);
  font-size: 11px;
}
.story-workspace {
  margin-top: var(--workspace-gap);
  display: grid;
  grid-template-columns: minmax(15rem, 23%) minmax(0, 1fr);
  align-items: start;
  gap: var(--workspace-gap);
}
.story-sidebar {
  min-width: 0;
  display: grid;
  gap: var(--workspace-gap);
}
.story-overview,
.stage-directory {
  min-width: 0;
  padding: clamp(16px, 1.5vw, 24px);
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  background: var(--surface);
}
.story-sidebar h2 {
  margin: 0 0 18px;
  font-size: 16px;
}
.story-overview dl {
  margin: 0;
  display: grid;
  gap: 14px;
}
.story-overview dl > div {
  display: flex;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 6px 16px;
  font-size: 12px;
}
.story-overview dt {
  color: var(--ink-secondary);
}
.story-overview dd {
  margin: 0;
  font-variant-numeric: tabular-nums;
  overflow-wrap: anywhere;
}
.arrangement-note {
  margin: 20px 0 0;
  padding-top: 16px;
  border-top: 1px solid var(--line);
  color: var(--ink-secondary);
  font-size: 12px;
}
.stage-directory a {
  min-height: 44px;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 12px;
  padding: 10px 8px;
  border-radius: var(--radius-sm);
  color: var(--ink-secondary);
  font-size: 13px;
  overflow-wrap: anywhere;
}
.stage-directory a:hover {
  color: var(--primary-strong);
  background: var(--primary-soft);
}
.stage-directory__number,
.stage-directory small {
  color: var(--ink-tertiary);
  font-size: 11px;
  font-variant-numeric: tabular-nums;
}
.vertical-story {
  min-width: 0;
}
.stage-block {
  display: grid;
  gap: 18px;
  margin-bottom: 36px;
}
.stage-block > header {
  min-width: 0;
  display: flex;
  gap: 13px;
  scroll-margin-top: 7rem;
  overflow-wrap: anywhere;
}
.stage-block > header > span {
  font-size: 12px;
  color: var(--accent);
  font-weight: 800;
}
.stage-block h2 {
  margin: 0;
  font-size: 19px;
}
.stage-block header p {
  margin: 4px 0;
  color: var(--ink-tertiary);
  font-size: 11px;
}
.stage-line {
  min-width: 0;
  position: relative;
  padding-left: 30px;
}
.stage-line:before {
  content: '';
  position: absolute;
  top: 13px;
  bottom: 32px;
  left: 7px;
  width: 1px;
  background: var(--line-strong);
}
.timeline-node {
  min-width: 0;
  min-height: 120px;
  margin-bottom: 14px;
  padding: clamp(16px, 1.5vw, 24px);
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(9rem, 23%);
  gap: var(--workspace-gap);
  position: relative;
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  background: var(--surface);
}
.timeline-node:hover {
  border-color: var(--primary);
}
.node-dot {
  width: 15px;
  height: 15px;
  position: absolute;
  top: 27px;
  left: -30px;
  border: 3px solid var(--canvas);
  border-radius: 50%;
  background: var(--primary);
}
.timeline-node--important {
  border-left: 4px solid var(--accent);
}
.timeline-node--deleted {
  opacity: 0.55;
}
.timeline-node__content {
  min-width: 0;
  overflow-wrap: anywhere;
}
.timeline-node h3 {
  margin: 4px 0 8px;
  font-size: 19px;
}
.timeline-node > div > p:not(.node-label) {
  margin: 0;
  max-width: 75ch;
  color: var(--ink-secondary);
  font-size: 13px;
}
.node-label {
  min-height: 18px;
  margin: 0;
  display: flex;
  flex-wrap: wrap;
  gap: 5px;
}
.node-label span {
  padding: 2px 6px;
  border-radius: 5px;
  background: var(--accent-soft);
  color: var(--accent);
  font-size: 9px;
}
.timeline-node__meta {
  min-width: 0;
  padding-left: clamp(16px, 1.5vw, 24px);
  display: flex;
  flex-direction: column;
  align-items: start;
  justify-content: center;
  gap: 8px;
  border-left: 1px solid var(--line);
  color: var(--ink-secondary);
  font-size: 12px;
  overflow-wrap: anywhere;
}
.detail-loading {
  min-height: 50vh;
  display: grid;
  place-items: center;
}
@media (max-width: 1100px) {
  .timeline-node {
    grid-template-columns: minmax(0, 1fr);
    gap: 16px;
  }
  .timeline-node__meta {
    padding: 0;
    border: 0;
    flex-direction: row;
    flex-wrap: wrap;
    justify-content: space-between;
  }
}
@media (max-width: 800px) {
  .detail-hero {
    align-items: start;
    flex-direction: column;
  }
  .story-workspace {
    grid-template-columns: minmax(0, 1fr);
  }
  .story-sidebar {
    grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr));
  }
  .stage-directory {
    display: none;
  }
  .stage-line {
    padding-left: 24px;
  }
  .stage-line:before {
    left: 1px;
  }
}
</style>
