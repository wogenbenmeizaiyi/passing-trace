export interface AssistantLocation {
  latitude: number
  longitude: number
  accuracyMeters: number
  capturedAt: string
  coordinateSystem: 'WGS84' | 'GCJ02'
}

export function isFreshLocation(location: AssistantLocation, now = Date.now()): boolean {
  const captured = Date.parse(location.capturedAt)
  return Number.isFinite(captured) && captured >= now - 5 * 60_000 && captured <= now + 60_000
}

export function getAssistantLocation(): Promise<AssistantLocation> {
  if (!window.isSecureContext)
    return Promise.reject(new Error('浏览器定位需要 HTTPS 或 localhost，请使用安全地址访问。'))
  if (!navigator.geolocation)
    return Promise.reject(new Error('当前浏览器不支持定位，请在消息中提供出发地。'))
  return new Promise((resolve, reject) => {
    navigator.geolocation.getCurrentPosition(
      (position) => {
        if (!Number.isFinite(position.timestamp)) {
          reject(new Error('浏览器返回的位置无效，请重新定位。'))
          return
        }
        const location: AssistantLocation = {
          latitude: position.coords.latitude,
          longitude: position.coords.longitude,
          accuracyMeters: position.coords.accuracy,
          capturedAt: new Date(position.timestamp).toISOString(),
          coordinateSystem: 'WGS84',
        }
        if (
          !Number.isFinite(location.latitude) ||
          !Number.isFinite(location.longitude) ||
          Math.abs(location.latitude) > 90 ||
          Math.abs(location.longitude) > 180 ||
          !Number.isFinite(location.accuracyMeters) ||
          location.accuracyMeters <= 0 ||
          location.accuracyMeters > 100_000 ||
          !isFreshLocation(location)
        ) {
          reject(new Error('浏览器返回的位置无效或已过期，请重新定位。'))
          return
        }
        resolve(location)
      },
      (error) => {
        const message =
          error.code === 1
            ? '未获得定位权限，请在浏览器设置中允许定位，或在消息中提供出发地。'
            : error.code === 3
              ? '定位超时，请重试，或在消息中提供出发地。'
              : '暂时无法获取位置，请检查系统定位设置，或在消息中提供出发地。'
        reject(new Error(message))
      },
      { enableHighAccuracy: true, maximumAge: 0, timeout: 10_000 },
    )
  })
}
