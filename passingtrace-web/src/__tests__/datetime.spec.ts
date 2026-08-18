import { describe, expect, it } from 'vitest'

import {
  defaultTimezone,
  formatLocal,
  formatLocalDate,
  offsetForTimezone,
  toDatetimeLocal,
  toIsoWithOffset,
} from '@/utils/datetime'

describe('datetime utilities', () => {
  it('defaultTimezone 返回有效的 IANA 名（兜底为 UTC）', () => {
    const tz = defaultTimezone()
    expect(typeof tz).toBe('string')
    expect(tz.length).toBeGreaterThan(0)
  })

  it('offsetForTimezone 在东京 +09:00', () => {
    // 2026-08-18 是夏令时无关的日期，东京恒为 +09:00。
    expect(offsetForTimezone(new Date('2026-08-18T12:00:00Z'), 'Asia/Tokyo')).toBe('+09:00')
  })

  it('offsetForTimezone 在纽约 冬令时 -05:00、夏令时 -04:00', () => {
    // 1 月：EST
    expect(offsetForTimezone(new Date('2026-01-15T12:00:00Z'), 'America/New_York')).toBe('-05:00')
    // 7 月：EDT
    expect(offsetForTimezone(new Date('2026-07-15T12:00:00Z'), 'America/New_York')).toBe('-04:00')
  })

  it('offsetForTimezone 非法时区回落到 +00:00', () => {
    expect(offsetForTimezone(new Date('2026-08-18T12:00:00Z'), 'Not/AZone')).toBe('+00:00')
  })

  it('toIsoWithOffset 把 datetime-local + 时区 → ISO 8601 带偏移', () => {
    const iso = toIsoWithOffset('2026-08-18T19:30', 'Asia/Tokyo')
    expect(iso).toBe('2026-08-18T19:30:00+09:00')
  })

  it('toIsoWithOffset 处理冬令时切换区', () => {
    // 纽约冬令时，墙上 09:00 → -05:00
    expect(toIsoWithOffset('2026-01-15T09:00', 'America/New_York')).toBe(
      '2026-01-15T09:00:00-05:00',
    )
  })

  it('toIsoWithOffset 对非法输入返回 null', () => {
    expect(toIsoWithOffset('', 'Asia/Tokyo')).toBeNull()
    expect(toIsoWithOffset('not-a-time', 'Asia/Tokyo')).toBeNull()
  })

  it('toDatetimeLocal 把 ISO 转回 datetime-local（按浏览器本地时区）', () => {
    const iso = '2026-08-18T10:30:00+00:00'
    const out = toDatetimeLocal(iso)
    expect(out).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/)
    // 往返：把输出按本地时区解释为 instant，应当等于原始 ISO。
    expect(new Date(out).toISOString()).toBe(new Date(iso).toISOString())
  })

  it('toDatetimeLocal 空值与非法值返回空串', () => {
    expect(toDatetimeLocal(null)).toBe('')
    expect(toDatetimeLocal(undefined)).toBe('')
    expect(toDatetimeLocal('xxx')).toBe('')
  })

  it('formatLocal 空值显示占位', () => {
    expect(formatLocal(null)).toBe('—')
    expect(formatLocal(undefined)).toBe('—')
  })

  it('formatLocalDate 空值显示占位', () => {
    expect(formatLocalDate(null)).toBe('—')
  })
})
