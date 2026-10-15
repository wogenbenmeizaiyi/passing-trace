import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { ModelViewerElement } from '@google/model-viewer'
import { mediaApi, type MediaAccessResponse } from '@/api/media'
import SubjectMedia from '@/features/subjects/SubjectMedia.vue'

vi.mock('@/api/media', () => ({ mediaApi: { access: vi.fn<typeof mediaApi.access>() } }))
vi.mock('@google/model-viewer', () => ({
  ModelViewerElement: class {
    static dracoDecoderLocation = ''
    static ktx2TranscoderLocation = ''
    static meshoptDecoderLocation = ''
  },
}))
const revoke = vi.fn<(url: string) => void>()
const access = (url: string, contentType = 'image/png'): MediaAccessResponse => ({
  url,
  contentType,
  expiresAt: '',
  inline: true,
})
beforeEach(() => {
  vi.clearAllMocks()
  vi.stubGlobal(
    'URL',
    class extends URL {
      static revokeObjectURL = revoke
    },
  )
})
afterEach(() => vi.unstubAllGlobals())

describe('人物私有媒体预览', () => {
  it('附件进入可见区域或点击后才下载，离开时停止观察', async () => {
    let intersect!: IntersectionObserverCallback
    const disconnect = vi.fn<() => void>()
    vi.stubGlobal(
      'IntersectionObserver',
      class {
        constructor(callback: IntersectionObserverCallback) {
          intersect = callback
        }
        observe() {}
        disconnect = disconnect
      },
    )
    vi.mocked(mediaApi.access).mockResolvedValue(access('blob:visible'))
    const wrapper = mount(SubjectMedia, { props: { id: 'media' } })
    expect(mediaApi.access).not.toHaveBeenCalled()
    intersect([{ isIntersecting: true } as IntersectionObserverEntry], {} as IntersectionObserver)
    await flushPromises()
    expect(mediaApi.access).toHaveBeenCalledOnce()
    wrapper.unmount()
    expect(disconnect).toHaveBeenCalled()
    expect(revoke).toHaveBeenCalledWith('blob:visible')
  })
  it('图片或视频失败后可重新获取地址，重试和离开页面会释放旧资源', async () => {
    vi.mocked(mediaApi.access)
      .mockResolvedValueOnce(access('blob:old'))
      .mockResolvedValueOnce(access('blob:retry', 'video/mp4'))
    const wrapper = mount(SubjectMedia, { props: { id: 'media', title: '猫' } })
    await flushPromises()
    await wrapper.get('img').trigger('error')
    expect(wrapper.get('[role="alert"]').text()).toContain('图片加载失败')
    await wrapper.get('button').trigger('click')
    await flushPromises()
    expect(revoke).toHaveBeenCalledWith('blob:old')
    expect(wrapper.get('video').attributes('src')).toBe('blob:retry')
    await wrapper.get('video').trigger('error')
    expect(wrapper.get('[role="alert"]').text()).toContain('视频加载失败')
    wrapper.unmount()
    expect(revoke).toHaveBeenCalledWith('blob:retry')
  })
  it('切换附件后较早返回的请求不会覆盖当前附件，并释放未使用地址', async () => {
    let finish!: (result: MediaAccessResponse) => void
    vi.mocked(mediaApi.access)
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finish = resolve
          }),
      )
      .mockResolvedValueOnce(access('blob:current'))
    const wrapper = mount(SubjectMedia, { props: { id: 'old' } })
    await wrapper.setProps({ id: 'current' })
    await flushPromises()
    finish(access('blob:stale'))
    await flushPromises()
    expect(wrapper.get('img').attributes('src')).toBe('blob:current')
    expect(revoke).toHaveBeenCalledWith('blob:stale')
    wrapper.unmount()
  })
  it('模型使用本地解码器，禁用 AR，支持视角重置和失败重试', async () => {
    vi.mocked(mediaApi.access).mockResolvedValue(access('blob:model', 'model/gltf-binary'))
    const wrapper = mount(SubjectMedia, { props: { id: 'glb' } })
    await flushPromises()
    expect(ModelViewerElement.dracoDecoderLocation).toContain('/model-decoders/draco/')
    expect(ModelViewerElement.ktx2TranscoderLocation).toContain('/model-decoders/basis/')
    expect(ModelViewerElement.meshoptDecoderLocation).toContain(
      '/model-decoders/meshopt_decoder.js',
    )
    const element = wrapper.get('model-viewer').element as ModelViewerElement
    expect(element.hasAttribute('ar')).toBe(false)
    element.jumpCameraToGoal = vi.fn<() => void>()
    await wrapper.get('button').trigger('click')
    expect(element.jumpCameraToGoal).toHaveBeenCalledOnce()
    await wrapper.get('model-viewer').trigger('error')
    expect(wrapper.get('[role="alert"]').text()).toContain('三维模型加载失败')
    wrapper.unmount()
    expect(revoke).toHaveBeenCalledWith('blob:model')
  })
})
