<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { httpClient } from '@/api/http-client'
import type { SharedMedia } from '@/api/social'
const props = defineProps<{ media: SharedMedia; endpoint: string }>()
const url = ref(''),
  error = ref(''),
  busy = ref(false)
let disposed = false
async function load() {
  busy.value = true
  error.value = ''
  try {
    const blob = await httpClient.blob(`${props.endpoint}/media/${props.media.id}`)
    if (!disposed) url.value = URL.createObjectURL(blob)
  } catch {
    error.value = '附件已不可查看，或连接暂时中断。'
  } finally {
    busy.value = false
  }
}
onBeforeUnmount(() => {
  disposed = true
  if (url.value) URL.revokeObjectURL(url.value)
})
</script>
<template>
  <div class="attachment">
    <button v-if="!url" class="button button-secondary" :disabled="busy" @click="load">
      {{ busy ? '加载中…' : `查看附件 · ${media.name}` }}</button
    ><template v-else
      ><img v-if="media.mimeType.startsWith('image/')" :src="url" :alt="media.name" /><video
        v-else-if="media.mimeType.startsWith('video/')"
        :src="url"
        controls
      /><a v-else :href="url" :download="media.name">下载 {{ media.name }}</a></template
    >
    <p v-if="error" role="alert">{{ error }}</p>
  </div>
</template>
<style scoped>
.attachment {
  margin-block: 1rem;
}
img,
video {
  max-width: 100%;
  max-height: 60dvh;
  object-fit: contain;
  border-radius: 1rem;
}
[role='alert'] {
  color: var(--danger);
}
</style>
