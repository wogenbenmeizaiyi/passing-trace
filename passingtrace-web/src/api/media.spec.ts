import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { httpClient } from '@/api/http-client'
import { mediaApi } from '@/api/media'

class FakeXmlHttpRequest {
  static requests: FakeXmlHttpRequest[] = []

  readonly upload: { onprogress: ((event: ProgressEvent) => void) | null } = { onprogress: null }
  status = 200
  onload: (() => void) | null = null
  onerror: (() => void) | null = null
  method = ''
  url = ''
  body: Blob | null = null
  headers: Record<string, string> = {}
  private readonly index: number

  constructor() {
    this.index = FakeXmlHttpRequest.requests.length + 1
    FakeXmlHttpRequest.requests.push(this)
  }

  open(method: string, url: string) {
    this.method = method
    this.url = url
  }

  setRequestHeader(name: string, value: string) {
    this.headers[name] = value
  }

  getResponseHeader(name: string) {
    return name.toLowerCase() === 'etag' ? `"part-${this.index}"` : null
  }

  send(body: Blob) {
    this.body = body
    this.upload.onprogress?.({ loaded: body.size } as ProgressEvent)
    this.onload?.()
  }
}

function file(name: string, type: string, bytes: number[]) {
  const value = new File([new Uint8Array(bytes)], name, { type })
  Object.defineProperty(value, 'arrayBuffer', {
    value: async () => Uint8Array.from(bytes).buffer,
  })
  return value
}

describe('mediaApi', () => {
  beforeEach(() => {
    FakeXmlHttpRequest.requests = []
    vi.stubGlobal('XMLHttpRequest', FakeXmlHttpRequest)
    vi.stubGlobal('crypto', {
      subtle: {
        digest: vi
          .fn<() => Promise<ArrayBuffer>>()
          .mockResolvedValue(new Uint8Array(32).fill(0xab).buffer),
      },
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('单次上传先申请签名、直传对象，再确认媒体', async () => {
    const post = vi.spyOn(httpClient, 'post')
    post.mockResolvedValueOnce({
      id: 'media-1',
      kind: 1,
      mode: 1,
      uploadUrl: 'https://s3.test/object',
      partSize: null,
      partCount: null,
      expiresAt: '2026-08-30T12:00:00Z',
    })
    post.mockResolvedValueOnce({
      id: 'media-1',
      fileName: 'photo.png',
      kind: 1,
      contentType: 'image/png',
      size: 4,
      status: 4,
      sortOrder: 0,
    })
    const progress: number[] = []

    const result = await mediaApi.upload(file('photo.png', 'image/png', [1, 2, 3, 4]), (value) =>
      progress.push(value.percent),
    )

    expect(result.id).toBe('media-1')
    expect(post).toHaveBeenCalledTimes(2)
    expect(post.mock.calls[0]?.[0]).toBe('/api/v1/media/uploads')
    expect(post.mock.calls[0]?.[1]).toMatchObject({
      body: {
        fileName: 'photo.png',
        contentType: 'image/png',
        size: 4,
        sha256: 'ab'.repeat(32),
      },
    })
    expect(post.mock.calls[1]).toEqual(['/api/v1/media/media-1/confirm', { body: { parts: null } }])
    expect(FakeXmlHttpRequest.requests[0]).toMatchObject({
      method: 'PUT',
      url: 'https://s3.test/object',
      headers: { 'Content-Type': 'image/png' },
    })
    expect(progress.at(-1)).toBe(100)
  })

  it('分片上传按服务端 partSize 排序回传 ETag', async () => {
    const post = vi.spyOn(httpClient, 'post')
    post
      .mockResolvedValueOnce({
        id: 'media-2',
        kind: 3,
        mode: 2,
        uploadUrl: null,
        partSize: 3,
        partCount: 2,
        expiresAt: '2026-08-30T12:00:00Z',
      })
      .mockResolvedValueOnce({ partNumber: 1, uploadUrl: 'https://s3.test/part-1' })
      .mockResolvedValueOnce({ partNumber: 2, uploadUrl: 'https://s3.test/part-2' })
      .mockResolvedValueOnce({
        id: 'media-2',
        fileName: 'note.txt',
        kind: 3,
        contentType: 'text/plain',
        size: 5,
        status: 4,
        sortOrder: 0,
      })

    await mediaApi.upload(file('note.txt', 'text/plain', [1, 2, 3, 4, 5]))

    expect(FakeXmlHttpRequest.requests.map((request) => request.body?.size)).toEqual([3, 2])
    expect(post.mock.calls[3]).toEqual([
      '/api/v1/media/media-2/confirm',
      {
        body: {
          parts: [
            { partNumber: 1, eTag: '"part-1"' },
            { partNumber: 2, eTag: '"part-2"' },
          ],
        },
      },
    ])
  })
})
