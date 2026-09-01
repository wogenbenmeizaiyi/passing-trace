interface AndroidUpdateResponse {
  updateAvailable: boolean
  downloadUrl: string | null
}

export async function getLatestAndroidDownloadUrl(
  eventsApiBase: string,
  signal?: AbortSignal,
): Promise<string> {
  const base = eventsApiBase.replace(/\/$/, '')
  const endpoint = `${base}/api/v1/app-updates/android/latest?currentVersionCode=0`
  let response: Response
  try {
    response = await fetch(endpoint, {
      headers: { Accept: 'application/json' },
      signal,
    })
  } catch (reason) {
    if (reason instanceof Error && reason.name === 'AbortError') throw reason
    throw new Error('无法连接下载服务，请检查网络后重试。')
  }

  if (!response.ok) {
    if (response.status === 404) {
      throw new Error('当前暂无可下载的 Android 安装包，请稍后再试。')
    }
    throw new Error(`下载服务暂时不可用（${response.status}），请稍后重试。`)
  }

  let update: AndroidUpdateResponse
  try {
    update = (await response.json()) as AndroidUpdateResponse
  } catch {
    throw new Error('下载信息暂时无法读取，请稍后重试。')
  }
  if (!update.updateAvailable || !update.downloadUrl) {
    throw new Error('当前暂无可下载的 Android 安装包，请稍后再试。')
  }

  return update.downloadUrl
}
