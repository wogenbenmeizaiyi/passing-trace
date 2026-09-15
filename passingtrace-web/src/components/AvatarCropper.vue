<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { avatarCropRect } from '@/utils/avatar-crop'

const props = defineProps<{ file: File }>()
const emit = defineEmits<{ cancel: []; confirm: [image: Blob] }>()
const dialog = ref<HTMLDialogElement | null>(null)
const canvas = ref<HTMLCanvasElement | null>(null)
const zoom = ref(1),
  x = ref(0.5),
  y = ref(0.5)
const ready = ref(false),
  busy = ref(false),
  error = ref('')
const photo = new Image()
let url = ''
let previousFocus: HTMLElement | null = null
function draw() {
  const target = canvas.value
  if (!target || !ready.value) return
  const ctx = target.getContext('2d')
  if (!ctx) return
  const crop = avatarCropRect(photo.naturalWidth, photo.naturalHeight, zoom.value, x.value, y.value)
  ctx.clearRect(0, 0, 512, 512)
  ctx.drawImage(photo, crop.left, crop.top, crop.side, crop.side, 0, 0, 512, 512)
}
function confirm() {
  if (!ready.value || busy.value) return
  busy.value = true
  canvas.value?.toBlob((blob) => {
    busy.value = false
    if (blob) emit('confirm', blob)
    else error.value = '裁剪失败，请重新选择图片。'
  }, 'image/png')
}
watch([zoom, x, y], draw)
onMounted(() => {
  previousFocus = document.activeElement as HTMLElement | null
  dialog.value?.showModal()
  photo.onload = () => {
    if (photo.naturalWidth * photo.naturalHeight > 32_000_000) {
      error.value = '图片尺寸过大，请选择较小的图片。'
      return
    }
    ready.value = true
    draw()
  }
  photo.onerror = () => {
    error.value = '图片无法读取，请选择 JPG、PNG 或 WebP 图片。'
  }
  url = URL.createObjectURL(props.file)
  photo.src = url
})
onBeforeUnmount(() => {
  photo.onload = null
  photo.onerror = null
  URL.revokeObjectURL(url)
  previousFocus?.focus()
})
</script>

<template>
  <dialog
    ref="dialog"
    class="avatar-cropper"
    aria-labelledby="crop-title"
    @cancel.prevent="emit('cancel')"
  >
    <h2 id="crop-title">调整头像</h2>
    <p>移动和缩放图片，圆形区域是头像展示效果。</p>
    <canvas ref="canvas" width="512" height="512" aria-label="头像裁剪预览" />
    <p v-if="error" role="alert">{{ error }}</p>
    <fieldset :disabled="!ready || busy">
      <label>缩放<input v-model.number="zoom" type="range" min="1" max="3" step="0.01" /></label>
      <label>左右位置<input v-model.number="x" type="range" min="0" max="1" step="0.01" /></label>
      <label>上下位置<input v-model.number="y" type="range" min="0" max="1" step="0.01" /></label>
    </fieldset>
    <footer>
      <button class="button button-secondary" type="button" @click="emit('cancel')">取消</button
      ><button
        class="button button-primary"
        type="button"
        :disabled="!ready || busy"
        @click="confirm"
      >
        使用这张头像
      </button>
    </footer>
  </dialog>
</template>

<style scoped>
.avatar-cropper {
  width: min(92vw, 480px);
  max-height: 90dvh;
  overflow-y: auto;
  padding: 24px;
  border: 1px solid var(--line-strong);
  border-radius: 24px;
  background: var(--surface);
  color: var(--ink);
}
.avatar-cropper::backdrop {
  background: rgb(0 0 0 / 55%);
}
h2 {
  margin: 0;
}
p {
  color: var(--ink-secondary);
}
canvas {
  display: block;
  width: min(100%, 260px);
  height: auto;
  aspect-ratio: 1;
  border-radius: 50%;
  margin: 20px auto;
  background: var(--surface-soft);
}
fieldset {
  border: 0;
  padding: 0;
}
label {
  display: grid;
  grid-template-columns: 5em 1fr;
  align-items: center;
  min-height: 44px;
  gap: 12px;
}
input {
  width: 100%;
  accent-color: var(--primary);
}
footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  margin-top: 20px;
  flex-wrap: wrap;
}
</style>
