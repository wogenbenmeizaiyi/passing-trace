// 与 Events API 的时间约定对齐：
//   - `happenedAt` / `plannedAt` 为 ISO 8601 字符串（带偏移，例如 `2026-08-18T19:30:00+09:00`）。
//   - `timezone` 为 IANA 时区名（例如 `Asia/Tokyo`），与表单里"用户选定的时区"一致。
// 表单使用 `<input type="datetime-local">`，值是 `YYYY-MM-DDTHH:mm` 这样的"墙上时间"。
// 我们把它当作"用户在该时区的本地时间"来编码，附加该时区在那一刻的偏移后发送。

const DATE_TIME_REGEX = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2})?$/

/** 用户浏览器当前时区，作为表单默认值。 */
export function defaultTimezone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
  } catch {
    return 'UTC'
  }
}

/** 在指定时区下，给定一个"墙上时间" Date，输出对应偏移字符串，例如 `+09:00` / `-05:00` / `+00:00`。 */
export function offsetForTimezone(date: Date, timezone: string): string {
  try {
    const parts = new Intl.DateTimeFormat('en-US', {
      timeZone: timezone,
      timeZoneName: 'longOffset',
    }).formatToParts(date)
    const raw = parts.find((p) => p.type === 'timeZoneName')?.value ?? 'GMT'
    // 形如 "GMT+09:00" / "GMT" / "GMT-05:00"；剥掉前缀 GMT 留偏移部分。
    const offset = raw.replace(/^GMT/, '')
    return offset || '+00:00'
  } catch {
    return '+00:00'
  }
}

interface WallClockParts {
  year: number
  month: number
  day: number
  hour: number
  minute: number
}

function parseWallClock(value: string): WallClockParts | null {
  if (!DATE_TIME_REGEX.test(value)) return null
  const [date, time] = value.split('T')
  const [y, m, d] = date!.split('-').map(Number)
  const [h, min] = (time ?? '00:00').split(':').map(Number)
  if ([y, m, d, h, min].some((n) => Number.isNaN(n))) return null
  return { year: y!, month: m!, day: d!, hour: h!, minute: min! }
}

/**
 * 把 datetime-local 的墙上时间（不带时区）+ 用户选定的 IANA 时区，
 * 转成带偏移的 ISO 8601 字符串发送给后端。
 *
 * 偏移是该时区在该时刻的实际偏移（含夏令时）。
 */
export function toIsoWithOffset(wallClock: string, timezone: string): string | null {
  const parts = parseWallClock(wallClock)
  if (!parts) return null
  // 先以"UTC 视角"放一个假想 instant，再让它经过时区返回得到那一刻的偏移。
  const probe = new Date(
    Date.UTC(parts.year, parts.month - 1, parts.day, parts.hour, parts.minute, 0),
  )
  const offset = offsetForTimezone(probe, timezone)
  const hh = String(parts.hour).padStart(2, '0')
  const mm = String(parts.minute).padStart(2, '0')
  const date = `${parts.year}-${String(parts.month).padStart(2, '0')}-${String(parts.day).padStart(2, '0')}`
  return `${date}T${hh}:${mm}:00${offset}`
}

/**
 * 把后端返回的 ISO 8601 字符串转换为 `datetime-local` 输入框值（墙上时间）。
 * 显示用的是浏览器的本地时区，区别于事件本身登记的 `timezone`。
 */
export function toDatetimeLocal(iso: string | null | undefined): string {
  if (!iso) return ''
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  // 用本地时区输出 YYYY-MM-DDTHH:mm，不带秒/偏移。
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** 友好展示：按浏览器本地时区显示日期 + 时间。空值返回 `—`。 */
export function formatLocal(iso: string | null | undefined, fallback = '—'): string {
  if (!iso) return fallback
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return fallback
  return d.toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

/** 仅显示日期。 */
export function formatLocalDate(iso: string | null | undefined, fallback = '—'): string {
  if (!iso) return fallback
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return fallback
  return d.toLocaleDateString('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit' })
}
