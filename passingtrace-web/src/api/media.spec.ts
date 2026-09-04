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
    const upload = vi.spyOn(httpClient, 'upload').mockImplementation(async (_path, body, opts) => {
      opts?.onProgress?.(body.size)
      return ''
    })
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
    expect(upload).toHaveBeenCalledWith(
      '/api/v1/media/media-1/content',
      expect.any(File),
      expect.objectContaining({ contentType: 'image/png' }),
    )
    expect(progress.at(-1)).toBe(100)
  })

  it('分片上传按服务端 partSize 排序回传 ETag', async () => {
    const post = vi.spyOn(httpClient, 'post')
    const upload = vi
      .spyOn(httpClient, 'upload')
      .mockResolvedValueOnce('"part-1"')
      .mockResolvedValueOnce('"part-2"')
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

    expect(upload.mock.calls.map((call) => call[1].size)).toEqual([3, 2])
    expect(upload.mock.calls.map((call) => call[0])).toEqual([
      '/api/v1/media/media-2/parts/1/content',
      '/api/v1/media/media-2/parts/2/content',
    ])
    expect(post.mock.calls[1]).toEqual([
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
