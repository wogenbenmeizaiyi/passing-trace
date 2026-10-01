import { afterEach, describe, expect, it, vi } from 'vitest'
import { getAssistantLocation, isFreshLocation } from '@/utils/assistant-location'

afterEach(() => vi.unstubAllGlobals())

function mockGeolocation(code?: number, timestamp = Date.now()) {
  vi.stubGlobal('isSecureContext', true)
  const getCurrentPosition = vi.fn<Geolocation['getCurrentPosition']>(
    (success: PositionCallback, failure?: PositionErrorCallback | null) => {
      if (code) failure?.({ code } as GeolocationPositionError)
      else
        success({
          coords: { latitude: 30, longitude: 120, accuracy: 25 },
          timestamp,
        } as GeolocationPosition)
    },
  )
  vi.stubGlobal('navigator', { geolocation: { getCurrentPosition } })
  return getCurrentPosition
}

describe('单次设备定位', () => {
  it('读取设备时间、精度与 WGS84 坐标，禁止读取缓存定位', async () => {
    const get = mockGeolocation()
    const result = await getAssistantLocation()
    expect(result).toMatchObject({
      latitude: 30,
      longitude: 120,
      accuracyMeters: 25,
      coordinateSystem: 'WGS84',
    })
    expect(isFreshLocation(result)).toBe(true)
    expect(get).toHaveBeenCalledWith(expect.any(Function), expect.any(Function), {
      enableHighAccuracy: true,
      maximumAge: 0,
      timeout: 10_000,
    })
  })
  it.each([
    [1, '未获得定位权限'],
    [2, '检查系统定位'],
    [3, '定位超时'],
  ])('定位错误 %s 可供用户恢复', async (code, message) => {
    mockGeolocation(code as number)
    await expect(getAssistantLocation()).rejects.toThrow(message as string)
  })
  it('不安全地址和不支持定位的浏览器不会请求位置', async () => {
    const get = mockGeolocation()
    vi.stubGlobal('isSecureContext', false)
    await expect(getAssistantLocation()).rejects.toThrow('HTTPS 或 localhost')
    expect(get).not.toHaveBeenCalled()
    vi.stubGlobal('isSecureContext', true)
    vi.stubGlobal('navigator', {})
    await expect(getAssistantLocation()).rejects.toThrow('不支持定位')
  })
  it.each([Date.now() - 6 * 60_000, Date.now() + 2 * 60_000, NaN])(
    '丢弃无效或过期采集时间',
    async (timestamp) => {
      mockGeolocation(undefined, timestamp)
      await expect(getAssistantLocation()).rejects.toThrow('无效')
    },
  )
})
