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

function activateHandle(event: KeyboardEvent) {
  const handle = event.currentTarget as HTMLElement
  const bounds = handle.getBoundingClientRect()
  handle.dispatchEvent(
    new MouseEvent('click', {
      bubbles: true,
      clientX: bounds.left + bounds.width / 2,
      clientY: bounds.top + bounds.height / 2,
    }),
  )
}
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
    <Handle
      type="target"
      :position="Position.Left"
      class="flow-record__handle"
      role="button"
      tabindex="0"
      :aria-label="`连接到：${data.title}`"
      title="连接到这里 · 点击或拖动连线"
      @click.stop
      @keydown.enter.prevent.stop="activateHandle"
      @keydown.space.prevent.stop="activateHandle"
    />
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
    <Handle
      type="source"
      :position="Position.Right"
      class="flow-record__handle"
      role="button"
      tabindex="0"
      :aria-label="`从这里连接：${data.title}`"
      title="从这里连接 · 点击后选择下一个节点"
      @click.stop
      @keydown.enter.prevent.stop="activateHandle"
      @keydown.space.prevent.stop="activateHandle"
    />
  </article>
</template>
<style scoped>
.flow-record {
  width: 260px;
  min-height: 126px;
  overflow: visible;
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
  border-radius: 15px 15px 0 0;
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
.flow-record__handle {
  /* Keep a generous hit area without covering the card's text. */
  --connection-dot-size: calc(20px * 2 / 3);
  width: var(--connection-dot-size);
  height: var(--connection-dot-size);
  border: 0;
  border-radius: 50%;
  background: transparent;
  cursor: crosshair;
  display: grid;
  place-items: center;
}
.flow-record__handle::before {
  content: '';
  position: absolute;
  width: 40px;
  height: 40px;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  border-radius: 50%;
  pointer-events: all;
}
.flow-record__handle::after {
  content: '';
  width: 100%;
  height: 100%;
  box-sizing: border-box;
  border: calc(2px * 2 / 3) solid var(--surface);
  border-radius: 50%;
  background: var(--primary);
  box-shadow: 0 0 0 1px var(--primary-strong);
  pointer-events: none;
  transition:
    box-shadow 150ms ease,
    background 150ms ease;
}
.flow-record__handle:hover::after,
.flow-record__handle:focus-visible::after,
.flow-record__handle.connecting::after,
.flow-record__handle.valid::after {
  background: var(--primary-strong);
  box-shadow: 0 0 0 3px var(--focus-color);
}
.flow-record__handle:focus-visible {
  outline: 2px solid var(--primary-strong);
  outline-offset: 2px;
}
@media (prefers-reduced-motion: reduce) {
  .flow-record__handle::after {
    transition: none;
  }
}
</style>
