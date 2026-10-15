<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { socialApi } from '@/api/social'
const me = ref<Awaited<ReturnType<typeof socialApi.me>> | null>(null)
const error = ref('')
onMounted(async () => {
  try {
    me.value = await socialApi.me()
  } catch {
    error.value = '好友码暂时无法加载。'
  }
})
</script>
<template>
  <details class="friend-code">
    <summary>我的好友码</summary>
    <template v-if="me"
      ><img :src="me.qrDataUrl" alt="添加我为好友的二维码" width="180" height="180" />
      <p>{{ me.profile.nickname }}</p>
      <p class="code">{{ me.profile.friendCode }}</p>
      <small>让朋友在“消息”中输入好友码，或用星期八扫码添加。</small></template
    >
    <p v-else>{{ error || '正在加载…' }}</p>
  </details>
</template>
<style scoped>
.friend-code {
  padding: 1rem;
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  background: var(--surface);
  margin-block: 1rem;
}
summary {
  cursor: pointer;
  font-weight: 600;
  padding: 0.5rem 0;
}
img {
  display: block;
  margin: 1rem auto;
  border-radius: 0.5rem;
}
p,
small {
  display: block;
  text-align: center;
}
.code {
  user-select: all;
  letter-spacing: 0.15em;
  overflow-wrap: anywhere;
}
</style>
