// 与后端 Events API 契约对齐；枚举为数字，UI 在中英文案间互转。

export const EventKind = {
  Trace: 0,
  Plan: 1,
} as const

export type EventKind = (typeof EventKind)[keyof typeof EventKind]

export const EventStatus = {
  Planned: 0,
  Completed: 1,
  Cancelled: 2,
} as const

export type EventStatus = (typeof EventStatus)[keyof typeof EventStatus]

export const EventVisibility = {
  Private: 0,
} as const

export type EventVisibility = (typeof EventVisibility)[keyof typeof EventVisibility]

export const MediaKind = { Image: 1, Video: 2, File: 3 } as const
export type MediaKind = (typeof MediaKind)[keyof typeof MediaKind]

export interface MediaResponse {
  id: string
  fileName: string
  kind: MediaKind
  contentType: string
  size: number
  status: number
  sortOrder: number
}

export interface ManualTagInput {
  taxonomyKey?: string | null
  name?: string | null
}
export interface ManualClassification {
  primaryCategoryKey?: string | null
  tags: ManualTagInput[]
  suppressedAiTagKeys: string[]
}
export interface EventLabelResponse {
  taxonomyKey: string | null
  displayName: string
  origin: 'manual' | 'ai'
  confidence: number | null
}
export interface EffectiveClassification {
  primaryCategory: EventLabelResponse | null
  tags: EventLabelResponse[]
  taxonomyVersion: string
}
export interface EventLocationResponse {
  id?: number
  name: string
  address?: string | null
  province?: string | null
  city?: string | null
  district?: string | null
  adCode?: string | null
  providerPoiId?: string | null
  poiType?: string | null
  latitude?: number | null
  longitude?: number | null
  accuracyMeters?: number | null
  coordinateSystem: string
  source: number
  capturedAt?: string | null
}
export interface TaxonomyItem {
  key: string
  label: string
}
export interface EventTaxonomyResponse {
  version: string
  categories: TaxonomyItem[]
  behaviorTags: TaxonomyItem[]
}
export interface PlaceCandidate extends Omit<EventLocationResponse, 'id' | 'source'> {
  provider: string
  poiId: string
  distanceMeters: number | null
}

/** UI 展示用的中文文案。 */
export const EventKindLabel: Record<EventKind, string> = {
  [EventKind.Trace]: '当下记录',
  [EventKind.Plan]: '未来安排',
}

/** 新建表单使用动作式文案，避免把内部数据类型直接暴露给用户。 */
export const EventKindActionLabel: Record<EventKind, string> = {
  [EventKind.Trace]: '记录当下',
  [EventKind.Plan]: '写下计划',
}

export const EventStatusLabel: Record<EventStatus, string> = {
  [EventStatus.Planned]: '待执行',
  [EventStatus.Completed]: '已完成',
  [EventStatus.Cancelled]: '已取消',
}

export interface EventResponse {
  id: number
  kind: EventKind
  status: EventStatus
  title: string | null
  rawContent: string | null
  /** ISO 8601 带偏移，trace 类型相关。 */
  happenedAt: string | null
  /** ISO 8601 带偏移，plan 类型相关。 */
  plannedAt: string | null
  /** ISO 8601 带偏移，由后端在状态变 completed 时写入。 */
  completedAt: string | null
  /** IANA 时区名，例如 `Asia/Tokyo`。 */
  timezone: string
  visibility: EventVisibility
  sourceRevision: number
  /** 并发令牌，传给 `If-Match`。 */
  version: number
  /** ISO 8601 带偏移。 */
  createdAt: string
  /** ISO 8601 带偏移。 */
  updatedAt: string
  media: MediaResponse[]
  semanticStatus: string
  semanticSummary: string | null
  manualClassification: ManualClassification
  effectiveClassification: EffectiveClassification
  locations: EventLocationResponse[]
}

export interface CreateEventRequest {
  kind: EventKind
  title?: string | null
  rawContent?: string | null
  happenedAt?: string | null
  plannedAt?: string | null
  timezone: string
  mediaIds?: string[]
  classification?: ManualClassification
  locations?: EventLocationResponse[]
}

export interface UpdateEventRequest {
  title?: string | null
  rawContent?: string | null
  happenedAt?: string | null
  plannedAt?: string | null
  timezone: string
  mediaIds?: string[]
  classification?: ManualClassification
  locations?: EventLocationResponse[]
}

export interface EventPage {
  items: EventResponse[]
  nextCursor: number | null
}

export interface ListEventsQuery {
  limit?: number
  cursor?: number
  kind?: EventKind
  status?: EventStatus
  from?: string
  to?: string
  categoryKey?: string
  tagKeys?: string[]
  query?: string
}

export interface ProblemDetails {
  status: number
  title?: string
  detail?: string
  type?: string
  instance?: string
  /** 后端可能扩展的字段。 */
  [key: string]: unknown
}
