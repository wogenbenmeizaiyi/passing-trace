import type { EventKind, EventStatus } from '@/api/events-types'

export const StorylineStatus = { Ongoing: 1, Completed: 2 } as const
export type StorylineStatus = (typeof StorylineStatus)[keyof typeof StorylineStatus]
export const StorylineRelationType = { Sequence: 1, Branch: 2, Parallel: 3, Related: 4 } as const
export type StorylineRelationType =
  (typeof StorylineRelationType)[keyof typeof StorylineRelationType]
export const StorylineNodeEmphasis = { Normal: 1, Important: 2 } as const
export const StorylineLayoutState = { Arranged: 1, NeedsArrangement: 2 } as const

export interface StorylineStageInput {
  key: string
  title: string
  semanticOrder: number
}
export interface InlinePlanInput {
  title: string
  plannedAt?: string | null
  rawContent?: string | null
  timezone?: string | null
}
export interface StorylineNodeInput {
  key: string
  nodeType: 'existing-event' | 'new-plan'
  eventId?: number | null
  sourceRevision?: number | null
  newPlan?: InlinePlanInput | null
  stageKey?: string | null
  semanticOrder: number
  emphasis: number
}
export interface StorylineEdgeInput {
  key: string
  sourceNodeKey: string
  targetNodeKey: string
  relationType: StorylineRelationType
  label?: string | null
}
export interface StorylineWebLayoutInput {
  direction: 'LR' | 'TB'
  viewportX: number
  viewportY: number
  zoom: number
  nodes: Array<{
    nodeKey: string
    x: number
    y: number
    width?: number | null
    height?: number | null
  }>
  stages: Array<{ stageKey: string; x: number; y: number; width: number; height: number }>
}
export interface SaveStorylineRequest {
  title: string
  description?: string | null
  categoryKey: string
  status: StorylineStatus
  coverMediaAssetId?: string | null
  tags: string[]
  stages: StorylineStageInput[]
  nodes: StorylineNodeInput[]
  edges: StorylineEdgeInput[]
  webCanvasLayout?: StorylineWebLayoutInput | null
}
export interface StorylineSummary {
  id: string
  title: string
  description: string | null
  categoryKey: string
  categoryLabel: string
  status: StorylineStatus
  revision: number
  version: number
  coverMediaAssetId: string | null
  rangeStart: string | null
  rangeEnd: string | null
  nodeCount: number
  tags: string[]
  layoutState: number
  updatedAt: string
}
export interface StorylineNodeResponse {
  key: string
  eventId: number
  sourceRevision: number
  currentSourceRevision: number
  revisionState: 'upToDate' | 'updated' | 'deleted'
  kind: EventKind
  status: EventStatus
  title: string
  rawContent: string | null
  occurredAt: string | null
  stageKey: string | null
  semanticOrder: number
  emphasis: number
  place: string | null
  tags: string[]
  imageMediaAssetId: string | null
}
export interface StorylineRevisionResponse {
  id: string
  title: string
  description: string | null
  categoryKey: string
  categoryLabel: string
  status: StorylineStatus
  revision: number
  version: number
  coverMediaAssetId: string | null
  rangeStart: string | null
  rangeEnd: string | null
  layoutState: number
  tags: string[]
  stages: StorylineStageInput[]
  nodes: StorylineNodeResponse[]
  edges: StorylineEdgeInput[]
  outline: Array<{
    nodeKey: string
    stageKey: string | null
    topologicalOrder: number
    depth: number
    incomingCount: number
    outgoingCount: number
    startsBranch: boolean
    isMerge: boolean
  }>
  webCanvasLayout: StorylineWebLayoutInput | null
  updatedAt: string
}
export interface StorylineSaveResponse {
  storyline: StorylineRevisionResponse
  createdPlans: Record<string, number>
  undoRevision: number | null
}
export interface StorylinePage {
  items: StorylineSummary[]
  nextCursor: string | null
}
export interface StorylineTaxonomy {
  version: string
  categories: Array<{ key: string; label: string }>
  relations: Array<{ value: number; key: string; label: string }>
}
export interface StorylineChangeRequest {
  operation: string
  nodeKey?: string
  eventId?: number
  sourceRevision?: number
  newPlan?: InlinePlanInput
  stageKey?: string | null
  semanticOrder?: number
  emphasis?: number
  parentNodeKey?: string
  createBranch?: boolean
  title?: string
  description?: string | null
  categoryKey?: string
  status?: StorylineStatus
  tags?: string[]
}
