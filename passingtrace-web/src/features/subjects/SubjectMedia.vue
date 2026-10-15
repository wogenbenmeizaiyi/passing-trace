<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref, watch } from 'vue'
import { mediaApi } from '@/api/media'
import type { ModelViewerElement } from '@google/model-viewer'
const props = defineProps<{ id: string; title?: string }>()
const container = ref<HTMLElement | null>(null),
  started = ref(false)
let observer: IntersectionObserver | undefined
const url = ref(''),
  type = ref(''),
  error = ref(''),
  busy = ref(false),
  modelLoaded = ref(false),
  model = ref<ModelViewerElement | null>(null)
let generation = 0
function release() {
  if (url.value) URL.revokeObjectURL(url.value)
  url.value = ''
}
async function load() {
  const ticket = ++generation
  busy.value = true
  error.value = ''
  modelLoaded.value = false
  release()
  let acquiredUrl: string | undefined
  try {
    const result = await mediaApi.access(props.id)
    acquiredUrl = result.url
    if (ticket !== generation) return
    type.value = result.contentType ?? ''
    if (type.value === 'model/gltf-binary') {
      const { ModelViewerElement: Viewer } = await import('@google/model-viewer')
      const base = new URL(`${import.meta.env.BASE_URL}model-decoders/`, location.origin).href
      Viewer.dracoDecoderLocation = base + 'draco/'
      Viewer.ktx2TranscoderLocation = base + 'basis/'
      Viewer.meshoptDecoderLocation = base + 'meshopt_decoder.js'
    }
    if (ticket !== generation) return
    url.value = result.url
    acquiredUrl = undefined
  } catch (e) {
    if (ticket === generation) error.value = e instanceof Error ? e.message : '媒体加载失败'
  } finally {
    if (acquiredUrl) URL.revokeObjectURL(acquiredUrl)
    if (ticket === generation) busy.value = false
  }
}
watch(
  () => props.id,
  () => {
    if (started.value) void load()
  },
)
function start() {
  if (started.value) return
  started.value = true
  observer?.disconnect()
  void load()
}
onMounted(() => {
  if (typeof IntersectionObserver === 'undefined') start()
  else {
    observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((x) => x.isIntersecting)) start()
      },
      { rootMargin: '200px' },
    )
    if (container.value) observer.observe(container.value)
  }
})
onBeforeUnmount(() => {
  observer?.disconnect()
  generation++
  release()
})
function reset() {
  if (model.value) {
    model.value.cameraOrbit = '0deg 75deg auto'
    model.value.cameraTarget = 'auto auto auto'
    model.value.fieldOfView = 'auto'
    model.value.jumpCameraToGoal()
  }
}
</script>
<template>
  <div ref="container" class="subject-panel">
    <button v-if="!started" class="button" @click="start">查看附件</button>
    <p v-if="busy" role="status">正在加载媒体…</p>
    <p v-if="error" role="alert">{{ error }} <button class="button" @click="load">重试</button></p>
    <template v-if="url && !error"
      ><img
        v-if="type.startsWith('image/')"
        class="subject-media"
        :src="url"
        :alt="title || '人物照片'"
        @error="error = '图片加载失败，请重试'"
      /><video
        v-else-if="type.startsWith('video/')"
        class="subject-media"
        :src="url"
        controls
        preload="metadata"
        @error="error = '视频加载失败，请重试'"
      /><template v-else-if="type === 'model/gltf-binary'"
        ><model-viewer
          ref="model"
          :src="url"
          :alt="title || '人物三维模型'"
          camera-controls
          touch-action="pan-y"
          loading="lazy"
          @load="modelLoaded = true"
          @error="error = '三维模型加载失败，请重试'"
        ></model-viewer>
        <p v-if="!modelLoaded" role="status">正在加载三维模型…</p>
        <button class="button" @click="reset">重置视角</button></template
      ><a v-else :href="url" download>下载附件</a></template
    >
  </div>
</template>
<style scoped>
model-viewer {
  display: block;
  width: 100%;
  height: 360px;
  max-height: 60vh;
  background: var(--surface-soft);
  border-radius: 14px;
}
.subject-media {
  width: 100%;
  object-fit: contain;
}
</style>
