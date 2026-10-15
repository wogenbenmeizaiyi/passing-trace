import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { mediaApi, type MediaAccessResponse } from '@/api/media'
import {
  subjectsApi,
  type Subject,
  type SubjectGraph,
  type Timeline,
  type TimelineItem,
} from '@/api/subjects'
import SubjectAvatar from '@/features/subjects/SubjectAvatar.vue'
import SubjectPicker from '@/features/subjects/SubjectPicker.vue'
import SubjectRelations from '@/features/subjects/SubjectRelations.vue'
import SubjectTimeline from '@/features/subjects/SubjectTimeline.vue'
import SubjectFields from '@/features/subjects/SubjectFields.vue'
import SubjectFormView from '@/features/subjects/SubjectFormView.vue'

const profile = reactive({ avatarUrl: '' })
vi.mock('@/stores/profile', () => ({ useProfileStore: () => profile }))
vi.mock('@/api/media', () => ({ mediaApi: { access: vi.fn<typeof mediaApi.access>() } }))
vi.mock('@/api/subjects', () => ({
  subjectsApi: {
    list: vi.fn<typeof subjectsApi.list>(),
    get: vi.fn<typeof subjectsApi.get>(),
    update: vi.fn<typeof subjectsApi.update>(),
  },
}))
const revoke = vi.fn<(url: string) => void>()
const access = (url: string): MediaAccessResponse => ({
  url,
  contentType: 'image/png',
  expiresAt: '',
  inline: true,
})
const person = (id: string, kind = 0): Subject => ({
  id,
  kind,
  name: id,
  description: null,
  itemType: null,
  isSelf: id === 'self',
  state: 0,
  startedAt: null,
  endedAt: null,
  endReason: null,
  version: 3,
  timezone: 'Asia/Shanghai',
  fields: [],
  values: {},
  mediaIds: [],
  coverMediaId: null,
  ageDays: null,
})
const graph: SubjectGraph = {
  rootId: 'self',
  nodes: [person('self'), person('小猫', 1), person('自行车', 2)],
  relations: [
    {
      id: 'r1',
      fromSubjectId: 'self',
      toSubjectId: '小猫',
      label: '饲养',
      directed: true,
      revision: 1,
      startedAt: null,
      endedAt: null,
    },
    {
      id: 'r2',
      fromSubjectId: 'self',
      toSubjectId: '自行车',
      label: '原主人',
      directed: false,
      revision: 2,
      startedAt: null,
      endedAt: '2025-01-01T00:00:00Z',
    },
  ],
}
const item = (id: string): TimelineItem => ({
  sourceType: 'SubjectEntry',
  sourceId: id,
  title: id,
  content: '长正文'.repeat(80),
  kind: 'Record',
  state: 'Completed',
  occurredAt: null,
  createdAt: '',
  originSubjectId: '小猫',
  originSubjectName: '小猫',
  isReference: false,
  invalid: false,
  afterEnd: false,
  version: 1,
  mediaIds: [],
  fieldChanges: {},
  fieldDefinitions: [],
})
const timeline: Timeline = {
  groups: [
    { key: '2026-10', items: [item('新记录')] },
    { key: '2026-09', items: [item('旧记录')] },
  ],
  nextCursor: null,
  timezone: 'Asia/Shanghai',
}
async function routerAt(path = '/subjects') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/subjects', component: { template: '<div />' } },
      { path: '/subjects/:id', component: { template: '<div />' } },
      { path: '/subjects/:id/edit', component: { template: '<div />' } },
    ],
  })
  await router.push(path)
  return router
}
beforeEach(() => {
  vi.clearAllMocks()
  sessionStorage.clear()
  profile.avatarUrl = ''
  vi.stubGlobal(
    'URL',
    class extends URL {
      static revokeObjectURL = revoke
    },
  )
  vi.mocked(subjectsApi.list).mockResolvedValue(graph.nodes)
})
afterEach(() => vi.unstubAllGlobals())

describe('人物 Web 展示与选择', () => {
  it('头像请求及图片解码期间显示默认图标，加载失败也保留；解码成功才铺满节点', async () => {
    let resolve!: (value: MediaAccessResponse) => void
    vi.mocked(mediaApi.access).mockImplementation(
      () =>
        new Promise((r) => {
          resolve = r
        }),
    )
    const wrapper = mount(SubjectAvatar, {
      props: { subject: { ...person('cat', 1), coverMediaId: 'photo' }, node: true },
    })
    expect(wrapper.find('svg').exists()).toBe(true)
    expect(wrapper.find('img').exists()).toBe(false)
    resolve(access('blob:photo'))
    await flushPromises()
    expect(wrapper.find('svg').exists()).toBe(true)
    expect(wrapper.classes()).not.toContain('subject-avatar--loaded')
    await wrapper.get('img').trigger('load')
    expect(wrapper.find('svg').exists()).toBe(false)
    expect(wrapper.classes()).toContain('subject-avatar--loaded')
    await wrapper.get('img').trigger('error')
    expect(wrapper.find('svg').exists()).toBe(true)
    expect(wrapper.classes()).not.toContain('subject-avatar--loaded')
    wrapper.unmount()
    expect(revoke).toHaveBeenCalledWith('blob:photo')
  })
  it('切换头像时旧请求不覆盖新图片，离开后释放未完成请求返回的资源', async () => {
    let resolve!: (value: MediaAccessResponse) => void
    vi.mocked(mediaApi.access)
      .mockImplementationOnce(
        () =>
          new Promise((r) => {
            resolve = r
          }),
      )
      .mockResolvedValueOnce(access('blob:new'))
    const wrapper = mount(SubjectAvatar, {
      props: { subject: { ...person('cat', 1), coverMediaId: 'old' } },
    })
    await wrapper.setProps({ subject: { ...person('cat', 1), coverMediaId: 'new' } })
    await flushPromises()
    expect(wrapper.get('img').attributes('src')).toBe('blob:new')
    wrapper.unmount()
    resolve(access('blob:old'))
    await flushPromises()
    expect(revoke).toHaveBeenCalledWith('blob:new')
    expect(revoke).toHaveBeenCalledWith('blob:old')
  })
  it('自身头像沿用账号，变更来源后等待新图片解码，不释放账号资源', async () => {
    profile.avatarUrl = 'blob:self'
    const wrapper = mount(SubjectAvatar, { props: { subject: person('self') } })
    await wrapper.get('img').trigger('load')
    profile.avatarUrl = 'blob:self-new'
    await flushPromises()
    expect(wrapper.find('svg').exists()).toBe(true)
    expect(wrapper.get('img').attributes('src')).toBe('blob:self-new')
    expect(mediaApi.access).not.toHaveBeenCalled()
    wrapper.unmount()
    expect(revoke).not.toHaveBeenCalled()
  })
  it('关系默认收起，展开可按名称和历史范围筛选，收起移除长列表', async () => {
    const router = await routerAt()
    const wrapper = mount(SubjectRelations, {
      props: { graph, subjectId: 'self' },
      global: { plugins: [router] },
    })
    expect(wrapper.findAll('li')).toHaveLength(0)
    expect(wrapper.text()).toContain('2 条关系')
    await wrapper.get('button[aria-expanded]').trigger('click')
    expect(wrapper.findAll('li')).toHaveLength(2)
    await wrapper.get('select').setValue('history')
    expect(wrapper.findAll('li')).toHaveLength(1)
    expect(wrapper.get('li').text()).toContain('自行车')
    await wrapper.get('input[type="search"]').setValue('不存在')
    expect(wrapper.findAll('li')).toHaveLength(0)
    await wrapper.get('button[aria-expanded]').trigger('click')
    expect(wrapper.find('input').exists()).toBe(false)
    wrapper.unmount()
  })
  it('标记列表在选择窗口内显示，取消不更改标记，跨筛选保留勾选且排除所属人物', async () => {
    const wrapper = mount(SubjectPicker, { props: { exclude: '小猫', modelValue: ['self'] } })
    await flushPromises()
    expect(wrapper.get('dialog').attributes('open')).toBeUndefined()
    expect(wrapper.findAll('input[type="checkbox"]')).toHaveLength(2)
    const choose = wrapper.findAll('button').find((x) => x.text().startsWith('选择人物'))!
    await choose.trigger('click')
    await wrapper.get('input[value="自行车"]').setValue(true)
    await wrapper.get('input[type="search"]').setValue('self')
    const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })
    wrapper.get('input[type="search"]').element.dispatchEvent(enter)
    expect(enter.defaultPrevented).toBe(true)
    await wrapper
      .findAll('button')
      .find((x) => x.text() === '取消')!
      .trigger('click')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    await choose.trigger('click')
    expect((wrapper.get('input[value="自行车"]').element as HTMLInputElement).checked).toBe(false)
    await wrapper.get('input[value="自行车"]').setValue(true)
    await wrapper.get('input[type="search"]').setValue('self')
    await wrapper
      .findAll('button')
      .find((x) => x.text().startsWith('确定'))!
      .trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([['self', '自行车']])
    expect(wrapper.get('dialog').attributes('open')).toBeUndefined()
    wrapper.unmount()
  })
  it('时间轴默认只展开最新组，全部收起及单组展开返回后恢复，其他档案不继承', async () => {
    const router = await routerAt()
    const options = {
      props: { subjectId: '小猫', timeline, groupBy: 'month' },
      global: { plugins: [router] },
    }
    const wrapper = mount(SubjectTimeline, options)
    expect(wrapper.findAll('.subject-date-group[open]')).toHaveLength(1)
    expect(wrapper.findAll('.subject-body-disclosure[open]')).toHaveLength(0)
    await wrapper
      .findAll('button')
      .find((x) => x.text() === '全部收起')!
      .trigger('click')
    expect(wrapper.findAll('.subject-date-group[open]')).toHaveLength(0)
    const older = wrapper.findAll('.subject-date-group')[1]!
    ;(older.element as HTMLDetailsElement).open = true
    await older.trigger('toggle')
    wrapper.unmount()
    const restored = mount(SubjectTimeline, options)
    expect(restored.get('.subject-date-group[open]').text()).toContain('旧记录')
    await restored.setProps({ subjectId: '另一个档案' })
    expect(restored.get('.subject-date-group[open]').text()).toContain('新记录')
    restored.unmount()
  })
  it('隐藏字段定义不妨碍填写，电话号码保留前导零，布尔未填写与否不混淆', async () => {
    const values = reactive<Record<string, string | number | boolean | null>>({})
    const wrapper = mount(SubjectFields, {
      props: {
        modelValue: values,
        fields: [
          { id: 'phone', name: '电话号码', type: 'phone', removed: false },
          { id: 'done', name: '已完成', type: 'boolean', removed: false },
        ],
        allowDefinitions: true,
      },
    })
    expect(wrapper.findAll('details[open]')).toHaveLength(0)
    await wrapper.get('input[type="tel"]').setValue('0012345')
    await wrapper.get('select[aria-label="已完成"]').setValue('false')
    expect(values).toEqual({ phone: '0012345', done: false })
    await wrapper.get('select[aria-label="已完成"]').setValue('')
    expect(values.done).toBeNull()
    wrapper.unmount()
  })
  it('手机上传的独立头像可以保留，编辑只提交改变的字段且使用原版本', async () => {
    const cat = {
      ...person('cat', 1),
      coverMediaId: 'cover-only',
      fields: [{ id: 'weight', name: '体重', type: 'number' as const, removed: false }],
      values: { weight: 4 },
    }
    vi.mocked(subjectsApi.get).mockResolvedValue(cat)
    vi.mocked(subjectsApi.update).mockResolvedValue(cat)
    const router = await routerAt('/subjects/cat/edit')
    const wrapper = mount(SubjectFormView, {
      global: {
        plugins: [router],
        stubs: { WebAppHeader: true, SubjectMediaEditor: true, SubjectAvatar: true },
      },
    })
    await flushPromises()
    expect(wrapper.get('option[value="cover-only"]').text()).toBe('保留当前头像')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(subjectsApi.update).toHaveBeenCalledWith(
      'cat',
      expect.objectContaining({ coverMediaId: 'cover-only', clearCover: false, values: {} }),
      3,
    )
    wrapper.unmount()
  })
})
