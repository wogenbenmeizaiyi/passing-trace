import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MarkerType, VueFlow, type ViewportTransform } from '@vue-flow/core'
import { ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import type {
  Subject,
  SubjectEntry,
  SubjectGraph,
  SubjectRelation,
  TimelineItem,
} from '@/api/subjects'
import { subjectsApi } from '@/api/subjects'
import {
  connectedFilter,
  separateSubjectNodes,
  subjectLayout,
  subjectNodeSpacing,
} from '@/features/subjects/graph-layout'
import SubjectDetailView from '@/features/subjects/SubjectDetailView.vue'
import { SubjectGraphSimulation, subjectLinkDistance } from '@/features/subjects/graph-simulation'
import SubjectEntryView from '@/features/subjects/SubjectEntryView.vue'
import SubjectsGraphView from '@/features/subjects/SubjectsGraphView.vue'
import AssistantMessageContent from '@/components/AssistantMessageContent'
import { defaultTimezone, toIsoWithOffset } from '@/utils/datetime'

vi.mock('@/stores/profile', () => ({ useProfileStore: () => ({ nickname: '我', avatarUrl: '' }) }))
vi.mock('@/api/subjects', () => ({
  subjectsApi: {
    get: vi.fn<typeof subjectsApi.get>(),
    graph: vi.fn<typeof subjectsApi.graph>(),
    timeline: vi.fn<typeof subjectsApi.timeline>(),
    entry: vi.fn<typeof subjectsApi.entry>(),
    list: vi.fn<typeof subjectsApi.list>(),
    createEntry: vi.fn<typeof subjectsApi.createEntry>(),
    updateEntry: vi.fn<typeof subjectsApi.updateEntry>(),
    decideEntry: vi.fn<typeof subjectsApi.decideEntry>(),
    previewLifecycle: vi.fn<typeof subjectsApi.previewLifecycle>(),
    lifecycle: vi.fn<typeof subjectsApi.lifecycle>(),
    requestDelete: vi.fn<typeof subjectsApi.requestDelete>(),
    relate: vi.fn<typeof subjectsApi.relate>(),
    updateRelation: vi.fn<typeof subjectsApi.updateRelation>(),
  },
}))
const selfId = '11111111-1111-4111-8111-111111111111',
  catId = '22222222-2222-4222-8222-222222222222',
  entryId = '33333333-3333-4333-8333-333333333333',
  weightId = '44444444-4444-4444-8444-444444444444'
const node = (id: string, name: string, isSelf = false): Subject => ({
  id,
  name,
  description: null,
  kind: isSelf ? 0 : 1,
  itemType: null,
  isSelf,
  state: 0,
  startedAt: null,
  endedAt: null,
  endReason: null,
  version: 3,
  timezone: 'Asia/Shanghai',
  fields: [{ id: weightId, name: '体重', type: 'number', unit: 'kg', removed: false }],
  values: {},
  mediaIds: [],
  coverMediaId: null,
  ageDays: null,
})
const edge = (fromSubjectId: string, toSubjectId: string): SubjectRelation => ({
  id: fromSubjectId + toSubjectId,
  fromSubjectId,
  toSubjectId,
  label: '关联',
  directed: false,
  revision: 1,
  startedAt: null,
  endedAt: '2026-01-01T00:00:00Z',
})
const cat = node(catId, '小猫'),
  graph: SubjectGraph = {
    rootId: selfId,
    nodes: [node(selfId, '自己', true), cat],
    relations: [edge(catId, selfId)],
  }
const plan: SubjectEntry = {
  id: entryId,
  subjectId: catId,
  subjectName: '小猫',
  sourceSubjectDeleted: false,
  kind: 1,
  state: 0,
  title: '下月增重',
  content: '',
  happenedAt: null,
  plannedAt: null,
  version: 2,
  timezone: 'Asia/Shanghai',
  fieldChanges: { [weightId]: 5 },
  actualFieldChanges: {},
  fieldDefinitions: cat.fields,
  markedSubjectIds: [],
  mediaIds: [],
}
const timelineItem = (
  sourceType: TimelineItem['sourceType'],
  sourceId: string,
  title: string,
): TimelineItem => ({
  sourceType,
  sourceId,
  title,
  content: null,
  kind: 'Record',
  state: 'Completed',
  occurredAt: '2026-09-01T00:00:00Z',
  createdAt: '2026-09-01T00:00:00Z',
  originSubjectId: catId,
  originSubjectName: '小猫',
  isReference: false,
  invalid: false,
  afterEnd: false,
  version: 1,
  mediaIds: [],
  fieldChanges: {},
  fieldDefinitions: [],
})
async function open(
  component:
    | typeof SubjectDetailView
    | typeof SubjectEntryView
    | typeof SubjectsGraphView
    | typeof AssistantMessageContent,
  path: string,
  props = {},
) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/subjects/entries/:entryId', component },
      { path: '/subjects/:id/entries/new', component },
      { path: '/subjects/:id', component },
      { path: '/subjects/:id/edit', component },
      { path: '/subjects', component },
      { path: '/events/:id', component },
      { path: '/assistant', component },
    ],
  })
  await router.push(path)
  const wrapper = mount(component, {
    props,
    global: {
      plugins: [router],
      stubs: {
        WebAppHeader: true,
        SubjectMedia: true,
        SubjectMediaEditor: true,
        SubjectPicker: true,
        Controls: true,
      },
    },
  })
  await flushPromises()
  return { wrapper, router }
}
beforeEach(() => {
  vi.clearAllMocks()
  sessionStorage.clear()
  vi.mocked(subjectsApi.get).mockResolvedValue(cat)
  vi.mocked(subjectsApi.graph).mockResolvedValue(graph)
  vi.mocked(subjectsApi.list).mockResolvedValue(graph.nodes)
  vi.mocked(subjectsApi.entry).mockResolvedValue(plan)
  vi.mocked(subjectsApi.timeline).mockResolvedValue({
    groups: [
      {
        key: '2026-09',
        items: [
          timelineItem('Event', '42', '原记录修车'),
          timelineItem('SubjectEntry', entryId, '专属疫苗'),
          { ...timelineItem('SubjectEntry', 'deleted', '引用内容已删除'), invalid: true },
        ],
      },
    ],
    nextCursor: null,
    timezone: 'Asia/Shanghai',
  })
  vi.mocked(subjectsApi.previewLifecycle).mockResolvedValue({
    subject: cat,
    plans: [plan],
    milestones: [],
  })
  vi.mocked(subjectsApi.lifecycle).mockResolvedValue(cat)
  vi.mocked(subjectsApi.decideEntry).mockResolvedValue({ ...plan, state: 1 })
})
afterEach(() => vi.unstubAllGlobals())
describe('人物与双来源时间轴', () => {
  it('相连节点拉远会跟随，靠近会排斥，拖住时固定，松手后继续稳定', () => {
    for (const distance of [190, 900]) {
      const positions = new Map([
        [selfId, { x: 0, y: 0 }],
        [catId, { x: distance, y: 0 }],
      ])
      const simulation = new SubjectGraphSimulation(graph, positions)
      simulation.pin(catId)
      simulation.step(1 / 60)
      expect(simulation.positions.get(catId)).toEqual({ x: distance, y: 0 })
      const direction = Math.sign(distance - subjectLinkDistance)
      expect(simulation.positions.get(selfId)!.x * direction).toBeGreaterThan(0)
      simulation.release()
      simulation.step(1 / 60)
      expect((simulation.positions.get(catId)!.x - distance) * direction).toBeLessThan(0)
      for (let i = 0; i < 1200 && simulation.active; i++) {
        simulation.step(1 / 60)
        const a = simulation.positions.get(selfId)!,
          b = simulation.positions.get(catId)!
        expect(Math.hypot(a.x - b.x, a.y - b.y)).toBeGreaterThanOrEqual(subjectNodeSpacing - 0.1)
      }
      const a = simulation.positions.get(selfId)!,
        b = simulation.positions.get(catId)!
      expect(Math.abs(Math.hypot(a.x - b.x, a.y - b.y) - subjectLinkDistance)).toBeLessThan(15)
      expect(simulation.active).toBe(false)
      const stopped = new Map(simulation.positions)
      simulation.step(1 / 60)
      expect(simulation.positions).toEqual(stopped)
    }
  })
  it('拖动固定当前节点，推开相邻及密集节点，远处节点保持不动', () => {
    const positions = new Map([
      ['dragged', { x: 0, y: 0 }],
      ['near', { x: 80, y: 0 }],
      ['next', { x: 240, y: 0 }],
      ['far', { x: 2000, y: 1000 }],
    ])
    separateSubjectNodes(positions, 'dragged')
    expect(positions.get('dragged')).toEqual({ x: 0, y: 0 })
    expect(positions.get('near')!.x).toBeGreaterThan(80)
    expect(positions.get('next')!.x).toBeGreaterThan(240)
    expect(positions.get('far')).toEqual({ x: 2000, y: 1000 })
    const dense = new Map(
      Array.from({ length: 30 }, (_, i) => [`node-${i}`, { x: 0, y: 0 }] as const),
    )
    separateSubjectNodes(dense, 'node-0')
    expect(dense.get('node-0')).toEqual({ x: 0, y: 0 })
    for (const set of [positions, dense]) {
      const points = [...set.values()]
      for (let i = 0; i < points.length; i++)
        for (let j = i + 1; j < points.length; j++)
          expect(
            Math.hypot(points[i]!.x - points[j]!.x, points[i]!.y - points[j]!.y),
          ).toBeGreaterThanOrEqual(subjectNodeSpacing - 0.1)
    }
  })
  it('图上仅有箭头，拖动避让后返回保留节点位置和关系，键盘也能移动节点', async () => {
    vi.stubGlobal(
      'requestAnimationFrame',
      vi.fn(() => 1),
    )
    vi.stubGlobal('cancelAnimationFrame', vi.fn())
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    )
    const { wrapper, router } = await open(SubjectsGraphView, '/subjects')
    const flow = wrapper.findComponent(VueFlow)
    const relations = JSON.stringify(graph.relations)
    const before = flow.props('nodes')!
    const destination = { x: 0, y: -40 }
    flow.vm.$emit('nodeDragStart', {
      node: { id: catId, position: before.find((x) => x.id === catId)!.position },
    })
    flow.vm.$emit('nodeDrag', { node: { id: catId, position: destination } })
    await flushPromises()
    const moved = flow.props('nodes')!
    expect(moved.find((x) => x.id === catId)!.position).toEqual(destination)
    expect(moved.find((x) => x.id === selfId)!.position).not.toEqual(
      before.find((x) => x.id === selfId)!.position,
    )
    expect(router.currentRoute.value.path).toBe('/subjects')
    expect(flow.props('edges')![0]).toMatchObject({
      markerStart: MarkerType.ArrowClosed,
      markerEnd: MarkerType.ArrowClosed,
    })
    expect(flow.props('edges')![0]!.label).toBeUndefined()
    expect(JSON.stringify(graph.relations)).toBe(relations)
    flow.vm.$emit('nodeDragStop', { node: { id: catId, position: destination } })
    await flushPromises()
    wrapper.unmount()
    const reopened = await open(SubjectsGraphView, '/subjects')
    expect(
      reopened.wrapper
        .findComponent(VueFlow)
        .props('nodes')!
        .find((x) => x.id === catId)!.position,
    ).toEqual(destination)
    await reopened.wrapper
      .findAll('.subject-node')
      .find((x) => x.text().includes('小猫'))!
      .trigger('keydown', { key: 'ArrowRight' })
    await flushPromises()
    expect(
      reopened.wrapper
        .findComponent(VueFlow)
        .props('nodes')!
        .find((x) => x.id === catId)!.position.x,
    ).toBe(24)
    reopened.wrapper.unmount()
  })
  it('图节点提供连线锚点，打开档案后恢复筛选与视图位置', async () => {
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    )
    Object.defineProperty(SVGElement.prototype, 'getBBox', {
      configurable: true,
      value: () => ({ x: 0, y: 0, width: 30, height: 14 }),
    })
    const { wrapper, router } = await open(SubjectsGraphView, '/subjects')
    expect(wrapper.findAll('.vue-flow__handle')).toHaveLength(4)
    await wrapper.get('input[aria-label="搜索人物"]').setValue('小猫')
    const viewport = ref({ x: 0, y: 0, zoom: 1 })
    wrapper.findComponent(VueFlow).vm.$emit('init', { viewport })
    // Controls change reactive state without emitting the user gesture event.
    viewport.value = { x: 90, y: 120, zoom: 1.2 }
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((x) => x.text() === '列表／关系清单')!
      .trigger('click')
    await wrapper
      .findAll('button')
      .find((x) => x.text() === '关系图')!
      .trigger('click')
    expect(wrapper.findComponent(VueFlow).props('defaultViewport')).toEqual(viewport.value)
    await wrapper
      .findAll('.subject-node')
      .find((x) => x.text().includes('小猫'))!
      .trigger('keydown', { key: 'Enter' })
    await flushPromises()
    expect(router.currentRoute.value.path).toBe(`/subjects/${catId}`)
    wrapper.unmount()
    const reopened = await open(SubjectsGraphView, '/subjects')
    expect(
      (reopened.wrapper.get('input[aria-label="搜索人物"]').element as HTMLInputElement).value,
    ).toBe('小猫')
    expect(reopened.wrapper.findComponent(VueFlow).props('defaultViewport')).toEqual({
      x: 90,
      y: 120,
      zoom: 1.2,
    })
    reopened.wrapper.unmount()
  })
  it('全屏画布尺寸变化时保留中心图点与缩放，离开页面停止观察', async () => {
    let resize!: ResizeObserverCallback
    const disconnect = vi.fn<() => void>()
    vi.stubGlobal(
      'ResizeObserver',
      class {
        private callback: ResizeObserverCallback
        constructor(callback: ResizeObserverCallback) {
          this.callback = callback
        }
        observe(target: Element) {
          if (target.classList.contains('subject-graph')) resize = this.callback
        }
        unobserve() {}
        disconnect = disconnect
      },
    )
    const { wrapper } = await open(SubjectsGraphView, '/subjects')
    Object.defineProperties(wrapper.get('.subject-graph').element, {
      clientWidth: { value: 1000, configurable: true },
      clientHeight: { value: 700, configurable: true },
    })
    const viewport = ref({ x: 120, y: 80, zoom: 1.2 })
    const setViewport = vi.fn<(value: ViewportTransform) => Promise<boolean>>(async (value) => {
      viewport.value = value
      return true
    })
    wrapper.findComponent(VueFlow).vm.$emit('init', { viewport, setViewport })
    resize(
      [{ contentRect: { width: 1000, height: 700 } } as ResizeObserverEntry],
      {} as ResizeObserver,
    )
    expect(setViewport).not.toHaveBeenCalled()
    resize(
      [{ contentRect: { width: 600, height: 400 } } as ResizeObserverEntry],
      {} as ResizeObserver,
    )
    await flushPromises()
    expect(viewport.value).toEqual({ x: -80, y: -70, zoom: 1.2 })
    expect(JSON.parse(sessionStorage.getItem(`subject-canvas:${selfId}`)!)).toEqual({
      width: 600,
      height: 400,
    })
    expect(JSON.parse(sessionStorage.getItem(`subject-viewport:${selfId}`)!)).toEqual(
      viewport.value,
    )
    const calls = disconnect.mock.calls.length
    wrapper.unmount()
    expect(disconnect.mock.calls.length).toBeGreaterThan(calls)
  })
  it('长链、环和历史关系布局只包含人物，筛选保留通向自己的路径', () => {
    const friend = node('friend', '朋友'),
      mother = node('mother', '母猫'),
      kitten = node('kitten', '幼猫')
    const chain: SubjectGraph = {
      rootId: selfId,
      nodes: [graph.nodes[0]!, friend, mother, kitten],
      relations: [
        edge(selfId, friend.id),
        edge(friend.id, mother.id),
        edge(mother.id, kitten.id),
        edge(mother.id, friend.id),
      ],
    }
    const layout = subjectLayout(chain)
    expect(layout.positions.size).toBe(4)
    expect(layout.positions.get(selfId)).toEqual({ x: 0, y: 0 })
    expect(connectedFilter(chain, '幼猫', '').kept).toEqual(
      new Set([kitten.id, mother.id, friend.id, selfId]),
    )
    expect(connectedFilter(chain, '不存在', '').kept).toEqual(new Set([selfId]))
    expect(connectedFilter(chain, '账号昵称', '', '账号昵称').matches).toEqual(new Set([selfId]))
    const siblings = Array.from({ length: 30 }, (_, i) => node(`sibling-${i}`, `物品 ${i}`))
    const dense: SubjectGraph = {
      rootId: selfId,
      nodes: [chain.nodes[0]!, ...siblings, kitten],
      relations: [...siblings.map((x) => edge(selfId, x.id)), edge(siblings[0]!.id, kitten.id)],
    }
    const densePositions = subjectLayout(dense).positions
    const radius = (id: string) => Math.hypot(densePositions.get(id)!.x, densePositions.get(id)!.y)
    expect(radius(kitten.id)).toBeGreaterThan(radius(siblings[0]!.id))
  })
  it('展示来源标签和真实来源链接，失效引用没有链接', async () => {
    const { wrapper } = await open(SubjectDetailView, `/subjects/${catId}`)
    expect(wrapper.text()).toContain('原记录')
    expect(wrapper.text()).toContain('来自人物')
    expect(wrapper.get('a[href="/events/42"]').text()).toBe('原记录修车')
    expect(wrapper.get(`a[href="/subjects/entries/${entryId}"]`).text()).toBe('专属疫苗')
    expect(wrapper.find('a[href*="deleted"]').exists()).toBe(false)
    wrapper.unmount()
  })
  it('结束预览默认保留所有计划', async () => {
    const { wrapper } = await open(SubjectDetailView, `/subjects/${catId}`)
    await wrapper
      .findAll('button')
      .find((x) => x.text() === '结束档案')!
      .trigger('click')
    await flushPromises()
    const form = wrapper.get('[data-testid="subject-lifecycle-form"]')
    await form.get('input[type="datetime-local"]').setValue('2026-09-01T12:00')
    await form.trigger('submit')
    await flushPromises()
    expect(subjectsApi.lifecycle).toHaveBeenCalledWith(
      catId,
      expect.objectContaining({ operation: 'end', cancelPlans: [] }),
      cat.version,
    )
    wrapper.unmount()
  })
  it('删除只申请授权并跳到指定聊天', async () => {
    vi.mocked(subjectsApi.requestDelete).mockResolvedValue({
      id: 'approval',
      conversationId: 'conversation',
      targetType: 'Subject',
      targetId: catId,
      title: '小猫',
      description: '删除资料',
      expiresAt: '2026-10-01T12:00:00Z',
    })
    const { wrapper, router } = await open(SubjectDetailView, `/subjects/${catId}`)
    await wrapper
      .findAll('button')
      .find((x) => x.text() === '申请删除档案')!
      .trigger('click')
    await flushPromises()
    expect(subjectsApi.requestDelete).toHaveBeenCalledWith('Subject', catId, expect.any(String))
    expect(router.currentRoute.value.fullPath).toBe('/assistant?conversation=conversation')
    wrapper.unmount()
  })
  it('完成专属计划要求实际时间，实际字段不会默认代入预期值', async () => {
    vi.mocked(subjectsApi.entry).mockResolvedValue({ ...plan, timezone: 'Pacific/Honolulu' })
    const { wrapper } = await open(SubjectEntryView, `/subjects/entries/${entryId}`)
    await wrapper
      .findAll('button')
      .find((x) => x.text() === '完成计划')!
      .trigger('click')
    const forms = wrapper.findAll('form'),
      actualForm = forms[1]!
    const expected = forms[0]!.get('input[aria-label="体重"]'),
      actual = actualForm.get('input[aria-label="体重"]')
    expect((expected.element as HTMLInputElement).value).toBe('5')
    expect((actual.element as HTMLInputElement).value).toBe('')
    expect(actual.attributes('id')).not.toBe(expected.attributes('id'))
    await actualForm.get('input[type="datetime-local"]').setValue('2026-09-15T12:00')
    await actualForm.trigger('submit')
    await flushPromises()
    expect(subjectsApi.decideEntry).toHaveBeenCalledWith(
      entryId,
      expect.objectContaining({
        operation: 'complete',
        actualFieldChanges: {},
        happenedAt: toIsoWithOffset('2026-09-15T12:00', defaultTimezone()),
      }),
      plan.version,
    )
    wrapper.unmount()
  })
  it('所属档案已删除时仍能读取保留的正文和历史字段', async () => {
    vi.mocked(subjectsApi.entry).mockResolvedValue({
      ...plan,
      content: '历史接种说明',
      sourceSubjectDeleted: true,
    })
    const { wrapper } = await open(SubjectEntryView, `/subjects/entries/${entryId}`)
    expect(wrapper.get('[aria-label="保留的记录"]').text()).toContain('历史接种说明')
    expect(wrapper.get('[aria-label="保留的记录"]').text()).toContain('5 kg')
    expect(wrapper.findAll('form')).toHaveLength(0)
    expect(subjectsApi.get).not.toHaveBeenCalled()
    wrapper.unmount()
  })
  it('AI 人物与专属内容引用按服务端证据生成链接并保留聊天，未知 ID 不生成链接', async () => {
    const { wrapper } = await open(AssistantMessageContent, '/assistant', {
      content: `[Subject #${catId}] [SubjectEntry #${entryId}] [Subject #${selfId}]`,
      conversationId: 'chat',
      subjects: [{ subjectId: catId, title: '小猫' }],
      subjectEntries: [{ entryId, title: '下月疫苗' }],
    })
    expect(wrapper.get(`a[href="/subjects/${catId}?conversation=chat"]`).text()).toBe('小猫')
    expect(wrapper.get(`a[href="/subjects/entries/${entryId}?conversation=chat"]`).text()).toBe(
      '下月疫苗',
    )
    expect(wrapper.findAll('a')).toHaveLength(2)
    expect(wrapper.text()).toContain('不可查看')
    wrapper.unmount()
  })
  it('危险操作默认收进菜单，取消结束不提交；自身没有结束或删除入口', async () => {
    const { wrapper } = await open(SubjectDetailView, `/subjects/${catId}`)
    expect(wrapper.get('.subject-more').attributes('open')).toBeUndefined()
    expect(wrapper.findAll('.subject-more .subject-danger')).toHaveLength(2)
    await wrapper
      .findAll('button')
      .find((x) => x.text() === '结束档案')!
      .trigger('click')
    await flushPromises()
    const dialog = wrapper.findAll('dialog').find((x) => x.text().includes('确认结束'))!
    expect(dialog.attributes('open')).toBeDefined()
    await dialog
      .findAll('button')
      .find((x) => x.text() === '取消')!
      .trigger('click')
    await flushPromises()
    expect(dialog.attributes('open')).toBeUndefined()
    expect(subjectsApi.lifecycle).not.toHaveBeenCalled()
    wrapper.unmount()
    vi.mocked(subjectsApi.get).mockResolvedValue(graph.nodes[0]!)
    const own = await open(SubjectDetailView, `/subjects/${selfId}`)
    expect(own.wrapper.find('[aria-label="更多档案操作"]').exists()).toBe(false)
    own.wrapper.unmount()
  })
  it('新增关系取消不写入，反向关联使用目标档案版本，编辑使用原关系修订', async () => {
    const { wrapper } = await open(SubjectDetailView, `/subjects/${catId}`)
    const add = wrapper.findAll('button').find((x) => x.text().includes('新增关联'))!
    await add.trigger('click')
    const form = wrapper.get('[data-testid="subject-relation-form"]')
    await form.get('input[maxlength="100"]').setValue('饲养')
    const dialog = form.element.closest('dialog')!
    dialog.querySelector<HTMLButtonElement>('footer button')!.click()
    await flushPromises()
    expect(subjectsApi.relate).not.toHaveBeenCalled()
    await add.trigger('click')
    await form.get('input[maxlength="100"]').setValue('饲养')
    await form.findAll('select')[1]!.setValue('incoming')
    vi.mocked(subjectsApi.relate).mockResolvedValue(graph)
    await form.trigger('submit')
    await flushPromises()
    expect(subjectsApi.relate).toHaveBeenCalledWith(
      selfId,
      expect.objectContaining({ toSubjectId: catId, directed: true, label: '饲养' }),
      graph.nodes[0]!.version,
    )
    await wrapper.get('button[aria-expanded]').trigger('click')
    await wrapper
      .findAll('button')
      .find((x) => x.text() === '编辑关联')!
      .trigger('click')
    await form.get('input[maxlength="100"]').setValue('家人')
    vi.mocked(subjectsApi.updateRelation).mockResolvedValue(graph.relations[0]!)
    await form.trigger('submit')
    await flushPromises()
    expect(subjectsApi.updateRelation).toHaveBeenCalledWith(
      graph.relations[0]!.id,
      expect.objectContaining({ label: '家人' }),
      graph.relations[0]!.revision,
    )
    wrapper.unmount()
  })
})
