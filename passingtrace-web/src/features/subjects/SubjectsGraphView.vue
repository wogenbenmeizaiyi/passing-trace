<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import {
  VueFlow,
  Handle,
  Position,
  MarkerType,
  type ViewportTransform,
  type VueFlowStore,
  type NodeDragEvent,
} from '@vue-flow/core'
import { Controls } from '@vue-flow/controls'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import '@vue-flow/controls/dist/style.css'
import { subjectsApi, type SubjectGraph } from '@/api/subjects'
import WebAppHeader from '@/components/WebAppHeader.vue'
import { useProfileStore } from '@/stores/profile'
import {
  connectedFilter,
  separateSubjectNodes,
  subjectLayout,
  type SubjectPosition,
} from './graph-layout'
import { SubjectGraphSimulation } from './graph-simulation'
import SubjectAvatar from './SubjectAvatar.vue'
import SubjectRelations from './SubjectRelations.vue'
import { subjectType } from './subject-presentation'
import './subjects.css'

const graph = ref<SubjectGraph | null>(null),
  error = ref(''),
  query = ref(''),
  kind = ref(''),
  list = ref(false)
const router = useRouter(),
  profile = useProfileStore()
const saved = ref<ViewportTransform | undefined>(),
  flowKey = ref(0)
const graphElement = ref<HTMLElement | null>(null)
let stopViewportWatch: (() => void) | undefined
let canvasObserver: ResizeObserver | undefined
function initialized(flow: VueFlowStore) {
  stopViewportWatch?.()
  canvasObserver?.disconnect()
  // Control buttons update the viewport without emitting viewportChangeEnd.
  stopViewportWatch = watch(flow.viewport, viewport, { deep: true })
  const element = graphElement.value
  if (!element) return
  let previous = { width: element.clientWidth, height: element.clientHeight }
  try {
    const stored = JSON.parse(
      sessionStorage.getItem(`subject-canvas:${graph.value?.rootId}`) ?? 'null',
    )
    if (
      stored &&
      Number.isFinite(stored.width) &&
      Number.isFinite(stored.height) &&
      stored.width > 0 &&
      stored.height > 0
    )
      previous = stored
  } catch {
    /* Ignore obsolete canvas dimensions. */
  }
  canvasObserver = new ResizeObserver(([entry]) => {
    if (!entry || entry.contentRect.width <= 0 || entry.contentRect.height <= 0) return
    const next = { width: entry.contentRect.width, height: entry.contentRect.height }
    if (
      previous.width > 0 &&
      previous.height > 0 &&
      (previous.width !== next.width || previous.height !== next.height)
    ) {
      // Keep the same graph point at the centre while retaining the user's zoom and pan.
      void flow.setViewport({
        ...flow.viewport.value,
        x: flow.viewport.value.x + (next.width - previous.width) / 2,
        y: flow.viewport.value.y + (next.height - previous.height) / 2,
      })
    }
    previous = next
    if (graph.value)
      sessionStorage.setItem(`subject-canvas:${graph.value.rootId}`, JSON.stringify(next))
  })
  canvasObserver.observe(element)
}
const filter = computed(() =>
  graph.value
    ? connectedFilter(graph.value, query.value, kind.value, profile.nickname)
    : { matches: new Set<string>(), kept: new Set<string>() },
)
const positions = ref(new Map<string, SubjectPosition>())
let simulation: SubjectGraphSimulation | undefined
let animationFrame: number | undefined
let lastFrame: number | undefined

function animate(time: number) {
  animationFrame = undefined
  if (!simulation || list.value) return
  const seconds = lastFrame === undefined ? 1 / 60 : Math.min(1 / 30, (time - lastFrame) / 1000)
  lastFrame = time
  const active = simulation.step(seconds)
  positions.value = new Map(simulation.positions)
  if (active) animationFrame = requestAnimationFrame(animate)
  else {
    lastFrame = undefined
    savePositions()
  }
}
function startSimulation() {
  if (simulation?.active && animationFrame === undefined && !list.value) {
    lastFrame = undefined
    animationFrame = requestAnimationFrame(animate)
  }
}
function stopAnimation() {
  if (animationFrame !== undefined) cancelAnimationFrame(animationFrame)
  animationFrame = undefined
  lastFrame = undefined
}
watch(list, (showList) => {
  if (showList) stopAnimation()
  else startSimulation()
})
const nodes = computed(
  () =>
    graph.value?.nodes
      .filter((x) => filter.value.kept.has(x.id))
      .map((x) => ({
        id: x.id,
        type: 'subject',
        position: positions.value.get(x.id)!,
        draggable: true,
        connectable: false,
        data: {
          subject: x,
          name: x.isSelf ? `${profile.nickname}（自己）` : x.name,
          dimmed: !filter.value.matches.has(x.id),
        },
      })) ?? [],
)
const edges = computed(
  () =>
    graph.value?.relations
      .filter((x) => filter.value.kept.has(x.fromSubjectId) && filter.value.kept.has(x.toSubjectId))
      .map((x) => ({
        id: x.id,
        source: x.fromSubjectId,
        target: x.toSubjectId,
        type: 'straight',
        selectable: false,
        markerEnd: MarkerType.ArrowClosed,
        markerStart: x.directed ? undefined : MarkerType.ArrowClosed,
        style: { stroke: x.endedAt ? '#929c96' : 'var(--primary)' },
      })) ?? [],
)
function moveNode(id: string, position: SubjectPosition) {
  simulation?.move(id, position)
  if (simulation) positions.value = new Map(simulation.positions)
  startSimulation()
}
function beginDrag({ node }: NodeDragEvent) {
  simulation?.pin(node.id)
  startSimulation()
}
function dragNode({ node }: NodeDragEvent) {
  moveNode(node.id, node.position)
}
function savePositions() {
  if (graph.value)
    sessionStorage.setItem(
      `subject-positions:${graph.value.rootId}`,
      JSON.stringify([...positions.value]),
    )
}
function finishDrag(event: NodeDragEvent) {
  dragNode(event)
  simulation?.release()
  startSimulation()
  savePositions()
}
function moveWithKeyboard(event: KeyboardEvent, id: string) {
  const deltas: Record<string, SubjectPosition> = {
    ArrowLeft: { x: -24, y: 0 },
    ArrowRight: { x: 24, y: 0 },
    ArrowUp: { x: 0, y: -24 },
    ArrowDown: { x: 0, y: 24 },
  }
  const delta = deltas[event.key],
    position = positions.value.get(id)
  if (!delta || !position) return
  event.preventDefault()
  event.stopPropagation()
  moveNode(id, { x: position.x + delta.x, y: position.y + delta.y })
  savePositions()
}
function viewport(value: ViewportTransform) {
  if (graph.value && !list.value)
    sessionStorage.setItem(`subject-viewport:${graph.value.rootId}`, JSON.stringify(value))
}
function toggleList() {
  if (!list.value && graph.value) {
    const raw = sessionStorage.getItem(`subject-viewport:${graph.value.rootId}`)
    if (raw) saved.value = JSON.parse(raw) as ViewportTransform
  }
  list.value = !list.value
}
function center() {
  const zoom = 0.85,
    offset = (160 * zoom) / 2
  const root = positions.value.get(graph.value?.rootId ?? '') ?? { x: 0, y: 0 }
  saved.value = {
    x: (graphElement.value?.clientWidth ?? 700) / 2 - offset - root.x * zoom,
    y: (graphElement.value?.clientHeight ?? 520) / 2 - offset - root.y * zoom,
    zoom,
  }
  flowKey.value++
}
onMounted(async () => {
  try {
    graph.value = await subjectsApi.graph()
    positions.value = subjectLayout(graph.value).positions
    const storedPositions = sessionStorage.getItem(`subject-positions:${graph.value.rootId}`)
    if (storedPositions) {
      try {
        const restored: unknown = JSON.parse(storedPositions)
        if (Array.isArray(restored)) {
          for (const entry of restored) {
            if (
              !Array.isArray(entry) ||
              typeof entry[0] !== 'string' ||
              !positions.value.has(entry[0])
            )
              continue
            const position = entry[1] as SubjectPosition | undefined
            if (position && Number.isFinite(position.x) && Number.isFinite(position.y))
              positions.value.set(entry[0], { x: position.x, y: position.y })
          }
        }
      } catch {
        /* Ignore an obsolete or incomplete local layout. */
      }
    }
    separateSubjectNodes(positions.value, graph.value.rootId)
    simulation = new SubjectGraphSimulation(graph.value, positions.value)
    const raw = sessionStorage.getItem(`subject-viewport:${graph.value.rootId}`)
    if (raw) saved.value = JSON.parse(raw) as ViewportTransform
    const view = sessionStorage.getItem(`subject-view:${graph.value.rootId}`)
    if (view) {
      const state = JSON.parse(view)
      query.value = state.query ?? ''
      kind.value = state.kind ?? ''
      list.value = state.list === true
    }
    if (!storedPositions) {
      simulation.start()
      startSimulation()
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : '加载失败'
  }
})
onBeforeUnmount(() => {
  stopAnimation()
  stopViewportWatch?.()
  canvasObserver?.disconnect()
  savePositions()
  if (graph.value)
    sessionStorage.setItem(
      `subject-view:${graph.value.rootId}`,
      JSON.stringify({ query: query.value, kind: kind.value, list: list.value }),
    )
})
</script>
<template>
  <div class="app-shell">
    <WebAppHeader />
    <main class="workspace-main subject-page subject-workspace">
      <header class="subject-workspace-header">
        <h1>人物</h1>
        <div class="subject-toolbar" role="group" aria-label="人物视图工具">
          <input
            v-model="query"
            type="search"
            aria-label="搜索人物"
            placeholder="搜索人物、宠物或物品"
          /><select v-model="kind" aria-label="人物类型">
            <option value="">所有类型</option>
            <option value="0">人</option>
            <option value="1">宠物</option>
            <option value="2">物品</option></select
          ><button class="button button-secondary" @click="toggleList">
            {{ list ? '关系图' : '列表／关系清单' }}</button
          ><button v-if="!list" class="button button-secondary" @click="center">回到自己</button>
        </div>
        <RouterLink class="button button-primary" to="/subjects/new">新建档案</RouterLink>
      </header>
      <p v-if="error" class="error-banner subject-workspace-notice" role="alert">{{ error }}</p>
      <p v-if="!graph && !error" class="subject-workspace-notice" role="status">
        正在加载人物档案…
      </p>
      <p v-if="query || kind" class="subject-workspace-notice">淡色节点保留通向自己的连接路径。</p>
      <div
        v-if="graph && !list"
        class="subject-graph"
        ref="graphElement"
        aria-label="人物关系图，节点可拖动并自动避让，可用列表查看全部关系"
      >
        <VueFlow
          :key="flowKey"
          :nodes="nodes"
          :edges="edges"
          :default-viewport="saved"
          :fit-view-on-init="!saved"
          :nodes-connectable="false"
          :nodes-draggable="true"
          :node-drag-threshold="4"
          :min-zoom="0.1"
          :max-zoom="2"
          @viewport-change-end="viewport"
          @init="initialized"
          @node-click="router.push(`/subjects/${$event.node.id}`)"
          @node-drag-start="beginDrag"
          @node-drag="dragNode"
          @node-drag-stop="finishDrag"
        >
          <template #node-subject="{ data }">
            <Handle
              type="target"
              :position="Position.Left"
              class="subject-node-handle"
              aria-hidden="true"
            />
            <button
              class="subject-node"
              :title="data.name"
              :class="{
                'subject-node--self': data.subject.isSelf,
                'subject-node--dim': data.dimmed,
              }"
              @keydown.enter="router.push(`/subjects/${data.subject.id}`)"
              @keydown="moveWithKeyboard($event, data.subject.id)"
            >
              <SubjectAvatar :subject="data.subject" node /><strong>{{ data.name }}</strong
              ><small>{{ data.subject.state === 1 ? '已结束' : '进行中' }}</small>
            </button>
            <Handle
              type="source"
              :position="Position.Right"
              class="subject-node-handle"
              aria-hidden="true"
            />
          </template>
          <Controls :show-interactive="false" />
        </VueFlow>
      </div>
      <section v-else-if="graph" class="subject-workspace-list" aria-label="人物与关系清单">
        <header class="subject-section-heading">
          <h2>档案列表</h2>
          <span class="subject-muted">{{ filter.matches.size }} 份档案</span>
        </header>
        <div class="subject-grid">
          <RouterLink
            v-for="node in graph.nodes.filter((x) => filter.matches.has(x.id))"
            :key="node.id"
            class="subject-card"
            :class="{ 'subject-card--self': node.isSelf }"
            :to="`/subjects/${node.id}`"
            ><SubjectAvatar :subject="node" />
            <div class="subject-card-copy">
              <h2>{{ node.isSelf ? `${profile.nickname}（自己）` : node.name }}</h2>
              <div class="subject-badges">
                <span class="subject-badge">{{ subjectType(node) }}</span
                ><span class="subject-badge">{{ node.state === 1 ? '已结束' : '进行中' }}</span>
              </div>
              <p v-if="node.description">{{ node.description }}</p>
            </div>
            <span aria-hidden="true">›</span></RouterLink
          >
        </div>
        <SubjectRelations
          :graph="{
            ...graph,
            relations: graph.relations.filter(
              (r) => filter.kept.has(r.fromSubjectId) && filter.kept.has(r.toSubjectId),
            ),
          }"
        />
      </section>
    </main>
  </div>
</template>
