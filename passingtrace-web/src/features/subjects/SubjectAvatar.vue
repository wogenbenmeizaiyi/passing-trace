<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import type { Subject } from '@/api/subjects'
import { mediaApi } from '@/api/media'
import { useProfileStore } from '@/stores/profile'
const props = withDefaults(defineProps<{ subject: Subject; size?: number; node?: boolean }>(), {
  size: 64,
  node: false,
})
const profile = useProfileStore()
const url = ref(''),
  loaded = ref(false),
  failed = ref(false)
const src = computed(() => (props.subject.isSelf ? profile.avatarUrl : url.value))
function imageFailed() {
  failed.value = true
  loaded.value = false
}
let generation = 0
function release() {
  if (url.value) URL.revokeObjectURL(url.value)
  url.value = ''
}
watch(
  () => [props.subject.coverMediaId, props.subject.isSelf] as const,
  async ([id, self]) => {
    const ticket = ++generation
    release()
    if (!id || self) return
    try {
      const result = await mediaApi.access(id)
      if (
        ticket !== generation ||
        (result.contentType && !result.contentType.startsWith('image/'))
      ) {
        URL.revokeObjectURL(result.url)
        return
      }
      url.value = result.url
    } catch {
      /* The type icon remains visible when a private photo is unavailable. */
    }
  },
  { immediate: true },
)
watch(src, () => {
  loaded.value = false
  failed.value = false
})
onBeforeUnmount(() => {
  generation++
  release()
})
const path = computed(() => {
  if (props.subject.kind === 0) return 'M12 3a4 4 0 1 1 0 8 4 4 0 0 1 0-8ZM4 21v-2a8 8 0 0 1 16 0v2'
  if (props.subject.kind === 1)
    return 'M7 13c2-5 8-5 10 0l2 4c2 5-3 5-7 3-4 2-9 2-7-3l2-4ZM4 7a1.5 2 0 1 0 0 .1ZM9 4a1.5 2 0 1 0 0 .1ZM15 4a1.5 2 0 1 0 0 .1ZM20 7a1.5 2 0 1 0 0 .1Z'
  return (
    (
      {
        vehicle: 'M3 11l2-6h14l2 6v9h-3v-3H6v3H3v-9Zm0 0h18M6 14h2M16 14h2',
        property: 'm3 11 9-8 9 8M5 10v11h14V10M10 21v-7h4v7',
        bicycle:
          'M9 16a4 4 0 1 1-8 0 4 4 0 0 1 8 0Zm14 0a4 4 0 1 1-8 0 4 4 0 0 1 8 0ZM5 16l5-9 6 9H5M8 4h4M17 4h3l2 12',
        collectible: 'm3 9 4-6h10l4 6-9 12L3 9Zm0 0h18M7 3l5 18 5-18',
      } as Record<string, string>
    )[props.subject.itemType ?? ''] ?? 'M3 4h18v5H3V4Zm2 5v12h14V9M9 13h6'
  )
})
</script>
<template>
  <span
    class="subject-avatar"
    :class="{ 'subject-avatar--node': node, 'subject-avatar--loaded': loaded && !failed }"
    :style="node ? undefined : { width: `${size}px`, height: `${size}px` }"
    aria-hidden="true"
  >
    <svg v-if="!loaded || failed" viewBox="0 0 24 24"><path :d="path" /></svg>
    <img
      v-if="src && !failed"
      :key="src"
      :src="src"
      alt=""
      :class="{ 'is-ready': loaded }"
      @load="loaded = true"
      @error="imageFailed"
    />
  </span>
</template>
