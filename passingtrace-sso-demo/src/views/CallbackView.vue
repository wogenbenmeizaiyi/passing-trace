<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

import { oidc } from '@/auth/oidc'

const route = useRoute()
const router = useRouter()
const error = ref('')

onMounted(async () => {
  try {
    if (route.name === 'logout-callback') {
      await oidc.signoutRedirectCallback()
      await oidc.removeUser()
      await router.replace('/?logout=success')
      return
    }

    const user = await oidc.signinRedirectCallback()
    const startedAt = Number((user.state as { startedAt?: number } | undefined)?.startedAt ?? 0)
    const elapsed = startedAt ? Date.now() - startedAt : 0
    await router.replace(`/?sso=success&elapsed=${elapsed}`)
  } catch (reason) {
    const message = reason instanceof Error ? reason.message : 'OIDC 回调处理失败。'
    error.value = message.includes('login_required')
      ? 'Identity 没有检测到共享登录 Cookie。请先在 5173 主站登录，再回来验证。'
      : message
  }
})
</script>

<template>
  <main class="callback-shell">
    <section class="callback-card" aria-live="polite">
      <div v-if="!error" class="spinner" aria-hidden="true"></div>
      <div v-else class="error-mark" aria-hidden="true">!</div>
      <p class="eyebrow">CLIENT B · CALLBACK</p>
      <h1>{{ error ? '回调验证失败' : '正在验证授权码与 PKCE' }}</h1>
      <p>{{ error || '这一步完成后，本站会得到属于自己的 Token。' }}</p>
      <RouterLink v-if="error" to="/" class="button button-dark">返回验证站</RouterLink>
    </section>
  </main>
</template>
