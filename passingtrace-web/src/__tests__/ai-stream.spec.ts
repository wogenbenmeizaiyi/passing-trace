import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { User } from 'oidc-client-ts'
import { aiApi, type StreamEvent } from '@/api/ai'
import { useAuthStore } from '@/stores/auth'

describe('助手流式消息', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useAuthStore().user = {
      access_token: 'test-token',
      expired: false,
      profile: { sub: 'test' },
    } as User
  })
  afterEach(() => vi.unstubAllGlobals())

  function stubStream(text: string) {
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockResolvedValue(new Response(text, { headers: { 'content-type': 'text/event-stream' } }))
    vi.stubGlobal('fetch', fetchMock)
    return fetchMock
  }

  it('发送设备时区且只请求当前会话，正常完成的消息不报错', async () => {
    const fetchMock = stubStream(
      'event: delta\ndata: {"text":"金额为50元。"}\n\nevent: done\ndata: {"cached":false,"watermark":1}\n\n',
    )
    const events: StreamEvent[] = []
    await aiApi.sendMessage('selected-chat', '统计本月消费', (event) => events.push(event))
    const [url, options] = fetchMock.mock.calls[0]!
    expect(url).toContain('/selected-chat/messages')
    expect(JSON.parse(options!.body as string)).toEqual({
      content: '统计本月消费',
      timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    })
    expect(events.map((event) => event.type)).toEqual(['delta', 'done'])
  })

  it('缺少结束事件时不把连接断开当作回答成功', async () => {
    stubStream('event: delta\ndata: {"text":"正在统计"}\n\n')
    await expect(aiApi.sendMessage('chat', '问题', vi.fn())).rejects.toThrow(
      '这次回答中途断开了，请重新发送一次。',
    )
  })

  it('服务端的文字错误保留，不覆盖成通用断连错误', async () => {
    stubStream('event: error\r\ndata: {"message":"暂时无法完成统计，请稍后重试。"}\r\n\r\n')
    const events: StreamEvent[] = []
    await aiApi.sendMessage('chat', '问题', (event) => events.push(event))
    expect(events).toEqual([{ type: 'error', data: { message: '暂时无法完成统计，请稍后重试。' } }])
  })

  it('格式损坏不向用户展示JSON解析异常', async () => {
    stubStream('event: delta\ndata: {broken-json}\n\n')
    await expect(aiApi.sendMessage('chat', '问题', vi.fn())).rejects.toThrow(
      '这次回答中途断开了，请重新发送一次。',
    )
  })
})
