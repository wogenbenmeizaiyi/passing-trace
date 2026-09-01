<script setup lang="ts">
import { RouterLink } from 'vue-router'

defineProps<{
  records: Array<{ eventId: number; title: string | null }>
}>()
</script>

<template>
  <details class="evidence-disclosure">
    <summary>
      <span class="evidence-label">
        <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M6 3h9l3 3v15H6V3Z" />
          <path d="M15 3v4h4M9 12h6M9 16h6" />
        </svg>
        相关记录
      </span>
      <span class="evidence-count">{{ records.length }} 条</span>
      <svg class="ui-icon evidence-chevron" viewBox="0 0 24 24" aria-hidden="true">
        <path d="m6 9 6 6 6-6" />
      </svg>
    </summary>
    <div class="evidence-list">
      <RouterLink v-for="record in records" :key="record.eventId" :to="`/events/${record.eventId}`">
        <strong>{{ record.title || '未命名记录' }}</strong>
        <small>打开记录</small>
      </RouterLink>
    </div>
  </details>
</template>

<style scoped>
.evidence-disclosure {
  margin-top: 16px;
  padding-top: 4px;
  border-top: 1px solid var(--line);
}
.evidence-disclosure summary {
  min-height: 44px;
  display: flex;
  align-items: center;
  gap: 8px;
  border-radius: var(--radius-sm);
  color: var(--ink-secondary);
  cursor: pointer;
  list-style: none;
  transition:
    color var(--motion-fast) var(--ease-out),
    background-color var(--motion-fast) var(--ease-out);
}
.evidence-disclosure summary::-webkit-details-marker {
  display: none;
}
.evidence-disclosure summary:hover {
  color: var(--primary-strong);
  background: var(--surface);
}
.evidence-label {
  min-width: 0;
  flex: 1;
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.04em;
}
.evidence-label .ui-icon {
  width: 18px;
  height: 18px;
  color: var(--accent);
}
.evidence-count {
  color: var(--ink-tertiary);
  font-size: 10px;
  white-space: nowrap;
}
.evidence-chevron {
  width: 17px;
  height: 17px;
  color: var(--ink-tertiary);
  transition: transform var(--motion-fast) var(--ease-out);
}
.evidence-disclosure[open] .evidence-chevron {
  transform: rotate(180deg);
}
.evidence-list {
  padding-top: 4px;
  display: grid;
  gap: 8px;
}
.evidence-list a {
  min-height: 48px;
  padding: 10px 12px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  color: var(--primary-strong);
  background: var(--surface);
}
.evidence-list a:hover {
  border-color: color-mix(in srgb, var(--primary) 36%, var(--line));
  background: var(--primary-soft);
}
.evidence-list a strong {
  min-width: 0;
  overflow: hidden;
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.evidence-list a small {
  color: var(--ink-tertiary);
  font-size: 9px;
  white-space: nowrap;
}
@media (prefers-reduced-motion: reduce) {
  .evidence-disclosure summary,
  .evidence-chevron {
    transition: none;
  }
}
</style>
