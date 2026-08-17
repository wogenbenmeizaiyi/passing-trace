import { UserManager, WebStorageStateStore, type UserManagerSettings } from 'oidc-client-ts'

export const identityAuthority = (
  import.meta.env.VITE_IDENTITY_AUTHORITY ?? 'https://localhost:56228'
).replace(/\/$/, '')

const settings: UserManagerSettings = {
  authority: identityAuthority,
  client_id: import.meta.env.VITE_OIDC_CLIENT_ID ?? 'passingtrace-sso-demo',
  redirect_uri: `${window.location.origin}/auth/callback`,
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
export const mainWebUrl = import.meta.env.VITE_MAIN_WEB_URL ?? 'http://localhost:5173'
