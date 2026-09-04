<script setup lang="ts">
import { computed, markRaw, nextTick, onMounted, onUnmounted, ref, shallowRef, watch } from 'vue'
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import { Background } from '@vue-flow/background'
import { Controls } from '@vue-flow/controls'
import { MiniMap } from '@vue-flow/minimap'
import {
  ConnectionMode,
  MarkerType,
  VueFlow,
  type Connection,
  type Edge,
  type Node,
} from '@vue-flow/core'
import dagre from '@dagrejs/dagre'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import '@vue-flow/controls/dist/style.css'
import '@vue-flow/minimap/dist/style.css'
import WebAppHeader from '@/components/WebAppHeader.vue'
import StorylineFlowNode from '@/components/StorylineFlowNode.vue'
import { eventsApi } from '@/api/events'
import {
  EventKind,
  EventKindLabel,
  EventStatus,
  EventStatusLabel,
  type EventKind as EventKindValue,
  type EventResponse,
  type EventStatus as EventStatusValue,
  type EventTaxonomyResponse,
} from '@/api/events-types'
import { mediaApi } from '@/api/media'
import { storylinesApi } from '@/api/storylines'
import {
  StorylineNodeEmphasis,
  StorylineRelationType,
  StorylineStatus,
  type SaveStorylineRequest,
  type StorylineNodeResponse,
  type StorylineRevisionResponse,
  type StorylineStageInput,
} from '@/api/storylines-types'

interface FlowData extends Record<string, unknown> {
  eventId?: number
  sourceRevision?: number
  title: string
  summary?: string
  occurredAt?: string | null
  place?: string | null
  tags?: string[]
  imageMediaAssetId?: string | null
  imageUrl?: string
  kind: number
  temporary?: boolean
  newPlan?: {
    title: string
    plannedAt?: string | null
    rawContent?: string | null
    timezone: string
  }
  stageKey?: string | null
  stageTitle?: string
  semanticOrder: number
  emphasis: number
  revisionState?: string
}
type StoryNode = Node<FlowData> & { data: FlowData }
const route = useRoute(),
  router = useRouter()
const storyId = computed(() => (typeof route.params.id === 'string' ? route.params.id : null))
const title = ref(''),
  description = ref(''),
  categoryKey = ref('trip'),
  status = ref<number>(StorylineStatus.Ongoing),
  tagsText = ref(''),
  coverMediaAssetId = ref<string | null>(null)
const version = ref(0),
  revision = ref(0),
  loading = ref(true),
  saving = ref(false),
  error = ref(''),
  notice = ref('')
const titleInput = ref<HTMLInputElement | null>(null),
  titleError = ref('')
const flowNodes = shallowRef<StoryNode[]>([]),
  flowEdges = shallowRef<Edge[]>([]),
  stages = ref<StorylineStageInput[]>([])
const selectedId = ref<string | null>(null),
  query = ref(''),
  recordBank = ref<EventResponse[]>([]),
  bankLoading = ref(false)
const bankFiltersOpen = ref(false),
  bankKind = ref<EventKindValue | ''>(''),
  bankStatus = ref<EventStatusValue | ''>(''),
  bankFrom = ref(''),
  bankTo = ref(''),
  bankCategory = ref(''),
  eventTaxonomy = ref<EventTaxonomyResponse | null>(null)
const planOpen = ref(false),
  planTitle = ref(''),
  planDate = ref(''),
  planContent = ref('')
const dirty = ref(false),
  history = ref<string[]>([]),
  future = ref<string[]>([])
const revisionHistoryOpen = ref(false),
  revisionHistoryLoading = ref(false),
  serverRevisions = ref<
    Array<{
      revision: number
      contentHash: string
      layoutState: number
      nodeCount: number
      createdAt: string
      isCurrent: boolean
    }>
  >([])
let queryTimer: number | undefined
const nodeTypes = { story: markRaw(StorylineFlowNode) as never }
const selected = computed(() => flowNodes.value.find((x) => x.id === selectedId.value) ?? null)
const coverOptions = computed(() =>
  flowNodes.value.filter(
    (node): node is StoryNode & { data: FlowData & { imageMediaAssetId: string } } =>
      Boolean(node.data.imageMediaAssetId),
  ),
)
const draftKey = computed(() => `weekday8:storyline-draft:${storyId.value ?? 'new'}`)
const categoryOptions = [
  ['trip', '行程旅行'],
  ['activity', '活动纪实'],
  ['project', '项目过程'],
  ['challenge', '目标挑战'],
  ['lifecycle', '成长陪伴'],
  ['series', '主题系列'],
  ['life-period', '生活阶段'],
  ['other', '其他'],
]

function uuid() {
  return crypto.randomUUID()
}
function cloneState() {
  return JSON.stringify({
    nodes: flowNodes.value.map((n) => ({ ...n, data: { ...n.data } })),
    edges: flowEdges.value.map((e) => ({ ...e })),
    stages: stages.value,
  })
}
function checkpoint() {
  history.value.push(cloneState())
  if (history.value.length > 30) history.value.shift()
  future.value = []
}
function applyState(raw: string) {
  const data = JSON.parse(raw)
  flowNodes.value = data.nodes
  flowEdges.value = data.edges
  stages.value = data.stages
  dirty.value = true
}
function undo() {
  const raw = history.value.pop()
  if (!raw) return
  future.value.push(cloneState())
  applyState(raw)
}
function redo() {
  const raw = future.value.pop()
  if (!raw) return
  history.value.push(cloneState())
  applyState(raw)
}
function markDirty() {
  dirty.value = true
  notice.value = ''
}
function validateTitle() {
  titleError.value = title.value.trim() ? '' : '请填写故事线名称。'
  return !titleError.value
}
function onTitleInput() {
  if (title.value.trim()) titleError.value = ''
}
function nextPosition() {
  const count = flowNodes.value.length
  return { x: 100 + (count % 3) * 320, y: 100 + Math.floor(count / 3) * 190 }
}
function stageTitle(key?: string | null) {
  return stages.value.find((x) => x.key === key)?.title
}
function eventDate(item: EventResponse) {
  return (item.kind === EventKind.Plan ? item.plannedAt : item.happenedAt) ?? item.createdAt
}
async function loadBank() {
  bankLoading.value = true
  try {
    recordBank.value = (
      await eventsApi.list({
        limit: 60,
        query: query.value || undefined,
        kind: bankKind.value === '' ? undefined : bankKind.value,
        status: bankStatus.value === '' ? undefined : bankStatus.value,
        from: bankFrom.value ? new Date(`${bankFrom.value}T00:00:00`).toISOString() : undefined,
        to: bankTo.value ? new Date(`${bankTo.value}T23:59:59.999`).toISOString() : undefined,
        categoryKey: bankCategory.value || undefined,
      })
    ).items.filter((e) => !flowNodes.value.some((n) => n.data.eventId === e.id))
  } finally {
    bankLoading.value = false
  }
}
function clearBankFilters() {
  bankKind.value = ''
  bankStatus.value = ''
  bankFrom.value = ''
  bankTo.value = ''
  bankCategory.value = ''
}
function addEvent(item: EventResponse) {
  checkpoint()
  const key = uuid()
  flowNodes.value = [
    ...flowNodes.value,
    {
      id: key,
      type: 'story',
      position: nextPosition(),
      data: {
        eventId: item.id,
        sourceRevision: item.sourceRevision,
        title: item.title || '无标题记录',
        summary: item.rawContent || '',
        occurredAt: eventDate(item),
        place: item.locations[0]?.name,
        tags: item.effectiveClassification.tags.map((x) => x.displayName),
        imageMediaAssetId: item.media.find((x) => x.kind === 1)?.id,
        kind: item.kind,
        stageKey: stages.value[0]?.key ?? null,
        stageTitle: stages.value[0]?.title,
        semanticOrder: flowNodes.value.length,
        emphasis: StorylineNodeEmphasis.Normal,
      },
    },
  ]
  markDirty()
  void loadImages()
}
function addPlan() {
  const value = planTitle.value.trim()
  if (!value) return
  checkpoint()
  const key = uuid()
  flowNodes.value = [
    ...flowNodes.value,
    {
      id: key,
      type: 'story',
      position: nextPosition(),
      data: {
        title: value,
        summary: planContent.value,
        occurredAt: planDate.value ? new Date(planDate.value).toISOString() : null,
        kind: EventKind.Plan,
        temporary: true,
        newPlan: {
          title: value,
          plannedAt: planDate.value ? new Date(planDate.value).toISOString() : null,
          rawContent: planContent.value || null,
          timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC',
        },
        stageKey: stages.value[0]?.key ?? null,
        stageTitle: stages.value[0]?.title,
        semanticOrder: flowNodes.value.length,
        emphasis: StorylineNodeEmphasis.Normal,
      },
    },
  ]
  planTitle.value = ''
  planDate.value = ''
  planContent.value = ''
  planOpen.value = false
  markDirty()
}
function removeSelected() {
  if (!selected.value) return
  checkpoint()
  const id = selected.value.id
  if (coverMediaAssetId.value === selected.value.data.imageMediaAssetId) {
    coverMediaAssetId.value = null
  }
  flowNodes.value = flowNodes.value.filter((x) => x.id !== id)
  flowEdges.value = flowEdges.value.filter((x) => x.source !== id && x.target !== id)
  selectedId.value = null
  markDirty()
}
function addStage() {
  checkpoint()
  const key = uuid()
  stages.value = [
    ...stages.value,
    { key, title: `阶段 ${stages.value.length + 1}`, semanticOrder: stages.value.length },
  ]
  markDirty()
}
function onConnect(connection: Connection) {
  if (!connection.source || !connection.target || connection.source === connection.target) return
  checkpoint()
  flowEdges.value = [
    ...flowEdges.value,
    {
      id: uuid(),
      source: connection.source,
      target: connection.target,
      type: 'smoothstep',
      markerEnd: MarkerType.ArrowClosed,
      data: { relationType: StorylineRelationType.Sequence },
    },
  ]
  markDirty()
}
function moveSelected(dx: number, dy: number) {
  if (!selected.value) return
  checkpoint()
  flowNodes.value = flowNodes.value.map((n) =>
    n.id === selected.value!.id
      ? { ...n, position: { x: n.position.x + dx, y: n.position.y + dy } }
      : n,
  )
  markDirty()
}
function autoLayout() {
  if (!flowNodes.value.length) return
  checkpoint()
  const graph = new dagre.graphlib.Graph().setDefaultEdgeLabel(() => ({}))
  graph.setGraph({ rankdir: 'LR', ranksep: 90, nodesep: 42 })
  for (const node of flowNodes.value) graph.setNode(node.id, { width: 260, height: 150 })
  for (const edge of flowEdges.value) graph.setEdge(edge.source, edge.target)
  dagre.layout(graph)
  flowNodes.value = flowNodes.value.map((n) => {
    const p = graph.node(n.id)
    return { ...n, position: { x: p.x - 130, y: p.y - 75 } }
  })
  markDirty()
}
function updateSelectedStage(value: string) {
  if (!selected.value) return
  const key = value || null
  flowNodes.value = flowNodes.value.map((n) =>
    n.id === selected.value!.id
      ? { ...n, data: { ...n.data, stageKey: key, stageTitle: stageTitle(key) } }
      : n,
  )
  markDirty()
}
function updateSelectedEmphasis(value: boolean) {
  if (!selected.value) return
  flowNodes.value = flowNodes.value.map((n) =>
    n.id === selected.value!.id ? { ...n, data: { ...n.data, emphasis: value ? 2 : 1 } } : n,
  )
  markDirty()
}
async function loadImages() {
  for (const node of flowNodes.value) {
    if (!node.data.imageMediaAssetId || node.data.imageUrl) continue
    try {
      const access = await mediaApi.access(node.data.imageMediaAssetId)
      flowNodes.value = flowNodes.value.map((n) =>
        n.id === node.id ? { ...n, data: { ...n.data, imageUrl: access.url } } : n,
      )
    } catch {
      /* 图片失败不阻塞图编辑 */
    }
  }
}
function fromResponse(value: StorylineRevisionResponse) {
  title.value = value.title
  titleError.value = ''
  description.value = value.description || ''
  categoryKey.value = value.categoryKey
  status.value = value.status
  tagsText.value = value.tags.join('，')
  coverMediaAssetId.value = value.coverMediaAssetId
  version.value = value.version
  revision.value = value.revision
  stages.value = value.stages
  const layout = new Map(value.webCanvasLayout?.nodes.map((x) => [x.nodeKey, x]) ?? [])
  flowNodes.value = value.nodes.map((node, index) => toFlowNode(node, layout.get(node.key), index))
  flowEdges.value = value.edges.map((edge) => ({
    id: edge.key,
    source: edge.sourceNodeKey,
    target: edge.targetNodeKey,
    type: 'smoothstep',
    markerEnd: MarkerType.ArrowClosed,
    label: edge.label || undefined,
    data: { relationType: edge.relationType },
  }))
  dirty.value = false
  history.value = []
  future.value = []
  void loadImages()
}
function toFlowNode(
  node: StorylineNodeResponse,
  layout: { x: number; y: number } | undefined,
  index: number,
): StoryNode {
  return {
    id: node.key,
    type: 'story',
    position: layout ?? { x: 100 + (index % 3) * 320, y: 100 + Math.floor(index / 3) * 190 },
    data: {
      eventId: node.eventId,
      sourceRevision: node.sourceRevision,
      title: node.title,
      summary: node.rawContent || '',
      occurredAt: node.occurredAt,
      place: node.place,
      tags: node.tags,
      imageMediaAssetId: node.imageMediaAssetId,
      kind: node.kind,
      stageKey: node.stageKey,
      stageTitle: stageTitle(node.stageKey),
      semanticOrder: node.semanticOrder,
      emphasis: node.emphasis,
      revisionState: node.revisionState,
    },
  }
}
function requestBody(): SaveStorylineRequest {
  const stageLayouts = stages.value.map((stage, stageIndex) => {
    const members = flowNodes.value.filter((node) => node.data.stageKey === stage.key)
    if (!members.length) {
      return { stageKey: stage.key, x: 40, y: 40 + stageIndex * 190, width: 320, height: 170 }
    }
    const left = Math.min(...members.map((node) => node.position.x))
    const top = Math.min(...members.map((node) => node.position.y))
    const right = Math.max(...members.map((node) => node.position.x + 260))
    const bottom = Math.max(...members.map((node) => node.position.y + 150))
    return {
      stageKey: stage.key,
      x: left - 34,
      y: top - 62,
      width: right - left + 68,
      height: bottom - top + 96,
    }
  })
  return {
    title: title.value.trim(),
    description: description.value.trim() || null,
    categoryKey: categoryKey.value,
    status: status.value as 1 | 2,
    coverMediaAssetId: coverMediaAssetId.value,
    tags: tagsText.value
      .split(/[，,]/)
      .map((x) => x.trim())
      .filter(Boolean)
      .slice(0, 10),
    stages: stages.value,
    nodes: flowNodes.value.map((n, index) => ({
      key: n.id,
      nodeType: n.data.temporary ? 'new-plan' : 'existing-event',
      eventId: n.data.eventId ?? null,
      sourceRevision: n.data.sourceRevision ?? null,
      newPlan: n.data.temporary ? n.data.newPlan : null,
      stageKey: n.data.stageKey ?? null,
      semanticOrder: n.data.semanticOrder ?? index,
      emphasis: n.data.emphasis ?? 1,
    })),
    edges: flowEdges.value.map((e) => ({
      key: e.id,
      sourceNodeKey: e.source,
      targetNodeKey: e.target,
      relationType: ((e.data?.relationType as number) ?? 1) as 1 | 2 | 3 | 4,
      label: typeof e.label === 'string' ? e.label : null,
    })),
    webCanvasLayout: {
      direction: 'LR',
      viewportX: 0,
      viewportY: 0,
      zoom: 1,
      nodes: flowNodes.value.map((n) => ({
        nodeKey: n.id,
        x: n.position.x,
        y: n.position.y,
        width: 260,
        height: 150,
      })),
      stages: stageLayouts,
    },
  }
}
async function save() {
  if (!validateTitle()) {
    error.value = ''
    await nextTick()
    titleInput.value?.scrollIntoView({ behavior: 'smooth', block: 'center' })
    titleInput.value?.focus()
    return
  }
  saving.value = true
  error.value = ''
  try {
    const key = uuid()
    const response = storyId.value
      ? await storylinesApi.save(storyId.value, requestBody(), version.value, key)
      : await storylinesApi.create(requestBody(), key)
    fromResponse(response.storyline)
    sessionStorage.removeItem(draftKey.value)
    notice.value = `已保存修订 ${response.storyline.revision}`
    if (!storyId.value) await router.replace(`/storylines/${response.storyline.id}/edit`)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '保存故事线失败。'
  } finally {
    saving.value = false
  }
}
async function load() {
  loading.value = true
  try {
    if (storyId.value) {
      fromResponse(await storylinesApi.get(storyId.value))
    } else {
      title.value = ''
      titleError.value = ''
      stages.value = [{ key: uuid(), title: '开始', semanticOrder: 0 }]
      const draft = sessionStorage.getItem(draftKey.value)
      if (draft) {
        const value = JSON.parse(draft)
        title.value = value.title || ''
        description.value = value.description || ''
        categoryKey.value = value.categoryKey || 'trip'
        tagsText.value = value.tagsText || ''
        coverMediaAssetId.value = value.coverMediaAssetId || null
        applyState(value.graph)
        notice.value = '已恢复此标签页未保存的草稿。'
      }
      dirty.value = false
    }
    if (!eventTaxonomy.value) eventTaxonomy.value = await eventsApi.taxonomy()
    await loadBank()
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '加载编辑器失败。'
  } finally {
    loading.value = false
    await nextTick()
    if (flowNodes.value.length && !storyId.value) autoLayout()
    dirty.value = false
  }
}
async function openRevisionHistory() {
  if (!storyId.value) return
  revisionHistoryOpen.value = true
  revisionHistoryLoading.value = true
  try {
    serverRevisions.value = await storylinesApi.revisions(storyId.value)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '加载版本历史失败。'
  } finally {
    revisionHistoryLoading.value = false
  }
}
async function restoreRevision(targetRevision: number) {
  if (!storyId.value || saving.value) return
  if (dirty.value && !window.confirm('恢复历史版本会丢弃当前未保存修改，确定继续吗？')) return
  saving.value = true
  try {
    const response = await storylinesApi.restore(
      storyId.value,
      targetRevision,
      version.value,
      uuid(),
    )
    fromResponse(response.storyline)
    revisionHistoryOpen.value = false
    notice.value = `已恢复为修订 ${targetRevision}，并生成新的当前修订。`
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '恢复历史版本失败。'
  } finally {
    saving.value = false
  }
}
watch([query, bankKind, bankStatus, bankFrom, bankTo, bankCategory], () => {
  window.clearTimeout(queryTimer)
  queryTimer = window.setTimeout(() => void loadBank(), 260)
})
watch([title, description, categoryKey, status, tagsText, coverMediaAssetId], () => {
  if (!loading.value) markDirty()
})
watch(dirty, (value) => {
  if (value)
    sessionStorage.setItem(
      draftKey.value,
      JSON.stringify({
        title: title.value,
        description: description.value,
        categoryKey: categoryKey.value,
        tagsText: tagsText.value,
        coverMediaAssetId: coverMediaAssetId.value,
        graph: cloneState(),
      }),
    )
})
onBeforeRouteLeave(() => !dirty.value || window.confirm('故事线还有未保存的修改，确定离开吗？'))
function beforeUnload(event: BeforeUnloadEvent) {
  if (dirty.value) {
    event.preventDefault()
    event.returnValue = ''
  }
}
onMounted(() => {
  window.addEventListener('beforeunload', beforeUnload)
  void load()
})
onUnmounted(() => window.removeEventListener('beforeunload', beforeUnload))
</script>

<template>
  <div class="app-shell story-editor-shell">
    <WebAppHeader />
    <main class="story-editor">
      <header class="editor-top">
        <div class="editor-title-summary">
          <p class="eyebrow">STORYLINE EDITOR</p>
          <h1>{{ title.trim() || (storyId ? '未命名故事线' : '新故事线') }}</h1>
          <p>在右侧整理故事线信息与节点属性</p>
        </div>
        <div class="editor-actions">
          <button class="text-button" :disabled="!history.length" @click="undo">撤销</button
          ><button class="text-button" :disabled="!future.length" @click="redo">重做</button
          ><button class="button button-secondary button-compact" @click="autoLayout">
            自动排版</button
          ><button
            v-if="storyId"
            class="button button-secondary button-compact"
            @click="openRevisionHistory"
          >
            版本历史</button
          ><button class="button button-primary button-compact" :disabled="saving" @click="save">
            {{ saving ? '保存中…' : '保存故事线' }}
          </button>
        </div>
      </header>
      <p v-if="error" class="editor-message editor-message--error">{{ error }}</p>
      <p v-if="notice" class="editor-message">{{ notice }}</p>
      <section v-if="!loading" class="editor-workspace">
        <aside class="record-bank">
          <div class="panel-heading">
            <div><span>记录库</span><small>拖入或点击添加</small></div>
            <button title="创建轻量计划" @click="planOpen = !planOpen">＋ 计划</button>
          </div>
          <div class="bank-tools">
            <label class="bank-search"
              ><span aria-hidden="true">⌕</span><input v-model="query" placeholder="搜索标题或正文"
            /></label>
            <button
              class="bank-filter-toggle"
              type="button"
              :aria-expanded="bankFiltersOpen"
              aria-controls="record-bank-filters"
              aria-label="筛选记录库"
              @click="bankFiltersOpen = !bankFiltersOpen"
            >
              <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
                <path d="M4 6h16M7 12h10M10 18h4" />
              </svg>
            </button>
          </div>
          <section v-if="bankFiltersOpen" id="record-bank-filters" class="bank-filters">
            <div class="bank-filter-grid">
              <label
                ><span>类型</span
                ><select v-model="bankKind">
                  <option value="">全部</option>
                  <option :value="EventKind.Trace">{{ EventKindLabel[EventKind.Trace] }}</option>
                  <option :value="EventKind.Plan">{{ EventKindLabel[EventKind.Plan] }}</option>
                </select></label
              >
              <label
                ><span>状态</span
                ><select v-model="bankStatus">
                  <option value="">全部</option>
                  <option :value="EventStatus.Planned">
                    {{ EventStatusLabel[EventStatus.Planned] }}
                  </option>
                  <option :value="EventStatus.Completed">
                    {{ EventStatusLabel[EventStatus.Completed] }}
                  </option>
                  <option :value="EventStatus.Cancelled">
                    {{ EventStatusLabel[EventStatus.Cancelled] }}
                  </option>
                </select></label
              >
            </div>
            <label class="bank-filter-wide"
              ><span>发生时间</span>
              <span class="bank-date-range"
                ><span
                  class="bank-date-input"
                  :class="{ 'has-value': bankFrom }"
                  data-placeholder="开始"
                  ><input v-model="bankFrom" type="date" aria-label="记录开始日期" /></span
                ><i>至</i
                ><span
                  class="bank-date-input"
                  :class="{ 'has-value': bankTo }"
                  data-placeholder="结束"
                  ><input v-model="bankTo" type="date" aria-label="记录结束日期" /></span></span
            ></label>
            <label class="bank-filter-wide"
              ><span>主分类</span
              ><select v-model="bankCategory">
                <option value="">全部</option>
                <option
                  v-for="item in eventTaxonomy?.categories ?? []"
                  :key="item.key"
                  :value="item.key"
                >
                  {{ item.label }}
                </option>
              </select></label
            >
            <button class="bank-filter-clear" type="button" @click="clearBankFilters">
              清除筛选
            </button>
          </section>
          <form v-if="planOpen" class="inline-plan" @submit.prevent="addPlan">
            <strong>直接添加计划</strong
            ><input v-model="planTitle" placeholder="计划标题" required /><input
              v-model="planDate"
              type="datetime-local"
            /><textarea v-model="planContent" rows="2" placeholder="简短说明（可选）"></textarea>
            <div>
              <button type="button" @click="planOpen = false">取消</button
              ><button type="submit">加入画布</button>
            </div>
          </form>
          <div class="bank-list">
            <p v-if="bankLoading" class="bank-hint">正在搜索…</p>
            <button
              v-for="item in recordBank"
              :key="item.id"
              class="bank-card"
              @click="addEvent(item)"
            >
              <span class="bank-card__kind">{{
                item.kind === EventKind.Plan ? '计划' : '记录'
              }}</span
              ><strong>{{ item.title || '无标题记录' }}</strong
              ><small>{{ item.rawContent || '没有正文' }}</small
              ><i>添加</i>
            </button>
            <p v-if="!bankLoading && !recordBank.length" class="bank-hint">没有更多可添加记录</p>
          </div>
        </aside>
        <section class="flow-canvas" aria-label="故事线流程画布">
          <VueFlow
            v-model:nodes="flowNodes"
            v-model:edges="flowEdges"
            :node-types="nodeTypes"
            :connection-mode="ConnectionMode.Loose"
            fit-view-on-init
            :min-zoom="0.2"
            :max-zoom="2"
            @connect="onConnect"
            @node-click="({ node }) => (selectedId = node.id)"
            @node-drag-start="checkpoint"
            @node-drag-stop="markDirty"
            ><Background :gap="24" color="var(--line)" /><MiniMap
              pannable
              zoomable
              node-color="var(--primary)"
              mask-color="color-mix(in srgb, var(--canvas) 76%, transparent)" /><Controls
          /></VueFlow>
          <div v-if="!flowNodes.length" class="canvas-empty">
            <strong>从左侧添加第一条记录</strong><span>也可以先创建一个轻量计划</span>
          </div>
        </section>
        <aside class="property-panel">
          <div class="panel-heading">
            <div><span>整理</span><small>故事线与节点属性</small></div>
          </div>
          <div class="property-scroll">
            <section class="story-properties">
              <div class="story-title-field" :class="{ 'has-error': titleError }">
                <label for="storyline-title">故事线名称 <span>必填</span></label>
                <input
                  id="storyline-title"
                  ref="titleInput"
                  v-model="title"
                  type="text"
                  maxlength="120"
                  placeholder="输入故事线名称"
                  required
                  :aria-invalid="Boolean(titleError)"
                  :aria-describedby="titleError ? 'storyline-title-error' : 'storyline-title-hint'"
                  @input="onTitleInput"
                  @blur="validateTitle"
                />
                <p v-if="titleError" id="storyline-title-error" class="field-error" role="alert">
                  {{ titleError }}
                </p>
                <p v-else id="storyline-title-hint" class="field-hint">
                  填写一个便于以后查找和辨认的名称
                </p>
              </div>
              <label
                >主分类<select v-model="categoryKey">
                  <option v-for="option in categoryOptions" :key="option[0]" :value="option[0]">
                    {{ option[1] }}
                  </option>
                </select></label
              ><label
                >状态<select v-model="status">
                  <option :value="StorylineStatus.Ongoing">进行中</option>
                  <option :value="StorylineStatus.Completed">已完成</option>
                </select></label
              ><label
                >说明<textarea
                  v-model="description"
                  rows="3"
                  placeholder="这段故事大概讲什么"
                ></textarea></label
              ><label>标签<input v-model="tagsText" placeholder="旅行，朋友，登山" /></label
              ><label v-if="coverOptions.length"
                >封面<select v-model="coverMediaAssetId">
                  <option :value="null">自动选择最早图片</option>
                  <option
                    v-for="node in coverOptions"
                    :key="node.data.imageMediaAssetId"
                    :value="node.data.imageMediaAssetId"
                  >
                    {{ node.data.title }}
                  </option>
                </select></label
              >
            </section>
            <section class="stage-editor">
              <div class="section-title">
                <strong>阶段</strong><button @click="addStage">添加阶段</button>
              </div>
              <div v-for="stage in stages" :key="stage.key" class="stage-row">
                <span>{{ stage.semanticOrder + 1 }}</span
                ><input v-model="stage.title" @input="markDirty" />
              </div>
            </section>
            <section v-if="selected" class="selected-editor">
              <div class="section-title">
                <strong>当前节点</strong
                ><button class="danger-link" @click="removeSelected">移除</button>
              </div>
              <h3>{{ selected.data.title }}</h3>
              <label
                >所属阶段<select
                  :value="selected.data.stageKey || ''"
                  @change="updateSelectedStage(($event.target as HTMLSelectElement).value)"
                >
                  <option value="">未分组</option>
                  <option v-for="stage in stages" :key="stage.key" :value="stage.key">
                    {{ stage.title }}
                  </option>
                </select></label
              ><label class="check-row"
                ><input
                  type="checkbox"
                  :checked="selected.data.emphasis === 2"
                  @change="updateSelectedEmphasis(($event.target as HTMLInputElement).checked)"
                />重要节点</label
              >
              <div class="position-pad" aria-label="节点位置微调">
                <button @click="moveSelected(0, -16)">上移</button
                ><button @click="moveSelected(-16, 0)">左移</button
                ><button @click="moveSelected(16, 0)">右移</button
                ><button @click="moveSelected(0, 16)">下移</button>
              </div>
              <RouterLink
                v-if="selected.data.eventId"
                class="inline-link"
                :to="`/events/${selected.data.eventId}`"
                >打开原记录</RouterLink
              >
            </section>
            <section v-else class="property-placeholder">
              <strong>选择一个节点</strong>
              <p>可设置阶段、重要性或用按钮微调位置。</p>
            </section>
          </div>
        </aside>
      </section>
    </main>
    <div
      v-if="revisionHistoryOpen"
      class="revision-overlay"
      role="dialog"
      aria-modal="true"
      aria-labelledby="revision-title"
      @click.self="revisionHistoryOpen = false"
    >
      <section class="revision-dialog">
        <header>
          <div>
            <p class="eyebrow">VERSIONS</p>
            <h2 id="revision-title">版本历史</h2>
          </div>
          <button
            class="text-button"
            aria-label="关闭版本历史"
            @click="revisionHistoryOpen = false"
          >
            关闭
          </button>
        </header>
        <p v-if="revisionHistoryLoading" class="revision-empty">正在读取历史…</p>
        <ol v-else class="revision-list">
          <li v-for="entry in serverRevisions" :key="entry.revision">
            <div>
              <strong>修订 {{ entry.revision }}</strong>
              <span>{{ new Date(entry.createdAt).toLocaleString('zh-CN') }}</span>
              <small
                >{{ entry.nodeCount }} 个节点 ·
                {{ entry.layoutState === 2 ? '待排版' : '已排版' }}</small
              >
            </div>
            <div class="revision-actions">
              <button
                class="text-button"
                @click="router.push(`/storylines/${storyId}/revisions/${entry.revision}`)"
              >
                查看
              </button>
              <button
                v-if="!entry.isCurrent"
                class="text-button"
                :disabled="saving"
                @click="restoreRevision(entry.revision)"
              >
                恢复
              </button>
              <span v-else>当前</span>
            </div>
          </li>
        </ol>
      </section>
    </div>
  </div>
</template>
<style scoped>
.story-editor-shell {
  height: 100dvh;
  overflow: hidden;
}
.story-editor {
  height: calc(100dvh - 72px);
  display: flex;
  flex-direction: column;
}
.editor-top {
  min-height: 82px;
  padding: 10px 22px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  border-bottom: 1px solid var(--line);
  background: var(--surface);
}
.editor-top .eyebrow {
  margin: 0;
}
.editor-title-summary h1 {
  margin: 2px 0 0;
  font-size: 24px;
  font-weight: 800;
  letter-spacing: -0.03em;
}
.editor-title-summary > p:last-child {
  margin: 2px 0 0;
  color: var(--ink-tertiary);
  font-size: 10px;
}
.editor-actions {
  display: flex;
  align-items: center;
  gap: 7px;
}
.editor-message {
  margin: 0;
  padding: 8px 22px;
  color: var(--primary-strong);
  background: var(--primary-soft);
  font-size: 12px;
}
.editor-message--error {
  color: var(--danger);
  background: color-mix(in srgb, var(--danger) 9%, var(--surface));
}
.editor-workspace {
  min-height: 0;
  flex: 1;
  display: grid;
  grid-template-columns: 280px minmax(420px, 1fr) 300px;
}
.record-bank,
.property-panel {
  min-height: 0;
  display: flex;
  flex-direction: column;
  background: var(--surface);
}
.record-bank {
  border-right: 1px solid var(--line);
}
.property-panel {
  border-left: 1px solid var(--line);
}
.panel-heading {
  min-height: 64px;
  padding: 12px 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--line);
}
.panel-heading span,
.panel-heading small {
  display: block;
}
.panel-heading span {
  font-weight: 800;
}
.panel-heading small {
  color: var(--ink-tertiary);
  font-size: 9px;
}
.panel-heading button,
.section-title button,
.inline-plan button {
  min-height: 36px;
  padding: 0 10px;
  border: 1px solid var(--line);
  border-radius: 9px;
  background: var(--surface-soft);
  color: var(--primary-strong);
  font-size: 11px;
}
.bank-search {
  height: 44px;
  min-width: 0;
  flex: 1;
  margin: 0;
  padding: 0 11px;
  display: flex;
  align-items: center;
  gap: 8px;
  border: 1px solid var(--line);
  border-radius: 11px;
  background: var(--surface-soft);
}
.bank-tools {
  margin: 12px 12px 4px;
  display: flex;
  align-items: center;
  gap: 8px;
}
.bank-filter-toggle {
  width: 44px;
  height: 44px;
  flex: 0 0 auto;
  display: grid;
  place-items: center;
  border: 1px solid var(--line);
  border-radius: 11px;
  color: var(--primary-strong);
  background: var(--surface-soft);
}
.bank-filter-toggle[aria-expanded='true'] {
  border-color: var(--primary);
  color: var(--on-primary);
  background: var(--primary);
}
.bank-filter-toggle .ui-icon {
  width: 18px;
  height: 18px;
}
.bank-filters {
  margin: 4px 12px 8px;
  padding: 10px;
  display: grid;
  gap: 9px;
  border: 1px solid var(--line);
  border-radius: 11px;
  background: var(--surface-soft);
}
.bank-filter-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 7px;
}
.bank-filters label {
  min-width: 0;
  display: grid;
  gap: 4px;
  color: var(--ink-tertiary);
  font-size: 9px;
}
.bank-filters select,
.bank-filters input {
  width: 100%;
  min-width: 0;
  min-height: 36px;
  padding: 0 8px;
  border: 1px solid var(--line);
  border-radius: 8px;
  color: var(--ink);
  background: var(--surface);
  font-size: 10px;
}
.bank-date-range {
  min-width: 0;
  display: grid;
  grid-template-columns: minmax(0, 1fr) 16px minmax(0, 1fr);
  align-items: center;
}
.bank-date-range i {
  color: var(--ink-tertiary);
  font-size: 9px;
  font-style: normal;
  text-align: center;
}
.bank-date-input {
  min-width: 0;
  position: relative;
}
.bank-date-input::before {
  content: attr(data-placeholder);
  position: absolute;
  z-index: 1;
  top: 50%;
  left: 8px;
  transform: translateY(-50%);
  pointer-events: none;
  color: var(--ink-tertiary);
  font-size: 10px;
}
.bank-date-input.has-value::before,
.bank-date-input:focus-within::before {
  content: none;
}
.bank-date-input:not(.has-value):not(:focus-within) input::-webkit-datetime-edit {
  color: transparent;
}
.bank-filter-clear {
  min-height: 34px;
  border: 0;
  border-radius: 8px;
  color: var(--primary-strong);
  background: var(--primary-soft);
  font-size: 10px;
}
.bank-search input {
  width: 100%;
  border: 0;
  outline: 0;
  background: transparent;
  font-size: 12px;
}
.bank-list,
.property-scroll {
  min-height: 0;
  overflow: auto;
  padding: 10px 12px 24px;
}
.bank-card {
  width: 100%;
  min-height: 100px;
  margin-bottom: 8px;
  padding: 12px;
  display: grid;
  grid-template-columns: 1fr auto;
  text-align: left;
  border: 1px solid var(--line);
  border-radius: 12px;
  background: var(--surface);
}
.bank-card:hover {
  border-color: var(--primary);
  background: var(--surface-tint);
}
.bank-card__kind {
  grid-column: 1/-1;
  color: var(--accent);
  font-size: 9px;
}
.bank-card strong {
  margin: 5px 0;
  font-size: 12px;
}
.bank-card small {
  grid-column: 1/2;
  overflow: hidden;
  color: var(--ink-tertiary);
  font-size: 9px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.bank-card i {
  grid-column: 2;
  grid-row: 2/4;
  align-self: center;
  color: var(--primary);
  font-size: 10px;
  font-style: normal;
}
.bank-hint {
  text-align: center;
  color: var(--ink-tertiary);
  font-size: 10px;
}
.inline-plan {
  margin: 8px 12px;
  padding: 12px;
  display: grid;
  gap: 8px;
  border: 1px solid var(--accent);
  border-radius: 12px;
  background: var(--accent-soft);
}
.inline-plan strong {
  font-size: 12px;
}
.inline-plan input,
.inline-plan textarea {
  width: 100%;
  padding: 8px;
  border: 1px solid color-mix(in srgb, var(--accent) 30%, var(--line));
  border-radius: 8px;
  background: var(--surface);
  font-size: 11px;
}
.inline-plan div {
  display: flex;
  justify-content: end;
  gap: 5px;
}
.flow-canvas {
  min-width: 0;
  min-height: 0;
  position: relative;
  background: var(--canvas);
}
.flow-canvas :deep(.vue-flow__node) {
  border: 0;
  background: transparent;
}
.flow-canvas :deep(.vue-flow__controls) {
  overflow: hidden;
  border: 1px solid var(--line-strong);
  border-radius: 10px;
  box-shadow: var(--shadow-1);
}
.flow-canvas :deep(.vue-flow__controls-button) {
  width: 34px;
  height: 34px;
  border: 0;
  border-bottom: 1px solid var(--line);
  color: var(--primary-strong);
  background: var(--surface);
}
.flow-canvas :deep(.vue-flow__controls-button:hover) {
  color: var(--on-primary);
  background: var(--primary);
}
.flow-canvas :deep(.vue-flow__controls-button svg) {
  fill: currentColor;
}
.flow-canvas :deep(.vue-flow__minimap) {
  overflow: hidden;
  border: 1px solid var(--line-strong);
  border-radius: 10px;
  background: var(--surface) !important;
  box-shadow: var(--shadow-1);
}
.canvas-empty {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  align-content: center;
  pointer-events: none;
  color: var(--ink-tertiary);
}
.canvas-empty strong,
.canvas-empty span {
  display: block;
}
.canvas-empty strong {
  color: var(--ink-secondary);
}
.property-scroll > section {
  padding: 14px 4px 20px;
  border-bottom: 1px solid var(--line);
}
.property-scroll label {
  margin-bottom: 12px;
  display: grid;
  gap: 5px;
  color: var(--ink-tertiary);
  font-size: 10px;
}
.property-scroll select,
.property-scroll textarea,
.property-scroll input:not([type='checkbox']) {
  width: 100%;
  min-height: 40px;
  padding: 8px 10px;
  border: 1px solid var(--line);
  border-radius: 9px;
  background: var(--surface-soft);
  font-size: 12px;
}
.story-title-field {
  margin-bottom: 16px;
}
.story-title-field > label {
  margin-bottom: 6px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  color: var(--ink-secondary);
  font-size: 11px;
  font-weight: 700;
}
.story-title-field > label span {
  padding: 2px 6px;
  border-radius: 5px;
  background: var(--accent-soft);
  color: var(--accent);
  font-size: 9px;
  font-weight: 700;
}
.property-scroll .story-title-field input {
  min-height: 48px;
  border-color: color-mix(in srgb, var(--primary) 45%, var(--line));
  background: var(--surface);
  box-shadow: inset 3px 0 0 var(--primary);
  color: var(--ink);
  font-size: 14px;
  font-weight: 700;
}
.property-scroll input:focus-visible,
.property-scroll select:focus-visible,
.property-scroll textarea:focus-visible {
  border-color: var(--primary);
  outline: 2px solid color-mix(in srgb, var(--primary) 36%, transparent);
  outline-offset: 2px;
}
.property-scroll .story-title-field.has-error input {
  border-color: var(--danger);
  box-shadow: inset 3px 0 0 var(--danger);
}
.field-hint,
.field-error {
  margin: 6px 0 0;
  font-size: 9px;
  line-height: 1.5;
}
.field-hint {
  color: var(--ink-tertiary);
}
.field-error {
  color: var(--danger);
}
.section-title {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
}
.stage-row {
  margin-bottom: 6px;
  display: grid;
  grid-template-columns: 28px 1fr;
  align-items: center;
}
.stage-row span {
  color: var(--accent);
  font-size: 10px;
}
.stage-row input {
  min-height: 36px !important;
}
.selected-editor h3 {
  font-size: 15px;
}
.danger-link {
  color: var(--danger) !important;
}
.check-row {
  min-height: 44px !important;
  display: flex !important;
  grid-template-columns: 20px 1fr !important;
  align-items: center;
}
.position-pad {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 6px;
}
.position-pad button {
  min-height: 40px;
  border: 1px solid var(--line);
  border-radius: 9px;
  background: var(--surface-soft);
  font-size: 10px;
}
.property-placeholder {
  color: var(--ink-tertiary);
  font-size: 11px;
}
.property-placeholder p {
  line-height: 1.6;
}
.revision-overlay {
  position: fixed;
  z-index: 100;
  inset: 0;
  display: grid;
  place-items: center;
  padding: 24px;
  background: rgb(12 20 16 / 55%);
  backdrop-filter: blur(5px);
}
.revision-dialog {
  width: min(620px, 100%);
  max-height: min(720px, calc(100dvh - 48px));
  padding: 24px;
  overflow: auto;
  border: 1px solid var(--line-strong);
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-2);
}
.revision-dialog > header {
  display: flex;
  align-items: start;
  justify-content: space-between;
  gap: 20px;
  margin-bottom: 18px;
}
.revision-dialog h2 {
  margin: 2px 0 0;
}
.revision-list {
  margin: 0;
  padding: 0;
  list-style: none;
}
.revision-list li {
  min-height: 72px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18px;
  padding: 12px 0;
  border-top: 1px solid var(--line);
}
.revision-list li > div:first-child {
  display: grid;
  gap: 3px;
}
.revision-list span,
.revision-list small,
.revision-empty {
  color: var(--ink-tertiary);
  font-size: 11px;
}
.revision-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}
@media (max-width: 1000px) {
  .editor-workspace {
    grid-template-columns: 240px 1fr;
  }
  .property-panel {
    display: none;
  }
  .story-editor-shell {
    overflow: auto;
  }
  .story-editor {
    min-width: 820px;
  }
  .editor-top {
    position: sticky;
    top: 0;
    z-index: 10;
  }
}
@media (prefers-reduced-motion: reduce) {
  .flow-canvas * {
    transition: none !important;
  }
}
</style>
