<script setup lang="ts">
import { onUnmounted, ref, watch } from 'vue'
import AccountAvatar from './AccountAvatar.vue'
import { httpClient } from '@/api/http-client'
const props = defineProps<{ id: string; hasAvatar: boolean; size?: number }>()
const src = ref('')
let request = 0
watch(
  () => [props.id, props.hasAvatar],
  async () => {
    const current = ++request
    if (src.value) URL.revokeObjectURL(src.value)
    src.value = ''
    if (!props.hasAvatar) return
    try {
      const blob = await httpClient.blob(`/api/v1/people/${props.id}/avatar`, {
        service: 'identity',
      })
      if (current === request) src.value = URL.createObjectURL(blob)
    } catch {
      /* Default avatar remains available. */
    }
  },
  { immediate: true },
)
onUnmounted(() => {
  request++
  if (src.value) URL.revokeObjectURL(src.value)
})
</script>
<template><AccountAvatar :src="src" :size="size" /></template>
