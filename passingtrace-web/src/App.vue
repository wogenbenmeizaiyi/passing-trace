<script setup lang="ts">
import { onMounted, onUnmounted, watch } from 'vue'
import { useSocialStore } from '@/stores/social'
import { RouterView } from 'vue-router'

import { useAuthStore } from '@/stores/auth'
import { useProfileStore } from '@/stores/profile'

const auth = useAuthStore()
const account = useProfileStore()
const social = useSocialStore()
watch(
  () => auth.user?.profile.sub,
  (value) => {
    if (value) void social.start()
    else social.stop()
  },
  { immediate: true },
)
function refreshProfile() {
  if (document.visibilityState === 'visible') void account.refresh().catch(() => {})
}

onMounted(() => {
  void auth.restore()
  document.addEventListener('visibilitychange', refreshProfile)
  window.addEventListener('focus', refreshProfile)
})
onUnmounted(() => {
  social.stop()
  document.removeEventListener('visibilitychange', refreshProfile)
  window.removeEventListener('focus', refreshProfile)
})
</script>

<template>
  <RouterView v-slot="{ Component }">
    <!-- Keep only the current chat workspace, never every conversation or another user's state. -->
    <KeepAlive
      :key="auth.user?.profile.sub ?? 'signed-out'"
      include="AssistantView,MessagesView"
      :max="2"
    >
      <component :is="Component" />
    </KeepAlive>
  </RouterView>
</template>
