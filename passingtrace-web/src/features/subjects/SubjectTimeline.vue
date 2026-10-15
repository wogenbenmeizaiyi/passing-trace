<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import type { Timeline } from '@/api/subjects'
import { subjectValue } from './subject-presentation'
const props = defineProps<{
  subjectId: string
  timeline: Timeline | null
  groupBy: string
  busy?: boolean
}>()
const emit = defineEmits<{ more: [] }>()
const stateNames: Record<string, string> = {
  Planned: '待执行',
  Completed: '已完成',
  Cancelled: '已取消',
  Corrected: '已纠正',
}
const expanded = ref<Record<string, boolean>>({}),
  defaultExpanded = ref<boolean | null>(null)
function key(date: string) {
  return `${props.groupBy}/${date}`
}
function opened(date: string, index: number) {
  return expanded.value[key(date)] ?? defaultExpanded.value ?? index === 0
}
function all(value: boolean) {
  defaultExpanded.value = value
  for (const group of props.timeline?.groups ?? []) expanded.value[key(group.key)] = value
}
function changed(date: string, event: Event) {
  expanded.value[key(date)] = (event.target as HTMLDetailsElement).open
}
function save(id: string) {
  sessionStorage.setItem(
    `subject-timeline:${id}`,
    JSON.stringify({ expanded: expanded.value, defaultExpanded: defaultExpanded.value }),
  )
}
watch(
  () => props.subjectId,
  (id, previous) => {
    if (previous) save(previous)
    expanded.value = {}
    defaultExpanded.value = null
    try {
      const stored = JSON.parse(sessionStorage.getItem(`subject-timeline:${id}`) ?? 'null')
      if (stored) {
        expanded.value = Object.fromEntries(
          Object.entries(stored.expanded ?? {}).filter(([, v]) => typeof v === 'boolean'),
        ) as Record<string, boolean>
        defaultExpanded.value =
          typeof stored.defaultExpanded === 'boolean' ? stored.defaultExpanded : null
      }
    } catch {
      /* Ignore obsolete view state. */
    }
  },
  { immediate: true },
)
onBeforeUnmount(() => save(props.subjectId))
</script>
<template>
  <section aria-label="人物时间轴">
    <header class="subject-heading">
      <div>
        <h2>时间轴</h2>
        <p class="subject-muted">记录、计划与标记了此档案的内容，按时间汇集在这里。</p>
      </div>
      <RouterLink class="button button-primary" :to="`/subjects/${subjectId}/entries/new`"
        >＋ 追加记录／计划</RouterLink
      >
    </header>
    <div class="subject-timeline-tools">
      <details class="subject-filter-panel">
        <summary>筛选时间轴</summary>
        <div class="subject-toolbar subject-filter-grid"><slot name="filters" /></div>
      </details>
      <div class="subject-actions">
        <button type="button" class="text-button" @click="all(false)">全部收起</button
        ><button type="button" class="text-button" @click="all(true)">全部展开</button
        ><a class="text-button" href="#subject-profile">回到资料 ↑</a>
      </div>
    </div>
    <details
      v-for="(group, index) in timeline?.groups"
      :key="key(group.key)"
      class="subject-date-group"
      :open="opened(group.key, index)"
      @toggle="changed(group.key, $event)"
    >
      <summary>
        <strong>{{ group.key }}</strong
        ><span>{{ group.items.length }} 条内容</span>
      </summary>
      <div class="subject-timeline-items">
        <article
          v-for="item in group.items"
          :key="`${item.sourceType}:${item.sourceId}`"
          class="subject-timeline-item"
        >
          <div class="subject-badges">
            <span class="subject-badge">{{
              item.sourceType === 'Event'
                ? '原记录'
                : item.sourceType === 'Milestone'
                  ? '生命周期'
                  : '来自人物'
            }}</span
            ><span class="subject-badge"
              >{{ item.kind === 'Plan' ? '计划' : '记录' }} ·
              {{ stateNames[item.state] ?? item.state }}</span
            ><span v-if="item.afterEnd" class="subject-badge">结束期间</span>
          </div>
          <h3 v-if="item.invalid || item.sourceType === 'Milestone'">{{ item.title }}</h3>
          <h3 v-else>
            <RouterLink
              :to="
                item.sourceType === 'Event'
                  ? `/events/${item.sourceId}`
                  : `/subjects/entries/${item.sourceId}`
              "
              >{{ item.title }}</RouterLink
            >
          </h3>
          <small class="subject-muted"
            >{{ item.occurredAt ? new Date(item.occurredAt).toLocaleString() : '未定日期'
            }}{{ item.isReference ? ` · 引用自 ${item.originSubjectName}` : '' }}</small
          >
          <details v-if="item.content && item.content.length > 160" class="subject-body-disclosure">
            <summary>查看正文</summary>
            <p>{{ item.content }}</p>
          </details>
          <p v-else-if="item.content">{{ item.content }}</p>
          <dl
            v-if="item.fieldChanges && Object.keys(item.fieldChanges).length"
            class="subject-values"
          >
            <div v-for="(value, fieldId) in item.fieldChanges" :key="fieldId">
              <dt>{{ item.fieldDefinitions?.find((x) => x.id === fieldId)?.name }}</dt>
              <dd>
                {{ subjectValue(value) }}
                {{ item.fieldDefinitions?.find((x) => x.id === fieldId)?.unit }}
              </dd>
            </div>
          </dl>
        </article>
      </div>
    </details>
    <p v-if="timeline?.groups.length === 0" class="subject-panel subject-muted">
      还没有时间轴内容。
    </p>
    <button
      v-if="timeline?.nextCursor"
      class="button button-secondary"
      :disabled="busy"
      @click="emit('more')"
    >
      加载更多
    </button>
  </section>
</template>
