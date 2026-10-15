<script setup lang="ts">
import { computed, onMounted, ref, useId, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import {
  subjectsApi,
  type Subject,
  type SubjectGraph,
  type SubjectEntry,
  type Timeline,
  type LifecycleInput,
  type SubjectRelation,
} from '@/api/subjects'
import { useProfileStore } from '@/stores/profile'
import WebAppHeader from '@/components/WebAppHeader.vue'
import SubjectAvatar from './SubjectAvatar.vue'
import SubjectDialog from './SubjectDialog.vue'
import SubjectRelations from './SubjectRelations.vue'
import SubjectTimeline from './SubjectTimeline.vue'
import SubjectMedia from './SubjectMedia.vue'
import { subjectType, subjectValue } from './subject-presentation'
import { defaultTimezone, toIsoWithOffset, toDatetimeLocal } from '@/utils/datetime'
import './subjects.css'
const route = useRoute(),
  router = useRouter(),
  profile = useProfileStore()
const id = computed(() => String(route.params.id))
const subject = ref<Subject | null>(null),
  graph = ref<SubjectGraph | null>(null),
  timeline = ref<Timeline | null>(null)
const error = ref(''),
  busy = ref(false),
  groupBy = ref('month'),
  kind = ref(''),
  state = ref(''),
  from = ref(''),
  to = ref('')
const relationTarget = ref(''),
  relationLabel = ref(''),
  directed = ref(false),
  relationStart = ref(''),
  relationEnd = ref(''),
  reverseRelation = ref(false)
const relationDialog = ref(false),
  editingRelation = ref<SubjectRelation | null>(null)
const lifecycle = ref(false),
  lifeOperation = ref('end'),
  lifeAt = ref(''),
  lifeReason = ref('other'),
  lifeNote = ref(''),
  undoEnd = ref(false)
const pendingPlans = ref<SubjectEntry[]>([]),
  cancelPlanIds = ref<string[]>([]),
  currentEndId = ref<string>()
const relationFormId = useId(),
  lifeFormId = useId(),
  moreMenu = ref<HTMLDetailsElement | null>(null)
const relationDirection = computed({
  get: () => (!directed.value ? 'none' : reverseRelation.value ? 'incoming' : 'outgoing'),
  set: (value: string) => {
    directed.value = value !== 'none'
    reverseRelation.value = value === 'incoming'
  },
})
const reasons: Record<string, string> = {
  deceased: '离世',
  'relationship-ended': '关系结束',
  sold: '出售',
  gifted: '转赠',
  lost: '丢失',
  scrapped: '报废',
  other: '其他',
}
const lifeTitle = computed(
  () =>
    ({ end: '结束人物档案', resume: '恢复人物档案', correct: '纠正结束信息' })[
      lifeOperation.value as 'end'
    ] ?? '档案状态',
)
function cancelRelationEdit() {
  relationDialog.value = false
  editingRelation.value = null
  relationLabel.value = ''
  relationTarget.value = ''
  relationStart.value = ''
  relationEnd.value = ''
  reverseRelation.value = false
  directed.value = false
}
function newRelation() {
  cancelRelationEdit()
  relationTarget.value = graph.value?.nodes.find((s) => s.isSelf && s.id !== id.value)?.id ?? ''
  relationDialog.value = true
}
function editRelation(relation: SubjectRelation) {
  editingRelation.value = relation
  reverseRelation.value = relation.toSubjectId === id.value
  relationTarget.value = reverseRelation.value ? relation.fromSubjectId : relation.toSubjectId
  relationLabel.value = relation.label
  directed.value = relation.directed
  relationStart.value = toDatetimeLocal(relation.startedAt)
  relationEnd.value = toDatetimeLocal(relation.endedAt)
  relationDialog.value = true
}
let generation = 0
async function load() {
  const ticket = ++generation
  try {
    const [s, g, t] = await Promise.all([
      subjectsApi.get(id.value),
      subjectsApi.graph(),
      subjectsApi.timeline(id.value, filters()),
    ])
    if (ticket === generation) {
      subject.value = s
      graph.value = g
      timeline.value = t
    }
  } catch (e) {
    if (ticket === generation) error.value = e instanceof Error ? e.message : '加载失败'
  }
}
function filters() {
  return {
    groupBy: groupBy.value,
    timezone: defaultTimezone(),
    kind: kind.value || undefined,
    state: state.value || undefined,
    from: from.value ? new Date(`${from.value}T00:00:00`).toISOString() : undefined,
    to: to.value ? new Date(`${to.value}T23:59:59.999`).toISOString() : undefined,
  }
}
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
async function more() {
  if (!timeline.value?.nextCursor) return
  const next = await subjectsApi.timeline(id.value, {
    ...filters(),
    cursor: timeline.value.nextCursor,
  })
  for (const group of next.groups) {
    const existing = timeline.value.groups.find((x) => x.key === group.key)
    if (existing) {
      const ids = new Set(existing.items.map((x) => `${x.sourceType}:${x.sourceId}`))
      existing.items.push(...group.items.filter((x) => !ids.has(`${x.sourceType}:${x.sourceId}`)))
    } else timeline.value.groups.push(group)
  }
  timeline.value.nextCursor = next.nextCursor
}
async function remove(type: string, targetId: string) {
  const request = await subjectsApi.requestDelete(type, targetId, crypto.randomUUID())
  await router.push({ path: '/assistant', query: { conversation: request.conversationId } })
}
async function toggleRelation(relation: SubjectRelation) {
  await subjectsApi.updateRelation(
    relation.id,
    relation.endedAt ? { resume: true } : { endedAt: new Date().toISOString() },
    relation.revision,
  )
  await load()
}
async function relate() {
  if (!subject.value) return
  if (relationStart.value && relationEnd.value && relationEnd.value < relationStart.value)
    throw new Error('结束时间不能早于起始时间')
  const body = {
    toSubjectId: relationTarget.value,
    label: relationLabel.value,
    directed: directed.value,
    startedAt: relationStart.value
      ? toIsoWithOffset(relationStart.value, defaultTimezone())
      : undefined,
    endedAt: relationEnd.value ? toIsoWithOffset(relationEnd.value, defaultTimezone()) : undefined,
  }
  if (editingRelation.value)
    await subjectsApi.updateRelation(
      editingRelation.value.id,
      {
        ...body,
        fromSubjectId: reverseRelation.value && directed.value ? relationTarget.value : id.value,
        toSubjectId: reverseRelation.value && directed.value ? id.value : relationTarget.value,
        resume: !relationEnd.value,
      },
      editingRelation.value.revision,
    )
  else if (reverseRelation.value && directed.value) {
    const target = graph.value?.nodes.find((s) => s.id === relationTarget.value)
    if (!target) throw new Error('请选择关联档案')
    await subjectsApi.relate(target.id, { ...body, toSubjectId: id.value }, target.version)
  } else await subjectsApi.relate(id.value, body, subject.value.version)
  cancelRelationEdit()
  await load()
}
async function preview(operation: string) {
  if (moreMenu.value) moreMenu.value.open = false
  const p = await subjectsApi.previewLifecycle(id.value)
  subject.value = p.subject
  pendingPlans.value = p.plans
  cancelPlanIds.value = []
  currentEndId.value = p.milestones
    .filter((x) => x.operation === 'end' && !x.voidedAt)
    .sort((a, b) => b.effectiveAt.localeCompare(a.effectiveAt))[0]?.id
  lifeOperation.value = operation
  lifeAt.value = toDatetimeLocal(new Date().toISOString())
  lifeReason.value = p.subject.endReason ?? 'other'
  lifeNote.value = ''
  undoEnd.value = false
  lifecycle.value = true
}
async function submitLife() {
  if (!subject.value) return
  const change: LifecycleInput = {
    operation: lifeOperation.value,
    effectiveAt: toIsoWithOffset(lifeAt.value, defaultTimezone())!,
    reason: lifeReason.value,
    note: lifeNote.value,
    cancelPlans:
      lifeOperation.value === 'end'
        ? pendingPlans.value
            .filter((x) => cancelPlanIds.value.includes(x.id))
            .map((x) => ({ id: x.id, version: x.version }))
        : [],
    undoEnd: undoEnd.value,
  }
  if (change.operation === 'correct') change.correctsId = currentEndId.value
  if (!change.effectiveAt) throw new Error('请选择实际日期')
  await subjectsApi.lifecycle(id.value, change, subject.value.version)
  lifecycle.value = false
  await load()
}
onMounted(() => void load())
watch(id, () => {
  subject.value = null
  timeline.value = null
  lifecycle.value = false
  cancelRelationEdit()
  error.value = ''
  void load()
})
watch([groupBy, kind, state, from, to], () => void load())
</script>
<template>
  <div class="app-shell">
    <WebAppHeader />
    <main class="workspace-main subject-page">
      <RouterLink
        v-if="route.query.conversation"
        :to="{ path: '/assistant', query: { conversation: route.query.conversation } }"
        >← 返回聊天</RouterLink
      ><RouterLink v-else to="/subjects">← 返回关系图</RouterLink>
      <p v-if="error" class="error-banner" role="alert">{{ error }}</p>
      <p v-if="!subject && !error" role="status">正在加载档案…</p>
      <template v-if="subject">
        <section id="subject-profile" class="subject-panel" style="margin-top: 1.5rem">
          <header class="subject-heading">
            <div class="subject-profile">
              <SubjectAvatar :subject="subject" :size="88" />
              <div>
                <h1>{{ subject.isSelf ? `${profile.nickname}（自己）` : subject.name }}</h1>
                <div class="subject-badges" style="margin-top: 0.7rem">
                  <span class="subject-badge">{{ subjectType(subject) }}</span
                  ><span class="subject-badge">{{
                    subject.state === 0
                      ? '进行中'
                      : `已结束 · ${reasons[subject.endReason ?? 'other']}`
                  }}</span>
                </div>
                <p v-if="subject.ageDays !== null" class="subject-muted">
                  年龄 {{ Math.floor(subject.ageDays / 365.2425) }} 岁（{{ subject.ageDays }} 天）
                </p>
              </div>
            </div>
            <div class="subject-actions">
              <RouterLink class="button button-secondary" :to="`/subjects/${id}/edit`"
                >编辑资料</RouterLink
              >
              <details v-if="!subject.isSelf" ref="moreMenu" class="subject-more">
                <summary class="button button-secondary" aria-label="更多档案操作">
                  更多操作
                </summary>
                <div class="subject-menu">
                  <button
                    v-if="subject.state === 1 && subject.endReason !== 'deceased'"
                    class="button"
                    :disabled="busy"
                    @click="run(() => preview('resume'))"
                  >
                    恢复档案</button
                  ><button
                    v-if="subject.state === 1"
                    class="button"
                    :disabled="busy"
                    @click="run(() => preview('correct'))"
                  >
                    纠正结束信息</button
                  ><button
                    v-if="subject.state === 0"
                    class="button subject-danger"
                    :disabled="busy"
                    @click="run(() => preview('end'))"
                  >
                    结束档案</button
                  ><button
                    class="button subject-danger"
                    :disabled="busy"
                    @click="run(() => remove('Subject', id))"
                  >
                    申请删除档案
                  </button>
                </div>
              </details>
            </div>
          </header>
          <p v-if="subject.description" class="subject-muted">{{ subject.description }}</p>
          <dl class="subject-values">
            <div v-for="f in subject.fields.filter((x) => !x.removed)" :key="f.id">
              <dt>{{ f.name }}</dt>
              <dd>
                {{ subjectValue(subject.values[f.id]) }}
                {{ subject.values[f.id] == null ? '' : f.unit }}
              </dd>
            </div>
          </dl>
        </section>
        <SubjectMedia v-for="m in subject.mediaIds" :id="m" :key="m" :title="subject.name" />
        <SubjectRelations
          v-if="graph"
          :graph="graph"
          :subject-id="id"
          :busy="busy"
          @add="newRelation"
          @edit="editRelation"
          @toggle="run(() => toggleRelation($event))"
          @remove="run(() => remove('SubjectRelation', $event.id))"
        />
        <SubjectDialog
          v-model="relationDialog"
          :title="editingRelation ? '编辑人物关系' : '新增人物关系'"
          :busy="busy"
        >
          <form
            :id="relationFormId"
            class="subject-form"
            data-testid="subject-relation-form"
            @submit.prevent="run(relate)"
          >
            <p v-if="error" class="error-banner" role="alert">{{ error }}</p>
            <label
              >关联档案<select v-model="relationTarget" required>
                <option value="">请选择</option>
                <option
                  v-for="s in graph?.nodes.filter((x) => x.id !== id)"
                  :key="s.id"
                  :value="s.id"
                >
                  {{ s.isSelf ? '自己' : s.name }}
                </option>
              </select></label
            >
            <label>关系名称<input v-model="relationLabel" required maxlength="100" /></label>
            <label
              >关系方向<select v-model="relationDirection">
                <option value="none">相互关联</option>
                <option value="outgoing">本档案指向关联档案</option>
                <option value="incoming">关联档案指向本档案</option>
              </select></label
            >
            <label>关系起始时间<input v-model="relationStart" type="datetime-local" /></label
            ><label
              >关系结束时间（可空）<input v-model="relationEnd" type="datetime-local"
            /></label>
            <p class="subject-muted">结束时间只标记历史关系，仍保留关系图连接。</p>
          </form>
          <template #footer
            ><button class="button button-secondary" :disabled="busy" @click="cancelRelationEdit">
              取消</button
            ><button
              class="button button-primary"
              :form="relationFormId"
              type="submit"
              :disabled="busy"
            >
              {{ busy ? '保存中…' : '保存关联' }}
            </button></template
          >
        </SubjectDialog>
        <SubjectDialog v-model="lifecycle" :title="lifeTitle" :busy="busy">
          <form
            :id="lifeFormId"
            class="subject-form"
            data-testid="subject-lifecycle-form"
            @submit.prevent="run(submitLife)"
          >
            <p v-if="error" class="error-banner" role="alert">{{ error }}</p>
            <p class="subject-muted">
              {{ subject.name }} ·
              {{
                lifeOperation === 'end'
                  ? '资料与历史保留，结束后仍可补录、回忆或安排后续事项。'
                  : lifeOperation === 'resume'
                    ? '恢复进行中状态，历史仍保留，已取消的计划不会自动恢复。'
                    : '纠正误标的时间或原因，原历史仍保留。'
              }}
            </p>
            <label>实际日期<input v-model="lifeAt" type="datetime-local" required /></label
            ><label v-if="lifeOperation !== 'resume'"
              >原因<select v-model="lifeReason">
                <option v-if="subject.kind !== 2" value="deceased">离世</option>
                <option value="relationship-ended">关系结束</option>
                <option value="sold">出售</option>
                <option value="gifted">转赠</option>
                <option value="lost">丢失</option>
                <option value="scrapped">报废</option>
                <option value="other">其他</option>
              </select></label
            >
            <label v-if="lifeOperation === 'correct'"
              ><input v-model="undoEnd" type="checkbox" />撤销误标的结束</label
            ><label>说明<textarea v-model="lifeNote" /></label>
            <section v-if="lifeOperation === 'end'" class="subject-panel">
              <h3>待执行计划</h3>
              <p class="subject-muted">默认保留。勾选后取消，其他引用档案也会看到取消结果。</p>
              <label v-for="p in pendingPlans" :key="p.id"
                ><input v-model="cancelPlanIds" type="checkbox" :value="p.id" />{{ p.title }}</label
              >
              <p v-if="!pendingPlans.length" class="subject-muted">没有所属待执行计划。</p>
            </section>
          </form>
          <template #footer
            ><button class="button button-secondary" :disabled="busy" @click="lifecycle = false">
              取消</button
            ><button
              class="button button-primary"
              :class="{ 'subject-danger': lifeOperation === 'end' }"
              :form="lifeFormId"
              type="submit"
              :disabled="busy"
            >
              {{ busy ? '提交中…' : lifeOperation === 'end' ? '确认结束' : '保存' }}
            </button></template
          >
        </SubjectDialog>
        <SubjectTimeline
          :subject-id="id"
          :timeline="timeline"
          :group-by="groupBy"
          :busy="busy"
          @more="run(more)"
        >
          <template #filters
            ><select v-model="groupBy" aria-label="时间分组">
              <option value="month">按月</option>
              <option value="day">按日</option></select
            ><select v-model="kind" aria-label="内容类型">
              <option value="">全部内容</option>
              <option value="Record">记录</option>
              <option value="Plan">计划</option></select
            ><select v-model="state" aria-label="计划状态">
              <option value="">所有状态</option>
              <option value="Planned">待执行</option>
              <option value="Completed">已完成</option>
              <option value="Cancelled">已取消</option></select
            ><label>开始日期<input v-model="from" type="date" aria-label="开始日期" /></label
            ><label>结束日期<input v-model="to" type="date" aria-label="结束日期" /></label
          ></template>
        </SubjectTimeline>
      </template>
    </main>
  </div>
</template>
