<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { subjectsApi, type Subject } from '@/api/subjects'
import SubjectDialog from './SubjectDialog.vue'
import { subjectType } from './subject-presentation'
import './subjects.css'
const model = defineModel<string[]>({ default: () => [] })
const props = defineProps<{ exclude?: string }>()
const subjects = ref<Subject[]>([]),
  error = ref(''),
  loading = ref(false),
  open = ref(false),
  query = ref(''),
  kind = ref(''),
  draft = ref<string[]>([])
const visible = computed(() =>
  subjects.value.filter(
    (x) =>
      x.id !== props.exclude &&
      x.name.includes(query.value) &&
      (!kind.value || String(x.kind) === kind.value),
  ),
)
function name(id: string) {
  const subject = subjects.value.find((s) => s.id === id)
  return subject?.isSelf ? '自己' : (subject?.name ?? '不可用档案')
}
async function load() {
  loading.value = true
  error.value = ''
  try {
    subjects.value = await subjectsApi.list()
  } catch (e) {
    error.value = e instanceof Error ? e.message : '人物列表加载失败'
  } finally {
    loading.value = false
  }
}
function choose() {
  draft.value = [...model.value]
  query.value = ''
  kind.value = ''
  open.value = true
}
function apply() {
  model.value = [...draft.value]
  open.value = false
}
onMounted(load)
</script>
<template>
  <fieldset class="subject-panel subject-picker">
    <legend>标记人物</legend>
    <p class="subject-muted">这条内容也会显示在标记人物的时间轴中。</p>
    <div class="subject-actions">
      <button type="button" class="button button-secondary" @click="choose">
        选择人物<span v-if="model.length"> · {{ model.length }} 个</span>
      </button>
    </div>
    <div v-if="model.length" class="subject-badges">
      <button
        v-for="id in model"
        :key="id"
        type="button"
        class="subject-badge text-button"
        :aria-label="`移除标记 ${name(id)}`"
        @click="model = model.filter((x) => x !== id)"
      >
        {{ name(id) }} ×
      </button>
    </div>
    <SubjectDialog v-model="open" title="标记人物">
      <div class="subject-toolbar">
        <input
          v-model="query"
          type="search"
          aria-label="搜索可标记人物"
          placeholder="搜索人物、宠物或物品"
          @keydown.enter.prevent
        /><select v-model="kind" aria-label="筛选可标记人物类型">
          <option value="">全部类型</option>
          <option value="0">人</option>
          <option value="1">宠物</option>
          <option value="2">物品</option>
        </select>
      </div>
      <p v-if="loading" role="status">正在加载人物…</p>
      <p v-if="error" class="error-banner" role="alert">
        {{ error }} <button class="text-button" type="button" @click="load">重试</button>
      </p>
      <div class="subject-form">
        <label v-for="item in visible" :key="item.id"
          ><input v-model="draft" type="checkbox" :value="item.id" @keydown.enter.prevent />{{
            name(item.id)
          }}<small class="subject-muted">{{ subjectType(item) }}</small></label
        >
      </div>
      <p v-if="!loading && !error && !visible.length" class="subject-muted">没有匹配的档案。</p>
      <template #footer
        ><button type="button" class="button button-secondary" @click="open = false">取消</button
        ><button
          type="button"
          class="button button-primary"
          :disabled="loading || Boolean(error)"
          @click="apply"
        >
          确定<span v-if="draft.length"> · {{ draft.length }} 个</span>
        </button></template
      >
    </SubjectDialog>
  </fieldset>
</template>
