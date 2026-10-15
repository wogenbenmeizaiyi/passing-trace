/** A conversation reference is an opaque ID, never an arbitrary return URL. */
export function conversationIdFromQuery(value: unknown): string | null {
  return typeof value === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
    ? value
    : null
}

export function recordFromConversation(eventId: number, conversationId?: string | null) {
  const id = conversationIdFromQuery(conversationId)
  const path = `/events/${eventId}`
  return id ? `${path}?conversation=${encodeURIComponent(id)}` : path
}

export function storylineFromConversation(storylineId: string, conversationId?: string | null) {
  const id = conversationIdFromQuery(conversationId)
  const path = `/storylines/${encodeURIComponent(storylineId)}`
  return id ? `${path}?conversation=${encodeURIComponent(id)}` : path
}
