<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import { RouterView } from 'vue-router'

import { useAuthStore } from '@/stores/auth'
import { useProfileStore } from '@/stores/profile'

const auth = useAuthStore()
const account = useProfileStore()
function refreshProfile() {
  if (document.visibilityState === 'visible') void account.refresh().catch(() => {})
}

onMounted(() => {
  void auth.restore()
  document.addEventListener('visibilitychange', refreshProfile)
  window.addEventListener('focus', refreshProfile)
})
onUnmounted(() => {
  document.removeEventListener('visibilitychange', refreshProfile)
  window.removeEventListener('focus', refreshProfile)
})
</script>

<template>
  <RouterView v-slot="{ Component }">
    <!-- Keep only the current chat workspace, never every conversation or another user's state. -->
    <KeepAlive :key="auth.user?.profile.sub ?? 'signed-out'" include="AssistantView" :max="1">
      <component :is="Component" />
    </KeepAlive>
  </RouterView>
</template>
