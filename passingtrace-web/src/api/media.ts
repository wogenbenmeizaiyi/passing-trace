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
      await httpClient.upload(`/api/v1/media/${session.id}/content`, file, {
        contentType: file.type || 'application/octet-stream',
        onProgress: (loaded) =>
          onProgress?.({
            loaded,
            total: file.size,
            percent: Math.round((loaded / file.size) * 100),
          }),
      })
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
      const eTag = await httpClient.upload(
        `/api/v1/media/${session.id}/parts/${partNumber}/content`,
        chunk,
        {
          contentType: 'application/octet-stream',
          onProgress: (loaded) =>
            onProgress?.({
              loaded: completed + loaded,
              total: file.size,
              percent: Math.round(((completed + loaded) / file.size) * 100),
            }),
        },
      )
      parts.push({ partNumber, eTag })
      completed += chunk.size
    }
    return httpClient.post<MediaResponse>(`/api/v1/media/${session.id}/confirm`, {
      body: { parts },
    })
  },

  async access(id: string, opts: RequestOptions = {}): Promise<MediaAccessResponse> {
    const blob = await httpClient.blob(`/api/v1/media/${id}/content`, opts)
    return {
      url: URL.createObjectURL(blob),
      expiresAt: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
      inline: blob.type.startsWith('image/') || blob.type.startsWith('video/'),
    }
  },

  remove(id: string): Promise<void> {
    return httpClient.delete<void>(`/api/v1/media/${id}`)
  },
}
