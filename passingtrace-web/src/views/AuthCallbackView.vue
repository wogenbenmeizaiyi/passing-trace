<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()
const failed = ref(false)

onMounted(async () => {
  try {
    const destination = await auth.completeLogin()
    await router.replace(destination)
  } catch {
    failed.value = true
  }
})
</script>

<template>
  <main class="callback-page">
    <section class="callback-card" aria-live="polite">
      <div v-if="!failed" class="spinner" aria-hidden="true"></div>
      <div v-else class="callback-mark">!</div>
      <p class="eyebrow">SECURE HANDOFF</p>
      <h1>{{ failed ? '登录没有完成' : '正在确认你的身份' }}</h1>
      <p>{{ failed ? auth.error : '正在校验授权码与 PKCE，请稍候。' }}</p>
      <RouterLink v-if="failed" class="button button-dark" to="/">返回首页</RouterLink>
    </section>
  </main>
</template>
