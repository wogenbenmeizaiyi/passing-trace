import { afterEach, describe, expect, it, vi } from 'vitest'

import { getLatestAndroidDownloadUrl } from '@/api/app-updates'

describe('Android 官网下载', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('使用网页端版本 0 获取短效下载地址', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          updateAvailable: true,
          downloadUrl: 'https://passingtrace.cn-nb1.rains3.com/signed.apk',
        }),
        { status: 200, headers: { 'content-type': 'application/json' } },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await getLatestAndroidDownloadUrl('https://passingtrace.com/')

    expect(fetchMock).toHaveBeenCalledWith(
      'https://passingtrace.com/api/v1/app-updates/android/latest?currentVersionCode=0',
      expect.objectContaining({ signal: undefined }),
    )
    expect(result).toBe('https://passingtrace.cn-nb1.rains3.com/signed.apk')
  })

  it('清单不存在时给出可恢复提示', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 404 })),
    )

    await expect(getLatestAndroidDownloadUrl('')).rejects.toThrow(
      '当前暂无可下载的 Android 安装包，请稍后再试。',
    )
  })

  it('网络失败时不暴露底层英文异常', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockRejectedValue(new TypeError('Failed to fetch')),
    )

    await expect(getLatestAndroidDownloadUrl('')).rejects.toThrow(
      '无法连接下载服务，请检查网络后重试。',
    )
  })
})
