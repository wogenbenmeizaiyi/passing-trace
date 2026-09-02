<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import WebAppHeader from '@/components/WebAppHeader.vue'
import { storylinesApi } from '@/api/storylines'
import { StorylineStatus, type StorylineRevisionResponse } from '@/api/storylines-types'

const route = useRoute()
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
    <main class="story-detail">
      <p v-if="error" class="error-banner">{{ error }}</p>
      <div v-else-if="loading" class="detail-loading"><span class="loading-ring"></span></div>
      <template v-else-if="item"
        ><header class="detail-hero">
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
            <RouterLink class="button button-primary" :to="`/storylines/${item.id}/edit`"
              >整理故事线</RouterLink
            >
          </div>
        </header>
        <section class="story-overview">
          <span>{{ item.nodes.length }} 个记录节点</span><span>{{ item.stages.length }} 个阶段</span
          ><span>修订 {{ item.revision }}</span
          ><span v-if="item.layoutState === 2">网页待排版</span>
        </section>
        <section class="vertical-story" aria-label="故事线纵向时间线">
          <div v-for="group in stages" :key="group.stage.key" class="stage-block">
            <header>
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
                :to="`/events/${entry.node.eventId}`"
                class="timeline-node"
                :class="{
                  'timeline-node--important': entry.node.emphasis === 2,
                  'timeline-node--deleted': entry.node.revisionState === 'deleted',
                }"
                ><span class="node-dot"></span>
                <div>
                  <p class="node-label">
                    <span v-if="entry.outline.startsBranch">分支</span
                    ><span v-if="entry.outline.isMerge"
                      >来自 {{ entry.outline.incomingCount }} 条路径</span
                    ><span v-if="entry.node.revisionState === 'updated'">内容已更新</span>
                  </p>
                  <h3>{{ entry.node.title }}</h3>
                  <p>{{ entry.node.rawContent || '这条记录没有正文。' }}</p>
                  <footer>
                    <span>{{ date(entry.node.occurredAt) }}</span
                    ><span v-if="entry.node.place">{{ entry.node.place }}</span>
                  </footer>
                </div></RouterLink
              >
            </div>
          </div>
          <div v-if="ungrouped.length" class="stage-block">
            <header>
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
                :to="`/events/${entry.node.eventId}`"
                class="timeline-node"
                ><span class="node-dot"></span>
                <div>
                  <h3>{{ entry.node.title }}</h3>
                  <p>{{ entry.node.rawContent || '这条记录没有正文。' }}</p>
                </div></RouterLink
              >
            </div>
          </div>
        </section></template
      >
    </main>
  </div>
</template>
<style scoped>
.story-detail {
  width: min(960px, calc(100% - 48px));
  margin: auto;
  padding: 58px 0 100px;
}
.detail-hero {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: 28px;
}
.detail-hero h1 {
  margin: 0;
  font-size: clamp(38px, 6vw, 68px);
  letter-spacing: -0.06em;
  line-height: 1;
}
.detail-hero > div > p:not(.eyebrow) {
  max-width: 650px;
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
.story-overview {
  margin: 36px 0 44px;
  padding: 15px 18px;
  display: flex;
  gap: 28px;
  border-block: 1px solid var(--line);
  color: var(--ink-tertiary);
  font-size: 12px;
}
.stage-block {
  display: grid;
  grid-template-columns: 180px 1fr;
  gap: 30px;
  margin-bottom: 44px;
}
.stage-block > header {
  display: flex;
  gap: 13px;
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
  position: relative;
  padding-left: 30px;
}
.stage-line:before {
  content: '';
  position: absolute;
  top: 13px;
  bottom: -30px;
  left: 7px;
  width: 1px;
  background: var(--line-strong);
}
.timeline-node {
  min-height: 140px;
  margin-bottom: 14px;
  padding: 20px 22px;
  display: block;
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
.timeline-node h3 {
  margin: 4px 0 8px;
  font-size: 19px;
}
.timeline-node > div > p:not(.node-label) {
  margin: 0;
  color: var(--ink-secondary);
  font-size: 13px;
}
.node-label {
  min-height: 18px;
  margin: 0;
  display: flex;
  gap: 5px;
}
.node-label span {
  padding: 2px 6px;
  border-radius: 5px;
  background: var(--accent-soft);
  color: var(--accent);
  font-size: 9px;
}
.timeline-node footer {
  margin-top: 17px;
  display: flex;
  justify-content: space-between;
  color: var(--ink-tertiary);
  font-size: 10px;
}
.detail-loading {
  min-height: 50vh;
  display: grid;
  place-items: center;
}
@media (max-width: 700px) {
  .story-detail {
    width: calc(100% - 32px);
    padding-top: 36px;
  }
  .detail-hero {
    align-items: start;
    flex-direction: column;
  }
  .stage-block {
    grid-template-columns: 1fr;
    gap: 12px;
  }
  .story-overview {
    overflow: auto;
  }
  .stage-line {
    padding-left: 24px;
  }
}
</style>
