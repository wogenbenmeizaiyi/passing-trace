import { afterEach, describe, expect, it, vi } from 'vitest'
import { profileApi } from '@/api/profile'
vi.mock('@/auth/oidc', () => ({
  identityAuthority: 'https://identity.test',
  oidc: { signinSilent: vi.fn<() => Promise<null>>() },
}))
vi.mock('@/stores/auth', () => ({ useAuthStore: () => ({ user: { access_token: 'test-token' } }) }))
afterEach(() => vi.unstubAllGlobals())
describe('资料上传协议', () => {
  it('向 Identity 发送认证 multipart，浏览器生成 boundary，不走记录上传', async () => {
    const fetch = vi
      .fn<typeof globalThis.fetch>()
      .mockResolvedValue(new Response(JSON.stringify({ nickname: '昵称' }), { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    await profileApi.save(' 昵称 ', ' 简介 ', 'version-one', new Blob(['png']), false)
    const [url, options] = fetch.mock.calls[0]!
    expect(url).toBe('https://identity.test/api/v1/account/profile')
    const headers = new Headers(options!.headers)
    const body = options!.body as FormData
    expect(headers.get('Authorization')).toBe('Bearer test-token')
    expect(headers.has('Content-Type')).toBe(false)
    expect(body).toBeInstanceOf(FormData)
    expect(body.get('nickname')).toBe('昵称')
    expect(body.get('avatar')).toBeInstanceOf(Blob)
    expect(body.get('version')).toBe('version-one')
  })
})
