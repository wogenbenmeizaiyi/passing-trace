<script setup lang="ts">
import { nextTick, onMounted, ref, useId, watch } from 'vue'
const props = defineProps<{ title: string; busy?: boolean }>()
const open = defineModel<boolean>({ default: false })
const dialog = ref<HTMLDialogElement | null>(null)
const titleId = useId()
async function sync() {
  await nextTick()
  if (!dialog.value) return
  if (open.value && !dialog.value.open) {
    if (typeof dialog.value.showModal === 'function') dialog.value.showModal()
    else dialog.value.setAttribute('open', '')
  } else if (!open.value && dialog.value.open) {
    if (typeof dialog.value.close === 'function') dialog.value.close()
    else dialog.value.removeAttribute('open')
  }
}
function cancel() {
  if (!props.busy) open.value = false
}
function backdrop(event: MouseEvent) {
  if (!dialog.value || event.target !== dialog.value) return
  const bounds = dialog.value.getBoundingClientRect()
  if (
    event.clientX < bounds.left ||
    event.clientX > bounds.right ||
    event.clientY < bounds.top ||
    event.clientY > bounds.bottom
  )
    cancel()
}
watch(open, sync)
onMounted(sync)
</script>
<template>
  <dialog
    ref="dialog"
    class="subject-dialog"
    :aria-labelledby="titleId"
    @cancel.prevent="cancel"
    @click="backdrop"
  >
    <header class="subject-section-heading">
      <h2 :id="titleId">{{ title }}</h2>
      <button
        type="button"
        class="button button-compact"
        aria-label="关闭弹窗"
        :disabled="busy"
        @click="cancel"
      >
        ×
      </button>
    </header>
    <div class="subject-dialog-body"><slot /></div>
    <footer v-if="$slots.footer" class="subject-dialog-footer"><slot name="footer" /></footer>
  </dialog>
</template>
