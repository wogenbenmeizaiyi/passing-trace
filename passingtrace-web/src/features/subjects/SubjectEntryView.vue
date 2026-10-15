<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { subjectsApi, type Subject, type SubjectEntry, type SubjectValue } from '@/api/subjects'
import WebAppHeader from '@/components/WebAppHeader.vue'
import SubjectFields from './SubjectFields.vue'
import SubjectPicker from './SubjectPicker.vue'
import SubjectMediaEditor from './SubjectMediaEditor.vue'
import SubjectMedia from './SubjectMedia.vue'
import { subjectValue } from './subject-presentation'
import { defaultTimezone, toDatetimeLocal, toIsoWithOffset } from '@/utils/datetime'
import './subjects.css'
const route = useRoute(),
  router = useRouter()
const entryId = computed(() => route.params.entryId as string | undefined)
const subject = ref<Subject | null>(null),
  current = ref<SubjectEntry | null>(null),
  subjectId = ref(String(route.params.id ?? ''))
const title = ref(''),
  content = ref(''),
  kind = ref(0),
  when = ref(''),
  values = ref<Record<string, SubjectValue>>({}),
  actualValues = ref<Record<string, SubjectValue>>({}),
  actualWhen = ref(''),
  marked = ref<string[]>([]),
  mediaIds = ref<string[]>([]),
  busy = ref(false),
  ready = ref(false),
  error = ref(''),
  showComplete = ref(false)
const mediaEditor = ref<InstanceType<typeof SubjectMediaEditor> | null>(null),
  key = crypto.randomUUID()
const timezone = defaultTimezone()
async function run(action: () => Promise<unknown>) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    await action()
  } catch (e) {
    error.value = e instanceof Error ? e.message : '操作失败'
  } finally {
    busy.value = false
  }
}
async function save() {
  if (!ready.value || mediaEditor.value?.busy) return
  const at = when.value ? toIsoWithOffset(when.value, timezone) : undefined
  const body = {
    title: title.value,
    content: content.value,
    kind: kind.value,
    happenedAt: kind.value === 0 || current.value?.state === 1 ? at : undefined,
    plannedAt: kind.value === 1 && current.value?.state !== 1 ? at : undefined,
    clearPlannedAt: kind.value === 1 && current.value?.state !== 1 && !at,
    fieldChanges: values.value,
    markedSubjectIds: marked.value,
    mediaIds: mediaIds.value,
    timezone,
  }
  const result = current.value
    ? await subjectsApi.updateEntry(current.value.id, body, current.value.version)
    : await subjectsApi.createEntry(subjectId.value, body, key)
  await router.replace(`/subjects/entries/${result.id}`)
  current.value = result
}
async function decide(operation: string) {
  if (!current.value) return
  const actual = operation === 'complete' ? toIsoWithOffset(actualWhen.value, timezone) : null
  if (operation === 'complete' && !actual) throw new Error('请选择实际发生时间')
  current.value = await subjectsApi.decideEntry(
    current.value.id,
    {
      operation,
      happenedAt: actual ?? undefined,
      actualFieldChanges: operation === 'complete' ? actualValues.value : undefined,
    },
    current.value.version,
  )
  showComplete.value = false
  values.value = current.value.actualFieldChanges
  when.value = toDatetimeLocal(current.value.happenedAt)
}
async function remove() {
  const request = await subjectsApi.requestDelete(
    'SubjectEntry',
    current.value!.id,
    crypto.randomUUID(),
  )
  await router.push({ path: '/assistant', query: { conversation: request.conversationId } })
}
onMounted(async () => {
  try {
    if (entryId.value) {
      current.value = await subjectsApi.entry(entryId.value)
      const c = current.value
      subjectId.value = c.subjectId
      title.value = c.title
      content.value = c.content ?? ''
      kind.value = c.kind
      when.value = toDatetimeLocal(c.state === 1 ? c.happenedAt : c.plannedAt)
      values.value = { ...(c.kind === 1 && c.state === 1 ? c.actualFieldChanges : c.fieldChanges) }
      marked.value = [...c.markedSubjectIds]
      mediaIds.value = [...c.mediaIds]
    }
    if (!current.value?.sourceSubjectDeleted) subject.value = await subjectsApi.get(subjectId.value)
    ready.value = true
  } catch (e) {
    error.value = e instanceof Error ? e.message : '加载失败'
  }
})
</script>
<template>
  <div class="app-shell">
    <WebAppHeader />
    <main class="workspace-main subject-page">
      <RouterLink
        v-if="route.query.conversation"
        :to="{ path: '/assistant', query: { conversation: route.query.conversation } }"
        >← 返回聊天</RouterLink
      ><RouterLink v-else :to="`/subjects/${subjectId}`">← 返回人物时间轴</RouterLink>
      <h1>{{ current ? '编辑' : '新建' }}{{ kind === 1 ? '计划' : '记录' }}</h1>
      <p class="subject-muted">
        所属：{{ subject?.name ?? current?.subjectName }} · 显示在所属及标记人物的时间轴中。
      </p>
      <p v-if="error" class="error-banner" role="alert">{{ error }}</p>
      <p v-if="current?.sourceSubjectDeleted">所属档案已删除，内容与修订仍保留。</p>
      <section v-if="current?.sourceSubjectDeleted" class="subject-panel" aria-label="保留的记录">
        <p>
          {{ current.kind === 1 ? '计划' : '记录' }} ·
          {{ ['待执行', '已完成', '已取消'][current.state] }}
        </p>
        <p>{{ current.content }}</p>
        <p>{{ current.happenedAt ?? current.plannedAt }}</p>
        <dl class="subject-values">
          <div v-for="field in current.fieldDefinitions" :key="field.id">
            <dt>{{ field.name }}</dt>
            <dd>
              {{
                subjectValue(
                  (current.kind === 1 && current.state === 1
                    ? current.actualFieldChanges
                    : current.fieldChanges)[field.id],
                )
              }}
              {{ field.unit }}
            </dd>
          </div>
        </dl>
      </section>
      <form
        v-if="ready && !current?.sourceSubjectDeleted"
        class="subject-form"
        @submit.prevent="run(save)"
      >
        <section class="subject-panel">
          <h2>内容</h2>
          <label v-if="!current"
            >类型<select v-model="kind">
              <option :value="0">记录</option>
              <option :value="1">计划</option>
            </select></label
          >
          <p v-else>
            {{ current.kind === 1 ? '计划' : '记录' }} ·
            {{ ['待执行', '已完成', '已取消'][current.state] }}
          </p>
          <label>标题<input v-model="title" required maxlength="200" /></label
          ><label>正文<textarea v-model="content" /></label
          ><label
            >{{ kind === 1 && current?.state !== 1 ? '计划时间' : '实际发生时间'
            }}<input v-model="when" type="datetime-local"
          /></label>
        </section>
        <SubjectFields
          v-model="values"
          :fields="subject?.fields ?? []"
          :planned="kind === 1 && current?.state !== 1"
        /><SubjectPicker v-model="marked" :exclude="subjectId" /><SubjectMediaEditor
          ref="mediaEditor"
          v-model="mediaIds"
        />
        <footer class="subject-form-footer">
          <RouterLink class="button" :to="`/subjects/${subjectId}`">取消</RouterLink
          ><button class="button button-primary" :disabled="busy || mediaEditor?.busy">
            {{ busy ? '保存中…' : kind === 1 ? '保存计划' : '保存记录' }}
          </button>
        </footer>
      </form>
      <SubjectMedia v-for="m in current?.mediaIds" :id="m" :key="m" :title="title" />
      <div v-if="current" class="subject-actions">
        <template v-if="current.kind === 1 && current.state === 0 && !current.sourceSubjectDeleted"
          ><button class="button" :disabled="busy" @click="showComplete = !showComplete">
            完成计划</button
          ><button class="button" :disabled="busy" @click="run(() => decide('cancel'))">
            取消计划
          </button></template
        >
        <details class="subject-more">
          <summary class="button" aria-label="更多记录操作">更多</summary>
          <div class="subject-menu">
            <button class="button subject-danger" :disabled="busy" @click="run(remove)">
              申请删除{{ kind === 1 ? '计划' : '记录' }}
            </button>
          </div>
        </details>
      </div>
      <form
        v-if="showComplete"
        class="subject-form subject-panel"
        @submit.prevent="run(() => decide('complete'))"
      >
        <h2>确认实际发生情况</h2>
        <p>请填写实际值；预期值不会自动代入。</p>
        <label>实际日期<input v-model="actualWhen" type="datetime-local" required /></label
        ><SubjectFields v-model="actualValues" :fields="subject?.fields ?? []" />
        <div class="subject-actions">
          <button type="button" class="button" :disabled="busy" @click="showComplete = false">
            取消</button
          ><button class="button button-primary" :disabled="busy">确认完成</button>
        </div>
      </form>
    </main>
  </div>
</template>
