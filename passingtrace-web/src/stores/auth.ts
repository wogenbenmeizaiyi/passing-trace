import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import type { User } from 'oidc-client-ts'

import { oidc } from '@/auth/oidc'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<User | null>(null)
  const busy = ref(false)
  const error = ref<string | null>(null)

  const isAuthenticated = computed(() => Boolean(user.value && !user.value.expired))
  const username = computed(() =>
    String(user.value?.profile.preferred_username ?? user.value?.profile.name ?? ''),
  )
  const expiresAt = computed(() =>
    user.value?.expires_at ? new Date(user.value.expires_at * 1000) : null,
  )

  async function restore() {
    user.value = await oidc.getUser()
  }

  async function login(destination = '/') {
    busy.value = true
    error.value = null
    try {
      await oidc.signinRedirect({ state: { destination } })
    } catch (reason) {
      error.value = describe(reason, '无法跳转到登录服务。')
      busy.value = false
    }
  }

  async function completeLogin() {
    busy.value = true
    error.value = null
    try {
      user.value = await oidc.signinRedirectCallback()
      return String((user.value.state as { destination?: string } | undefined)?.destination ?? '/')
    } catch (reason) {
      error.value = describe(reason, '登录回调校验失败，请重新登录。')
      throw reason
    } finally {
      busy.value = false
    }
  }

  async function logout() {
    busy.value = true
    error.value = null
    try {
      if (user.value?.id_token) {
        await oidc.signoutRedirect()
      } else {
        await oidc.removeUser()
        user.value = null
      }
    } catch (reason) {
      error.value = describe(reason, '退出登录失败。')
      busy.value = false
    }
  }

  async function completeLogout() {
    busy.value = true
    error.value = null
    try {
      await oidc.signoutRedirectCallback()
      await oidc.removeUser()
      user.value = null
    } catch (reason) {
      error.value = describe(reason, '退出回调校验失败，本地会话已清除。')
      await oidc.removeUser()
      user.value = null
      throw reason
    } finally {
      busy.value = false
    }
  }

  function clearError() {
    error.value = null
  }

  return {
    user,
    busy,
    error,
    isAuthenticated,
    username,
    expiresAt,
    restore,
    login,
    completeLogin,
    completeLogout,
    logout,
    clearError,
  }
})

function describe(reason: unknown, fallback: string) {
  return reason instanceof Error && reason.message ? reason.message : fallback
}
