import { httpClient } from '@/api/http-client'
import { useAuthStore } from '@/stores/auth'

export interface ConversationSummary {
  id: string
  title: string
  createdAt: string
  updatedAt: string
}

export interface ConversationDetail extends ConversationSummary {
  messages: Array<{
    id: number
    role: string
    content: string
    createdAt: string
    evidence: EvidenceBundle | null
  }>
}

export interface UserMemory {
  id: number
  type: string
  content: string
  confidence: number
  status: string
  updatedAt: string
  evidenceEventIds: number[]
}

export interface SemanticResult {
  eventId: number
  sourceRevision: number
  status: string
  summary: string | null
  semantic: unknown
  model: string
  pipelineVersion: string
  createdAt: string
  completedAt: string | null
  error: string | null
}

export interface EvidenceBundle {
  records: Array<{ eventId: number; title: string | null }>
  memories: Array<{ memoryId: number; content: string }>
  aggregate: string | null
  amapPlaces?: AmapPlaceEvidence[]
  actions?: AssistantAction[]
}

export interface AmapPlaceEvidence {
  candidateId: string
  poiId: string | null
  name: string
  address: string | null
  province: string | null
  city: string | null
  district: string | null
  latitude: number
  longitude: number
  coordinateSystem: 'GCJ02'
  source: 'amap-live'
}

export interface AssistantAction {
  type: 'amap-navigation' | 'amap-trip-map'
  provider: 'amap'
  label: string
  placeName: string
  address: string | null
  latitude: number
  longitude: number
  coordinateSystem: 'GCJ02'
  poiId: string | null
  source: 'amap-live' | 'personal-record'
  eventId?: number | null
  locationId?: number | null
  webUrl?: string | null
}

export interface AiCapabilities {
  amap: { available: boolean; capabilities: string[] }
}

export type StreamEvent =
  | { type: 'delta'; data: { text: string; replacement?: boolean; cached?: boolean } }
  | { type: 'evidence'; data: EvidenceBundle }
  | { type: 'action'; data: AssistantAction }
  | { type: 'done'; data: { cached: boolean; watermark: number } }
  | { type: 'error'; data: { message: string } }

function apiUrl(path: string) {
  const base = (import.meta.env.VITE_EVENTS_API_BASE_URL ?? '').replace(/\/$/, '')
  return `${base}${path}`
}

export const aiApi = {
  listConversations: () => httpClient.get<ConversationSummary[]>('/api/v1/ai/conversations'),
  createConversation: (title?: string) =>
    httpClient.post<ConversationSummary>('/api/v1/ai/conversations', { body: { title } }),
  getConversation: (id: string) =>
    httpClient.get<ConversationDetail>(`/api/v1/ai/conversations/${id}`),
  deleteConversation: (id: string) => httpClient.delete<void>(`/api/v1/ai/conversations/${id}`),
  listMemories: () => httpClient.get<UserMemory[]>('/api/v1/ai/memories'),
  updateMemory: (id: number, body: { content?: string; type?: string; status?: string }) =>
    httpClient.patch<UserMemory>(`/api/v1/ai/memories/${id}`, { body }),
  deleteMemory: (id: number) => httpClient.delete<void>(`/api/v1/ai/memories/${id}`),
  clearMemories: () => httpClient.delete<void>('/api/v1/ai/memories'),
  getSemantic: (eventId: number) =>
    httpClient.get<SemanticResult>(`/api/v1/events/${eventId}/semantic`),
  reparse: (eventId: number) => httpClient.post<void>(`/api/v1/events/${eventId}/semantic/reparse`),
  getCapabilities: () => httpClient.get<AiCapabilities>('/api/v1/ai/capabilities'),

  async sendMessage(id: string, content: string, onEvent: (event: StreamEvent) => void) {
    const token = useAuthStore().user?.access_token
    if (!token) throw new Error('会话已失效，请重新登录。')
    const response = await fetch(apiUrl(`/api/v1/ai/conversations/${id}/messages`), {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
        Accept: 'text/event-stream',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ content }),
    })
    if (!response.ok || !response.body) throw new Error(`AI 请求失败 (${response.status})`)
    const reader = response.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''
    while (true) {
      const { value, done } = await reader.read()
      buffer += decoder.decode(value, { stream: !done })
      const blocks = buffer.split('\n\n')
      buffer = blocks.pop() ?? ''
      for (const block of blocks) {
        let type = ''
        let data = ''
        for (const line of block.split('\n')) {
          if (line.startsWith('event:')) type = line.slice(6).trim()
          if (line.startsWith('data:')) data += line.slice(5).trim()
        }
        if (type && data) onEvent({ type, data: JSON.parse(data) } as StreamEvent)
      }
      if (done) break
    }
  },
}
