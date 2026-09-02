<script setup lang="ts">
import { Handle, Position, type NodeProps } from '@vue-flow/core'
import { EventKind } from '@/api/events-types'

interface StoryNodeData extends Record<string, unknown> {
  title: string
  summary?: string
  occurredAt?: string | null
  place?: string | null
  tags?: string[]
  imageUrl?: string
  kind: number
  temporary?: boolean
  revisionState?: string
  stageTitle?: string
}
defineProps<NodeProps<StoryNodeData>>()
</script>
<template>
  <article
    class="flow-record"
    :class="{
      'flow-record--selected': selected,
      'flow-record--plan': data.kind === EventKind.Plan,
      'flow-record--temporary': data.temporary,
    }"
  >
    <Handle type="target" :position="Position.Left" />
    <div v-if="data.imageUrl" class="flow-record__image">
      <img :src="data.imageUrl" :alt="`${data.title} 的记录图片`" />
    </div>
    <div class="flow-record__body">
      <p class="flow-record__meta">
        <span>{{
          data.temporary ? '待创建' : data.kind === EventKind.Plan ? '未来安排' : '已有记录'
        }}</span
        ><span v-if="data.revisionState === 'updated'" class="update-badge">内容已更新</span>
      </p>
      <h3>{{ data.title }}</h3>
      <p v-if="data.summary">{{ data.summary }}</p>
      <div class="flow-record__tags">
        <span v-if="data.stageTitle">{{ data.stageTitle }}</span
        ><span v-if="data.place">{{ data.place }}</span
        ><span v-for="tag in data.tags?.slice(0, 2)" :key="tag">{{ tag }}</span>
      </div>
    </div>
    <Handle type="source" :position="Position.Right" />
  </article>
</template>
<style scoped>
.flow-record {
  width: 260px;
  min-height: 126px;
  overflow: hidden;
  border: 1px solid var(--line-strong);
  border-radius: 16px;
  background: var(--surface);
  box-shadow: 0 5px 16px rgba(24, 48, 36, 0.08);
}
.flow-record--selected {
  border-color: var(--primary);
  box-shadow:
    0 0 0 3px var(--focus-color),
    var(--shadow-1);
}
.flow-record--plan {
  border-top: 3px solid var(--accent);
}
.flow-record--temporary {
  border-style: dashed;
}
.flow-record__image {
  height: 104px;
  overflow: hidden;
  background: var(--surface-soft);
}
.flow-record__image img {
  width: 100%;
  height: 100%;
  display: block;
  object-fit: cover;
}
.flow-record__body {
  padding: 13px 14px;
}
.flow-record__meta {
  margin: 0 0 6px;
  display: flex;
  gap: 6px;
  color: var(--primary-strong);
  font-size: 9px;
  font-weight: 800;
  letter-spacing: 0.06em;
}
.update-badge {
  color: var(--accent);
}
.flow-record h3 {
  margin: 0;
  font-size: 15px;
  line-height: 1.3;
}
.flow-record__body > p:not(.flow-record__meta) {
  margin: 6px 0 0;
  color: var(--ink-secondary);
  font-size: 10px;
  line-height: 1.45;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.flow-record__tags {
  margin-top: 9px;
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.flow-record__tags span {
  padding: 2px 6px;
  border-radius: 99px;
  color: var(--ink-tertiary);
  background: var(--surface-soft);
  font-size: 8px;
}
</style>
