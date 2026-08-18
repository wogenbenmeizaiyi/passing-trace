import { UserManager, WebStorageStateStore, type UserManagerSettings } from 'oidc-client-ts'

const authority = (import.meta.env.VITE_IDENTITY_AUTHORITY ?? 'https://localhost:56228').replace(
  /\/$/,
  '',
)

const settings: UserManagerSettings = {
  authority,
  client_id: import.meta.env.VITE_OIDC_CLIENT_ID ?? 'passingtrace-web',
  redirect_uri: `${window.location.origin}/auth/callback`,
  // OpenIddict 精确匹配回调 URI；使用专用路径，避免 origin 末尾斜杠差异导致 400。
  post_logout_redirect_uri: `${window.location.origin}/auth/logout-callback`,
  response_type: 'code',
  scope: 'openid profile offline_access passingtrace.api',
  loadUserInfo: false,
  automaticSilentRenew: true,
  monitorSession: false,
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
}

export const oidc = new UserManager(settings)
export const identityAuthority = authority
