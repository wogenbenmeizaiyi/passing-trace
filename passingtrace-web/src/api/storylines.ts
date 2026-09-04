import { httpClient, type RequestOptions } from '@/api/http-client'
import type {
  SaveStorylineRequest,
  StorylineChangeRequest,
  StorylinePage,
  StorylineRevisionResponse,
  StorylineSaveResponse,
  StorylineTaxonomy,
} from '@/api/storylines-types'

export const storylinesApi = {
  taxonomy: (opts: RequestOptions = {}) =>
    httpClient.get<StorylineTaxonomy>('/api/v1/storyline-taxonomy', opts),
  list: (query: Record<string, string | number | undefined> = {}, opts: RequestOptions = {}) =>
    httpClient.get<StorylinePage>('/api/v1/storylines', { ...opts, query }),
  get: (id: string, opts: RequestOptions = {}) =>
    httpClient.get<StorylineRevisionResponse>(`/api/v1/storylines/${id}`, opts),
  revision: (id: string, revision: number, opts: RequestOptions = {}) =>
    httpClient.get<StorylineRevisionResponse>(
      `/api/v1/storylines/${id}/revisions/${revision}`,
      opts,
    ),
  revisions: (id: string, opts: RequestOptions = {}) =>
    httpClient.get<
      Array<{
        revision: number
        contentHash: string
        layoutState: number
        nodeCount: number
        createdAt: string
        isCurrent: boolean
      }>
    >(`/api/v1/storylines/${id}/revisions`, opts),
  create: (body: SaveStorylineRequest, idempotencyKey: string, opts: RequestOptions = {}) =>
    httpClient.post<StorylineSaveResponse>('/api/v1/storylines', { ...opts, body, idempotencyKey }),
  save: (
    id: string,
    body: SaveStorylineRequest,
    version: number,
    idempotencyKey: string,
    opts: RequestOptions = {},
  ) =>
    httpClient.put<StorylineSaveResponse>(`/api/v1/storylines/${id}`, {
      ...opts,
      body,
      ifMatch: version,
      idempotencyKey,
    }),
  change: (
    id: string,
    body: StorylineChangeRequest,
    version: number,
    idempotencyKey: string,
    opts: RequestOptions = {},
  ) =>
    httpClient.post<StorylineSaveResponse>(`/api/v1/storylines/${id}/changes`, {
      ...opts,
      body,
      ifMatch: version,
      idempotencyKey,
    }),
  restore: (
    id: string,
    revision: number,
    version: number,
    idempotencyKey: string,
    opts: RequestOptions = {},
  ) =>
    httpClient.post<StorylineSaveResponse>(
      `/api/v1/storylines/${id}/revisions/${revision}/restore`,
      { ...opts, ifMatch: version, idempotencyKey },
    ),
  remove: (id: string, version: number, opts: RequestOptions = {}) =>
    httpClient.delete<void>(`/api/v1/storylines/${id}`, { ...opts, ifMatch: version }),
}
