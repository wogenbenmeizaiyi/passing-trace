<script setup lang="ts">
import { computed, ref, useId } from 'vue'
import { RouterLink } from 'vue-router'
import type { SubjectGraph, SubjectRelation } from '@/api/subjects'
const props = defineProps<{ graph: SubjectGraph; subjectId?: string; busy?: boolean }>()
const emit = defineEmits<{
  add: []
  edit: [relation: SubjectRelation]
  toggle: [relation: SubjectRelation]
  remove: [relation: SubjectRelation]
}>()
const expanded = ref(false),
  query = ref(''),
  state = ref('')
const panelId = useId()
const relations = computed(() =>
  props.graph.relations.filter(
    (r) =>
      !props.subjectId || r.fromSubjectId === props.subjectId || r.toSubjectId === props.subjectId,
  ),
)
function name(id: string) {
  return props.graph.nodes.find((s) => s.id === id)?.name ?? '已删除档案'
}
const visible = computed(() =>
  relations.value.filter(
    (r) =>
      (!state.value || Boolean(r.endedAt) === (state.value === 'history')) &&
      `${name(r.fromSubjectId)} ${name(r.toSubjectId)} ${r.label}`.includes(query.value),
  ),
)
</script>
<template>
  <section class="subject-panel subject-relations" aria-label="人物关系">
    <header class="subject-section-heading">
      <button
        class="subject-disclosure"
        type="button"
        :aria-expanded="expanded"
        :aria-controls="panelId"
        @click="expanded = !expanded"
      >
        <span
          ><strong>人物关系</strong
          ><small
            >{{ relations.length }} 条关系<span v-if="relations.some((r) => r.endedAt)">
              · {{ relations.filter((r) => r.endedAt).length }} 条历史关系</span
            ></small
          ></span
        >
        <span aria-hidden="true">{{ expanded ? '⌃' : '⌄' }}</span>
      </button>
      <button
        v-if="subjectId"
        type="button"
        class="button button-secondary button-compact"
        :disabled="busy"
        @click="emit('add')"
      >
        ＋ 新增关联
      </button>
    </header>
    <div v-if="expanded" :id="panelId">
      <div class="subject-toolbar">
        <input
          v-model="query"
          type="search"
          aria-label="搜索名称或关系"
          placeholder="搜索名称或关系"
        />
        <select v-model="state" aria-label="关系范围">
          <option value="">全部关系</option>
          <option value="current">当前关系</option>
          <option value="history">历史关系</option>
        </select>
      </div>
      <ul class="subject-relation-list">
        <li v-for="r in visible" :key="r.id">
          <div class="subject-relation-copy">
            <template v-if="!subjectId"
              ><RouterLink :to="`/subjects/${r.fromSubjectId}`">{{
                name(r.fromSubjectId)
              }}</RouterLink>
              <span>{{ r.directed ? '→' : '—' }}</span>
              <RouterLink :to="`/subjects/${r.toSubjectId}`">{{
                name(r.toSubjectId)
              }}</RouterLink></template
            >
            <RouterLink
              v-else
              :to="`/subjects/${r.fromSubjectId === subjectId ? r.toSubjectId : r.fromSubjectId}`"
              >{{
                name(r.fromSubjectId === subjectId ? r.toSubjectId : r.fromSubjectId)
              }}</RouterLink
            >
            <small
              >{{ r.label
              }}{{
                subjectId && r.directed
                  ? r.fromSubjectId === subjectId
                    ? ' · 本档案指向对方'
                    : ' · 对方指向本档案'
                  : ''
              }}{{ r.endedAt ? ' · 历史关系' : '' }}</small
            >
          </div>
          <details v-if="subjectId" class="subject-more">
            <summary aria-label="管理关联" class="button button-compact">管理</summary>
            <div class="subject-menu">
              <button type="button" class="button" :disabled="busy" @click="emit('edit', r)">
                编辑关联</button
              ><button type="button" class="button" :disabled="busy" @click="emit('toggle', r)">
                {{ r.endedAt ? '恢复关系' : '结束关系' }}</button
              ><button
                type="button"
                class="button subject-danger"
                :disabled="busy"
                @click="emit('remove', r)"
              >
                移除误关联
              </button>
            </div>
          </details>
        </li>
      </ul>
      <p v-if="!visible.length" class="subject-muted">没有匹配的关系。</p>
    </div>
  </section>
</template>
