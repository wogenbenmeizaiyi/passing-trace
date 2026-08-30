import { httpClient, type RequestOptions } from '@/api/http-client'
import type { MediaResponse } from '@/api/events-types'

interface MediaUploadResponse {
  id: string
  kind: number
  mode: number
  uploadUrl: string | null
  partSize: number | null
  partCount: number | null
  expiresAt: string
}

interface PartUploadResponse {
  partNumber: number
  uploadUrl: string
  expiresAt: string
}

export interface UploadProgress {
  loaded: number
  total: number
  percent: number
}

export interface MediaAccessResponse {
  url: string
  expiresAt: string
  inline: boolean
}

async function sha256(file: File): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', await file.arrayBuffer())
  return [...new Uint8Array(digest)].map((value) => value.toString(16).padStart(2, '0')).join('')
}

function put(
  url: string,
  body: Blob,
  contentType: string | null,
  onProgress?: (loaded: number) => void,
) {
  return new Promise<string>((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('PUT', url)
    if (contentType) xhr.setRequestHeader('Content-Type', contentType)
    xhr.upload.onprogress = (event) => onProgress?.(event.loaded)
    xhr.onerror = () => reject(new Error('上传网络中断，请重试。'))
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(xhr.getResponseHeader('ETag') ?? '')
      } else {
        reject(new Error(`对象上传失败 (${xhr.status})`))
      }
    }
    xhr.send(body)
  })
}

export const mediaApi = {
  async upload(file: File, onProgress?: (value: UploadProgress) => void): Promise<MediaResponse> {
    const hash = await sha256(file)
    const session = await httpClient.post<MediaUploadResponse>('/api/v1/media/uploads', {
      body: {
        fileName: file.name,
        contentType: file.type || 'application/octet-stream',
        size: file.size,
        sha256: hash,
      },
    })
    if (session.mode === 1) {
      if (!session.uploadUrl) throw new Error('服务端没有返回上传地址。')
      await put(session.uploadUrl, file, file.type || 'application/octet-stream', (loaded) =>
        onProgress?.({ loaded, total: file.size, percent: Math.round((loaded / file.size) * 100) }),
      )
      return httpClient.post<MediaResponse>(`/api/v1/media/${session.id}/confirm`, {
        body: { parts: null },
      })
    }

    const partSize = session.partSize ?? 16 * 1024 * 1024
    const partCount = session.partCount ?? Math.ceil(file.size / partSize)
    const parts: Array<{ partNumber: number; eTag: string }> = []
    let completed = 0
    for (let partNumber = 1; partNumber <= partCount; partNumber += 1) {
      const start = (partNumber - 1) * partSize
      const chunk = file.slice(start, Math.min(file.size, start + partSize))
      const part = await httpClient.post<PartUploadResponse>(`/api/v1/media/${session.id}/parts`, {
        body: { partNumber },
      })
      const eTag = await put(part.uploadUrl, chunk, null, (loaded) =>
        onProgress?.({
          loaded: completed + loaded,
          total: file.size,
          percent: Math.round(((completed + loaded) / file.size) * 100),
        }),
      )
      parts.push({ partNumber, eTag })
      completed += chunk.size
    }
    return httpClient.post<MediaResponse>(`/api/v1/media/${session.id}/confirm`, {
      body: { parts },
    })
  },

  access(id: string, opts: RequestOptions = {}): Promise<MediaAccessResponse> {
    return httpClient.get<MediaAccessResponse>(`/api/v1/media/${id}/access`, opts)
  },

  remove(id: string): Promise<void> {
    return httpClient.delete<void>(`/api/v1/media/${id}`)
  },
}
