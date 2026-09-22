import { httpClient } from './http-client'

export interface Person {
  id: string
  nickname: string
  bio: string
  hasAvatar: boolean
  friendCode: string
}
export interface Friend {
  id: string
  person: Person
  remark: string
  label: string
  relationship: string | null
  proposedRelationship: string | null
  relationshipRequestedBy: string | null
  version: string
}
export interface FriendRequest {
  id: string
  person: Person
  direction: string
  status: string
  createdAt: string
}
export interface Page<T> {
  items: T[]
  nextCursor: string | null
}
export interface DirectConversation {
  id: string
  friendshipId: string
  person: Person
  preview: string
  unreadCount: number
  peerReadThroughId: number
  updatedAt: string
  canSend: boolean
  clearedThroughId: number
}
export interface DirectMessage {
  id: number
  conversationId: string
  senderId: string
  clientMessageId: string
  kind: string
  text: string | null
  shareId: string | null
  shareTitle: string | null
  shareAvailable: boolean
  createdAt: string
}
export interface SendMessage {
  clientMessageId: string
  kind: 'text' | 'record' | 'storyline'
  text?: string
  eventId?: number
  storylineId?: string
}
export interface SharedMedia {
  id: string
  name: string
  kind: string
  mimeType: string
}
export interface SharedRecord {
  eventId: number
  revision: number
  title: string
  content: string | null
  happenedAt: string | null
  plannedAt: string | null
  status: string
  media: SharedMedia[]
  participants: string[]
  labels?: string[] | null
  places?: Array<{
    name: string
    address: string | null
    latitude: number | null
    longitude: number | null
    coordinateSystem: string
  }> | null
  available: boolean
}
export interface SharedDocument {
  kind: string
  title: string
  description: string | null
  authorId: string
  records: SharedRecord[]
  stages: Array<{ key: string; title: string; order: number }>
  nodes: Array<{ key: string; eventId: number; stageKey: string | null; order: number }>
  edges: Array<{ source: string; target: string; type: string; label: string | null }>
  status: string
  available: boolean
}
export interface SharedView {
  id: string
  ownerId: string
  recipientId: string
  document: SharedDocument
  createdAt: string
  available: boolean
}
export interface Notification {
  id: number
  kind: string
  text: string
  target: string | null
  createdAt: string
  read: boolean
}
export interface JointRecord {
  id: number
  title: string
  authorId: string
  happenedAt: string | null
}

export const socialApi = {
  me: () =>
    httpClient.get<{ profile: Person; qrDataUrl: string }>('/api/v1/people/me', {
      service: 'identity',
    }),
  profiles: (ids: string[]) =>
    httpClient.post<Person[]>('/api/v1/people/profiles', { service: 'identity', body: ids }),
  friends: (query = '') => httpClient.get<Friend[]>('/api/v1/friends', { query: { query } }),
  requests: () => httpClient.get<FriendRequest[]>('/api/v1/friend-requests'),
  request: (code: string) => httpClient.post('/api/v1/friend-requests', { body: { code } }),
  decide: (id: string, decision: string) =>
    httpClient.post(`/api/v1/friend-requests/${id}/decision`, { body: { decision } }),
  preference: (id: string, remark: string, label: string) =>
    httpClient.put(`/api/v1/friends/${id}/preference`, { body: { remark, label } }),
  relationship: (friend: Friend, kind: string) =>
    httpClient.post(`/api/v1/friends/${friend.id}/relationship`, {
      body: { kind, version: friend.version },
    }),
  relationshipDecision: (friend: Friend, decision: string) =>
    httpClient.post(`/api/v1/friends/${friend.id}/relationship/decision`, {
      body: { decision, version: friend.version },
    }),
  remove: (id: string, block = false) =>
    httpClient.delete(`/api/v1/friends/${id}`, { query: { block: String(block) } }),
  blocks: () => httpClient.get<string[]>('/api/v1/friends/blocks'),
  unblock: (id: string) => httpClient.delete(`/api/v1/friends/blocks/${id}`),
  conversations: (cursor?: string) =>
    httpClient.get<Page<DirectConversation>>('/api/v1/conversations', {
      query: { limit: 20, cursor },
    }),
  open: (friendshipId: string) =>
    httpClient.post<{ id: string }>('/api/v1/conversations', { body: { friendshipId } }),
  conversation: (id: string) => httpClient.get<DirectConversation>(`/api/v1/conversations/${id}`),
  messages: (id: string, before?: number, after?: number) =>
    httpClient.get<Page<DirectMessage>>(`/api/v1/conversations/${id}/messages`, {
      query: { limit: 30, before, after },
    }),
  send: (id: string, body: SendMessage) =>
    httpClient.post<DirectMessage>(`/api/v1/conversations/${id}/messages`, { body }),
  read: (id: string, throughId: number) =>
    httpClient.put(`/api/v1/conversations/${id}/read`, { body: { throughId } }),
  clear: (id: string) => httpClient.delete(`/api/v1/conversations/${id}/messages`),
  shareStatuses: (id: string, ids: string[]) =>
    httpClient.post<Array<{ id: string; title: string; available: boolean }>>(
      `/api/v1/conversations/${id}/share-statuses`,
      { body: ids },
    ),
  preview: (body: SendMessage) =>
    httpClient.post<SharedDocument>('/api/v1/shares/preview', { body }),
  shared: (id: string) => httpClient.get<SharedView>(`/api/v1/shares/${id}`),
  revoke: (id: string) => httpClient.delete(`/api/v1/shares/${id}`),
  shares: (eventId?: number, storylineId?: string) =>
    httpClient.get<
      Array<{ id: string; recipientId: string; revokedAt: string | null; createdAt: string }>
    >('/api/v1/shares', { query: { eventId, storylineId } }),
  joint: (before?: number) =>
    httpClient.get<Page<JointRecord>>('/api/v1/friends/shared-records', { query: { before } }),
  jointRecord: (id: number) =>
    httpClient.get<SharedDocument>(`/api/v1/friends/shared-records/${id}`),
  detach: (id: number) => httpClient.delete(`/api/v1/friends/shared-records/${id}/participation`),
  notifications: (before?: number) =>
    httpClient.get<Notification[]>('/api/v1/notifications', { query: { before } }),
  readNotifications: (throughId: number) =>
    httpClient.put('/api/v1/notifications/read', { body: { throughId } }),
}

export function safeSocialPath(path: string | null | undefined): string | null {
  return path && /^\/(?:joint-records\/\d+|shares\/[0-9a-f-]{36})$/i.test(path) ? path : null
}
