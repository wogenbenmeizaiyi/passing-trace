import { httpClient } from '@/api/http-client'
import type { ApprovalRequest } from '@/api/ai'

export type SubjectValue = string | number | boolean | null
export interface SubjectField {
  id: string
  name: string
  type: 'text' | 'number' | 'date' | 'boolean' | 'select' | 'phone'
  unit?: string | null
  options?: string[] | null
  key?: string | null
  removed: boolean
}
export interface Subject {
  id: string
  name: string
  description: string | null
  kind: number
  itemType: string | null
  isSelf: boolean
  state: number
  startedAt: string | null
  endedAt: string | null
  endReason: string | null
  version: number
  timezone: string
  fields: SubjectField[]
  values: Record<string, SubjectValue>
  mediaIds: string[]
  coverMediaId: string | null
  ageDays: number | null
}
export interface SubjectRelation {
  id: string
  fromSubjectId: string
  toSubjectId: string
  label: string
  directed: boolean
  startedAt: string | null
  endedAt: string | null
  revision: number
}
export interface SubjectGraph {
  rootId: string
  nodes: Subject[]
  relations: SubjectRelation[]
}
export interface SubjectEntry {
  id: string
  subjectId: string
  subjectName: string
  sourceSubjectDeleted: boolean
  kind: number
  state: number
  title: string
  content: string | null
  happenedAt: string | null
  plannedAt: string | null
  version: number
  timezone: string
  fieldChanges: Record<string, SubjectValue>
  actualFieldChanges: Record<string, SubjectValue>
  fieldDefinitions: SubjectField[]
  markedSubjectIds: string[]
  mediaIds: string[]
}
export interface TimelineItem {
  sourceType: 'Event' | 'SubjectEntry' | 'Milestone'
  sourceId: string
  title: string
  content: string | null
  kind: string
  state: string
  occurredAt: string | null
  createdAt: string
  originSubjectId: string | null
  originSubjectName: string | null
  isReference: boolean
  invalid: boolean
  afterEnd: boolean
  version: number
  mediaIds: string[]
  fieldChanges: Record<string, SubjectValue> | null
  fieldDefinitions: SubjectField[] | null
}
export interface Timeline {
  groups: { key: string; items: TimelineItem[] }[]
  nextCursor: string | null
  timezone: string
}
export interface RelationInput {
  toSubjectId: string
  label: string
  directed?: boolean
  startedAt?: string | null
  endedAt?: string | null
}
export interface SubjectInput {
  kind?: number
  name?: string
  description?: string
  itemType?: string | null
  relations?: RelationInput[]
  startedAt?: string | null
  timezone?: string
  fields?: SubjectField[]
  values?: Record<string, SubjectValue>
  mediaIds?: string[]
  coverMediaId?: string | null
  clearCover?: boolean
  effectiveAt?: string | null
}
export interface EntryInput {
  kind?: number
  title?: string
  content?: string
  happenedAt?: string | null
  plannedAt?: string | null
  timezone?: string
  fieldChanges?: Record<string, SubjectValue>
  markedSubjectIds?: string[]
  mediaIds?: string[]
  clearPlannedAt?: boolean
}
export interface LifecycleInput {
  operation: string
  effectiveAt: string
  reason?: string
  note?: string
  cancelPlans?: { id: string; version: number }[]
  correctsId?: string
  undoEnd?: boolean
}
const root = '/api/v1/subjects'
export const subjectsApi = {
  list: () => httpClient.get<Subject[]>(root),
  graph: () => httpClient.get<SubjectGraph>(`${root}/graph`),
  get: (id: string) => httpClient.get<Subject>(`${root}/${id}`),
  presets: (kind: number, itemType?: string) =>
    httpClient.get<SubjectField[]>(`${root}/presets`, { query: { kind, itemType } }),
  create: (body: SubjectInput, idempotencyKey: string) =>
    httpClient.post<Subject>(root, { body, idempotencyKey }),
  update: (id: string, body: SubjectInput, ifMatch: number) =>
    httpClient.patch<Subject>(`${root}/${id}`, { body, ifMatch }),
  timeline: (id: string, query: Record<string, string | number | undefined>) =>
    httpClient.get<Timeline>(`${root}/${id}/timeline`, { query }),
  relate: (id: string, body: RelationInput, ifMatch: number) =>
    httpClient.post<SubjectGraph>(`${root}/${id}/relations`, { body, ifMatch }),
  updateRelation: (
    id: string,
    body: Partial<SubjectRelation> & { resume?: boolean },
    ifMatch: number,
  ) => httpClient.patch<SubjectRelation>(`${root}/relations/${id}`, { body, ifMatch }),
  entry: (id: string) => httpClient.get<SubjectEntry>(`${root}/entries/${id}`),
  createEntry: (id: string, body: EntryInput, idempotencyKey: string) =>
    httpClient.post<SubjectEntry>(`${root}/${id}/entries`, { body, idempotencyKey }),
  updateEntry: (id: string, body: EntryInput, ifMatch: number) =>
    httpClient.patch<SubjectEntry>(`${root}/entries/${id}`, { body, ifMatch }),
  decideEntry: (
    id: string,
    body: {
      operation: string
      happenedAt?: string
      actualFieldChanges?: Record<string, SubjectValue>
    },
    ifMatch: number,
  ) => httpClient.post<SubjectEntry>(`${root}/entries/${id}/decision`, { body, ifMatch }),
  previewLifecycle: (id: string) =>
    httpClient.get<{
      subject: Subject
      plans: SubjectEntry[]
      milestones: Array<{
        id: string
        operation: string
        voidedAt: string | null
        effectiveAt: string
      }>
    }>(`${root}/${id}/lifecycle/preview`),
  lifecycle: (id: string, body: LifecycleInput, ifMatch: number) =>
    httpClient.post<Subject>(`${root}/${id}/lifecycle`, { body, ifMatch }),
  requestDelete: (targetType: string, targetId: string, requestId: string) =>
    httpClient.post<ApprovalRequest>(`${root}/delete-requests`, {
      body: { targetType, targetId, requestId },
    }),
}
