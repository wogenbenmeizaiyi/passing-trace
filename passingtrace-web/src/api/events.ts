// Events 域的薄包装：负责把 UI 调用的入参映射成 HTTP 客户端调用。
// 并发令牌、幂等键等横切关注点都在调用方持有，service 不做缓存。

import { httpClient, type RequestOptions } from '@/api/http-client'
import type {
  CreateEventRequest,
  EventPage,
  EventResponse,
  ListEventsQuery,
  UpdateEventRequest,
} from '@/api/events-types'

function listQuery(input: ListEventsQuery) {
  return {
    limit: input.limit,
    cursor: input.cursor,
    kind: input.kind,
    status: input.status,
    from: input.from,
    to: input.to,
  }
}

export const eventsApi = {
  list(input: ListEventsQuery = {}, opts: RequestOptions = {}): Promise<EventPage> {
    return httpClient.get<EventPage>('/api/v1/events', {
      ...opts,
      query: listQuery(input),
    })
  },

  get(id: number, opts: RequestOptions = {}): Promise<EventResponse> {
    if (!Number.isFinite(id) || id <= 0) {
      return Promise.reject(new Error('无效的事件 id'))
    }
    return httpClient.get<EventResponse>(`/api/v1/events/${id}`, opts)
  },

  create(
    payload: CreateEventRequest,
    idempotencyKey: string,
    opts: RequestOptions = {},
  ): Promise<EventResponse> {
    return httpClient.post<EventResponse>('/api/v1/events', {
      ...opts,
      body: payload,
      idempotencyKey,
    })
  },

  update(
    id: number,
    payload: UpdateEventRequest,
    version: number,
    opts: RequestOptions = {},
  ): Promise<EventResponse> {
    return httpClient.patch<EventResponse>(`/api/v1/events/${id}`, {
      ...opts,
      body: payload,
      ifMatch: version,
    })
  },

  remove(id: number, version: number, opts: RequestOptions = {}): Promise<void> {
    return httpClient.delete<void>(`/api/v1/events/${id}`, {
      ...opts,
      ifMatch: version,
    })
  },
}
