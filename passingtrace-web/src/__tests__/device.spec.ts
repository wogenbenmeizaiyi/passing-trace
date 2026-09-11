import { describe, expect, it } from 'vitest'

import { defaultWebEntry, isMobileWebClient, type WebDeviceSignals } from '@/utils/device'

function signals(overrides: Partial<WebDeviceSignals> = {}): WebDeviceSignals {
  return {
    userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',
    viewportWidth: 1440,
    coarsePointer: false,
    ...overrides,
  }
}

describe('Web 设备入口', () => {
  it('手机浏览器默认进入产品介绍页', () => {
    const phone = signals({
      userAgent:
        'Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 Chrome/140 Mobile Safari/537.36',
      viewportWidth: 412,
      coarsePointer: true,
    })

    expect(isMobileWebClient(phone)).toBe(true)
    expect(defaultWebEntry(phone)).toBe('/product')
  })

  it('桌面和大屏平板默认进入应用主界面', () => {
    expect(defaultWebEntry(signals())).toBe('/events')
    expect(
      defaultWebEntry(
        signals({
          userAgent: 'Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X)',
          viewportWidth: 1024,
          coarsePointer: true,
        }),
      ),
    ).toBe('/events')
  })

  it('没有典型手机标识的小屏触控设备仍进入产品介绍页', () => {
    expect(defaultWebEntry(signals({ viewportWidth: 600, coarsePointer: true }))).toBe('/product')
  })
})
