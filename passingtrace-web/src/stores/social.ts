import { defineStore } from 'pinia'
import { ref } from 'vue'
import { httpClient } from '@/api/http-client'

export const useSocialStore = defineStore('social', () => {
  const unread = ref(0)
  const change = ref(0)
  const online = ref(false)
  let controller: AbortController | null = null
  let cursor = 0
  async function refresh() {
    const owner = controller
    const result = await httpClient.get<{ count: number }>('/api/v1/conversations/unread')
    if (owner && controller === owner && !owner.signal.aborted) unread.value = result.count
  }
  function stop() {
    controller?.abort()
    controller = null
    cursor = 0
    unread.value = 0
    online.value = false
    change.value = 0
  }
  async function start() {
    stop()
    const current = new AbortController()
    controller = current
    void refresh().catch(() => {})
    while (!current.signal.aborted) {
      try {
        const response = await httpClient.stream(
          `/api/v1/notifications/stream?after=${cursor}`,
          current.signal,
        )
        online.value = true
        const reader = response.body!.getReader()
        const decoder = new TextDecoder()
        let buffer = ''
        while (!current.signal.aborted) {
          const { done, value } = await reader.read()
          if (done || current.signal.aborted || controller !== current) break
          buffer += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n')
          let at: number
          let changed = false
          while ((at = buffer.indexOf('\n\n')) >= 0) {
            const frame = buffer.slice(0, at)
            buffer = buffer.slice(at + 2)
            const id = Number(/^id:\s*(\d+)/m.exec(frame)?.[1])
            if (id > cursor) {
              cursor = id
              changed = true
            }
          }
          if (changed) {
            change.value++
            void refresh().catch(() => {})
          }
        }
      } catch {
        if (!current.signal.aborted) online.value = false
      }
      if (!current.signal.aborted)
        await new Promise<void>((resolve) => {
          const finish = () => {
            clearTimeout(timer)
            current.signal.removeEventListener('abort', finish)
            resolve()
          }
          const timer = window.setTimeout(finish, 2000)
          current.signal.addEventListener('abort', finish, { once: true })
        })
    }
  }
  return { unread, change, online, start, stop, refresh }
})
