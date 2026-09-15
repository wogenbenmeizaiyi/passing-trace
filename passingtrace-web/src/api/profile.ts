import { HttpError, httpClient } from './http-client'

export interface AccountProfile {
  username: string
  nickname: string
  bio: string
  createdAt: string
  version: string
  hasAvatar: boolean
}

export const profileApi = {
  get: () =>
    httpClient.get<AccountProfile>('/api/v1/account/profile', {
      service: 'identity',
      signal: AbortSignal.timeout(45_000),
    }),
  avatar: () =>
    httpClient.blob('/api/v1/account/avatar', {
      service: 'identity',
      signal: AbortSignal.timeout(45_000),
    }),
  save: (
    nickname: string,
    bio: string,
    version: string,
    avatar: Blob | null,
    removeAvatar: boolean,
  ) => {
    const body = new FormData()
    body.set('nickname', nickname.trim())
    body.set('bio', bio.trim())
    body.set('version', version)
    body.set('removeAvatar', String(removeAvatar))
    if (avatar) body.set('avatar', avatar, 'avatar.png')
    return httpClient.put<AccountProfile>('/api/v1/account/profile', {
      service: 'identity',
      body,
      signal: AbortSignal.timeout(45_000),
    })
  },
}

export function profileError(error: unknown): string {
  if (error instanceof HttpError) {
    if (error.status === 409) return '资料已在另一处更新。请重新加载后再修改，本次修改尚未保存。'
    if (error.status === 401 || error.status === 403) return '登录状态已失效，请重新登录。'
    if (error.status === 413) return '头像图片太大，请选择不超过 5MB 的图片。'
    if (error.status === 429) return '保存过于频繁，请稍后再试。'
    if (error.status === 400) return '请检查昵称、简介和头像格式后重试。'
  }
  return '暂时无法连接账号服务，请稍后重试。已填写的内容会保留。'
}
