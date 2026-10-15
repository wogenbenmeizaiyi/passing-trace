import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { useAuthStore } from './auth'
import { profileApi, type AccountProfile } from '@/api/profile'

export const useProfileStore = defineStore('profile', () => {
  const auth = useAuthStore()
  const profile = ref<AccountProfile | null>(null)
  const avatarUrl = ref('')
  const nickname = computed(() => profile.value?.nickname || auth.username || '我的星期八')
  let generation = 0
  let lastFetch = 0
  let avatarVersion = ''
  let saving: number | null = null
  function clearAvatar() {
    if (avatarUrl.value) URL.revokeObjectURL(avatarUrl.value)
    avatarUrl.value = ''
    avatarVersion = ''
  }
  async function accept(value: AccountProfile, ticket: number) {
    if (ticket !== generation) return
    profile.value = value
    if (!value.hasAvatar) {
      clearAvatar()
      return
    }
    if (avatarVersion === value.version && avatarUrl.value) return
    try {
      const blob = await profileApi.avatar()
      if (ticket !== generation) return
      clearAvatar()
      avatarUrl.value = URL.createObjectURL(blob)
      avatarVersion = value.version
    } catch {
      if (ticket === generation) clearAvatar()
    }
  }
  async function refresh(force = false) {
    if (saving !== null || !auth.isAuthenticated || (!force && Date.now() - lastFetch < 5000))
      return
    lastFetch = Date.now()
    const ticket = ++generation
    const value = await profileApi.get()
    await accept(value, ticket)
  }
  async function save(
    nickname: string,
    bio: string,
    version: string,
    avatar: Blob | null,
    remove: boolean,
  ) {
    const ticket = ++generation
    saving = ticket
    try {
      const value = await profileApi.save(nickname, bio, version, avatar, remove)
      await accept(value, ticket)
      return value
    } finally {
      if (saving === ticket) saving = null
    }
  }
  watch(
    () => auth.user?.profile.sub,
    () => {
      generation++
      saving = null
      lastFetch = 0
      profile.value = null
      clearAvatar()
      if (auth.isAuthenticated) void refresh().catch(() => {})
    },
    { immediate: true },
  )
  return { profile, avatarUrl, nickname, refresh, save }
})
