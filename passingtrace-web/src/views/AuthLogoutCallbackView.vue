<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()
const failed = ref(false)

onMounted(async () => {
  try {
    await auth.completeLogout()
    await router.replace('/')
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
      <p class="eyebrow">SECURE SIGN OUT</p>
      <h1>{{ failed ? '退出回调没有完成' : '正在安全退出' }}</h1>
      <p>{{ failed ? auth.error : '正在清理这个标签页保存的登录令牌。' }}</p>
      <RouterLink v-if="failed" class="button button-dark" to="/">返回首页</RouterLink>
    </section>
  </main>
</template>
