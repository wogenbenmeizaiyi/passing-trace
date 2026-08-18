// 浏览器侧生成 UUID v4（用于 Idempotency-Key）。优先使用 `crypto.randomUUID`，
// 在不支持的旧环境下降级到 `crypto.getRandomValues`。
export function randomUuid(): string {
  const c = typeof globalThis !== 'undefined' ? globalThis.crypto : undefined
  if (c && typeof c.randomUUID === 'function') {
    return c.randomUUID()
  }
  if (c && typeof c.getRandomValues === 'function') {
    const bytes = new Uint8Array(16)
    c.getRandomValues(bytes)
    // RFC 4122 v4
    bytes[6] = (bytes[6]! & 0x0f) | 0x40
    bytes[8] = (bytes[8]! & 0x3f) | 0x80
    const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
  }
  // 极简兜底：仅供本地调试；生产应使用现代浏览器。
  return `pt-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
}
