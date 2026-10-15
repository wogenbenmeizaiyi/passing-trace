import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import { defineComponent, h, nextTick } from 'vue'
import { useVueFlow } from '@vue-flow/core'
import type { eventsApi } from '@/api/events'
import StorylineEditorView from '@/views/StorylineEditorView.vue'
import { connectionProblem } from '@/utils/storyline-connections'

vi.mock('@/api/events', () => ({
  eventsApi: {
    taxonomy: vi
      .fn<typeof eventsApi.taxonomy>()
      .mockResolvedValue({ version: 'test', categories: [], behaviorTags: [] }),
    list: vi.fn<typeof eventsApi.list>().mockResolvedValue({ items: [], nextCursor: null }),
  },
}))
vi.mock('@/api/storylines', () => ({ storylinesApi: {} }))

const nodes = ['a', 'b', 'c', 'd'].map((id) => ({ id }))
describe('故事线连接校验', () => {
  it('允许分支、汇合和多个起点', () => {
    const edges = [
      { source: 'a', target: 'b' },
      { source: 'a', target: 'c' },
    ]
    expect(connectionProblem('b', 'd', nodes, edges)).toBeNull()
    expect(connectionProblem('c', 'b', nodes, edges)).toBeNull()
    expect(connectionProblem('d', 'c', nodes, edges)).toBeNull()
  })
  it('拒绝自环、重复边、无效端点和间接循环', () => {
    const edges = [
      { source: 'a', target: 'b' },
      { source: 'b', target: 'c' },
    ]
    expect(connectionProblem('a', 'a', nodes, edges)).toContain('自己')
    expect(connectionProblem('a', 'b', nodes, edges)).toContain('已经连接')
    expect(connectionProblem('a', 'missing', nodes, edges)).toContain('已移除')
    expect(connectionProblem('c', 'a', nodes, edges)).toContain('循环')
  })
})

describe('故事线编辑器连接操作', () => {
  let wrapper: VueWrapper
  let flow: ReturnType<typeof useVueFlow>

  beforeEach(async () => {
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        disconnect() {}
        unobserve() {}
      },
    )
    // jsdom has no hit-testing; Vue Flow falls back to the clicked handle in the DOM.
    vi.stubGlobal(
      'DOMMatrixReadOnly',
      class {
        m22 = 1
      },
    )
    Object.defineProperty(document, 'elementFromPoint', { configurable: true, value: () => null })
    sessionStorage.clear()
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/storylines/new', component: StorylineEditorView }],
    })
    await router.push('/storylines/new')
    wrapper = mount(
      defineComponent({
        setup() {
          flow = useVueFlow('storyline-editor')
          return () => h(RouterView)
        },
      }),
      {
        attachTo: document.body,
        global: {
          plugins: [router],
          stubs: {
            WebAppHeader: true,
            Background: true,
            MiniMap: true,
            Controls: true,
          },
        },
      },
    )
    await flushPromises()
    for (const title of ['出发', '散步']) {
      await wrapper.get('button[title="创建轻量计划"]').trigger('click')
      await wrapper.get('.inline-plan input').setValue(title)
      await wrapper.get('.inline-plan button:last-child').trigger('click')
    }
    await flushPromises()
  })
  afterEach(() => {
    wrapper?.unmount()
    sessionStorage.clear()
    vi.unstubAllGlobals()
  })
  const source = () => wrapper.get('[aria-label="从这里连接：出发"]')
  const target = () => wrapper.get('[aria-label="连接到：散步"]')
  async function connect() {
    await source().trigger('click', { clientX: 100, clientY: 100 })
    await target().trigger('click', { clientX: 400, clientY: 100 })
    await flushPromises()
  }

  it('点击起点后，无需按住鼠标就更新预览；点击终点只创建一条连接', async () => {
    await source().trigger('click', { clientX: 100, clientY: 100 })
    expect(wrapper.get('[role="status"]').text()).toContain('再点击')
    await wrapper
      .get('.flow-canvas')
      .trigger('mousemove', { clientX: 320, clientY: 180, buttons: 0 })
    expect(flow.connectionStartHandle.value).not.toBeNull()
    expect(flow.connectionPosition.value).toEqual({ x: 320, y: 180 })
    await target().trigger('click', { clientX: 400, clientY: 100 })
    await flushPromises()
    expect(flow.edges.value).toHaveLength(1)
    expect(flow.connectionStartHandle.value).toBeNull()
    expect(flow.connectionClickStartHandle.value).toBeNull()
    expect(wrapper.get('.connection-editor').text()).toContain('出发')
    expect(wrapper.get('.connection-editor').text()).toContain('散步')
    expect(wrapper.get('.canvas-connection-tools button').text()).toBe('删除连接')
    await connect()
    expect(flow.edges.value).toHaveLength(1)
  })

  it('Esc 和空白处都可取消连接，不留下关系', async () => {
    await source().trigger('click')
    await source().trigger('keydown', { key: 'Escape' })
    expect(flow.connectionClickStartHandle.value).toBeNull()
    expect(flow.connectionStartHandle.value).toBeNull()
    await source().trigger('click')
    await wrapper.get('.vue-flow__pane').trigger('click')
    expect(flow.connectionClickStartHandle.value).toBeNull()
    expect(flow.edges.value).toHaveLength(0)
  })

  it('删除仅解除连接，撤销/重做保留节点和关系类型', async () => {
    await connect()
    await wrapper.get('.connection-editor select').setValue('2')
    await wrapper.get('.connection-delete').trigger('click')
    expect(flow.edges.value).toHaveLength(0)
    expect(flow.nodes.value).toHaveLength(2)
    await wrapper.get('.editor-actions button:first-child').trigger('click')
    await nextTick()
    expect(flow.edges.value).toHaveLength(1)
    expect(flow.edges.value[0]?.data.relationType).toBe(2)
    await wrapper.get('.editor-actions button:nth-child(2)').trigger('click')
    expect(flow.edges.value).toHaveLength(0)
  })

  it('键盘可连接，编辑输入框时 Delete 不删除连接', async () => {
    await source().trigger('keydown', { key: 'Enter' })
    await target().trigger('keydown', { key: ' ' })
    await flushPromises()
    expect(flow.edges.value).toHaveLength(1)
    await wrapper.get('#storyline-title').trigger('keydown', { key: 'Delete' })
    expect(flow.edges.value).toHaveLength(1)
    await wrapper.get('.flow-canvas').trigger('keydown', { key: 'Delete' })
    expect(flow.edges.value).toHaveLength(0)
    expect(flow.nodes.value).toHaveLength(2)
  })
})
