import { afterEach, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { httpClient } from '@/api/http-client'
import { useSocialStore } from '@/stores/social'
vi.mock('@/api/http-client', () => ({
  httpClient: { get: vi.fn<typeof httpClient.get>(), stream: vi.fn<typeof httpClient.stream>() },
}))
afterEach(() => vi.useRealTimers())
it('短暂重连不提示，十秒后提示，恢复清除，不调用退出登录', async () => {
  vi.useFakeTimers()
  setActivePinia(createPinia())
  vi.mocked(httpClient.get).mockResolvedValue({ count: 2 })
  let online = false
  let controller!: ReadableStreamDefaultController<Uint8Array>
  vi.mocked(httpClient.stream).mockImplementation(async () => {
    if (!online) throw new Error('offline')
    return new Response(
      new ReadableStream({
        start(c) {
          controller = c
        },
      }),
    )
  })
  const store = useSocialStore()
  const running = store.start()
  await vi.advanceTimersByTimeAsync(9999)
  expect(store.reconnecting).toBe(false)
  expect(store.unread).toBe(2)
  await vi.advanceTimersByTimeAsync(1)
  expect(store.reconnecting).toBe(true)
  online = true
  await vi.advanceTimersByTimeAsync(2000)
  expect(store.online).toBe(true)
  expect(store.reconnecting).toBe(false)
  store.stop()
  controller.close()
  await running
})
