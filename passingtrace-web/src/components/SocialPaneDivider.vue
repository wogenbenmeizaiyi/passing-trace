<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
const props = defineProps<{ storageKey: string }>()
const root = ref<HTMLElement>()
const handle = ref<HTMLElement>()
const width = ref(300)
const dragging = ref(false)
let origin = 0,
  startWidth = 300
function resize(value: number, persist = true) {
  width.value = Math.max(240, Math.min(440, Math.round(value)))
  root.value?.parentElement?.style.setProperty('--social-list-width', `${width.value}px`)
  if (persist) {
    try {
      localStorage.setItem(props.storageKey, String(width.value))
    } catch {
      /* Optional preference. */
    }
  }
}
function restore() {
  let saved = 300
  try {
    const value = Number(localStorage.getItem(props.storageKey))
    if (Number.isFinite(value) && value >= 240 && value <= 440) saved = value
  } catch {
    /* Optional preference. */
  }
  resize(saved, false)
}
function start(event: PointerEvent) {
  if (event.button !== 0 || (event.target as HTMLElement).closest('button')) return
  event.preventDefault()
  origin = event.clientX
  startWidth = root.value?.previousElementSibling?.getBoundingClientRect().width || width.value
  dragging.value = true
  handle.value?.setPointerCapture(event.pointerId)
  handle.value?.focus()
}
function move(event: PointerEvent) {
  if (dragging.value) resize(startWidth + event.clientX - origin, false)
}
function finish() {
  if (dragging.value) resize(width.value)
  dragging.value = false
}
function key(event: KeyboardEvent) {
  const next =
    event.key === 'ArrowLeft'
      ? width.value - 20
      : event.key === 'ArrowRight'
        ? width.value + 20
        : event.key === 'Home'
          ? 240
          : event.key === 'End'
            ? 440
            : null
  if (next !== null) {
    event.preventDefault()
    resize(next)
  }
}
onMounted(restore)
watch(() => props.storageKey, restore)
</script>
<template>
  <div ref="root" class="pane-divider" :class="{ dragging }">
    <div
      ref="handle"
      class="resize-handle"
      role="separator"
      tabindex="0"
      aria-label="调整列表宽度"
      aria-orientation="vertical"
      :aria-valuenow="width"
      :aria-valuemin="240"
      :aria-valuemax="440"
      title="拖动调整宽度，或使用左右方向键；双击恢复默认"
      @pointerdown="start"
      @pointermove="move"
      @pointerup="finish"
      @pointercancel="finish"
      @lostpointercapture="finish"
      @keydown="key"
      @dblclick="resize(300)"
    >
      <span class="divider-grip" />
    </div>
    <div class="divider-controls">
      <button type="button" aria-label="缩窄列表" title="缩窄列表" @click.stop="resize(width - 40)">
        −
      </button>
      <button type="button" aria-label="加宽列表" title="加宽列表" @click.stop="resize(width + 40)">
        +
      </button>
    </div>
  </div>
</template>
<style scoped>
.pane-divider {
  position: relative;
  z-index: 3;
  cursor: col-resize;
  touch-action: none;
  background: var(--surface-soft);
  border-inline: 1px solid var(--line);
  outline-offset: -2px;
}
.resize-handle {
  height: 100%;
  outline-offset: -2px;
}
.divider-grip {
  position: absolute;
  top: 50%;
  left: 2px;
  height: 28px;
  width: 2px;
  border-radius: 2px;
  background: var(--line-strong, var(--ink-secondary));
  opacity: 0.45;
}
.pane-divider:hover,
.pane-divider:focus-within,
.pane-divider.dragging {
  background: var(--primary-soft);
}
.resize-handle:focus-visible {
  outline: 2px solid var(--primary);
}
.divider-controls {
  position: absolute;
  top: calc(50% + 28px);
  left: -12px;
  display: none;
  padding: 2px;
  background: var(--surface);
  border: 1px solid var(--line);
  border-radius: 6px;
}
.pane-divider:hover .divider-controls,
.pane-divider:focus-within .divider-controls {
  display: grid;
}
.divider-controls button {
  padding: 0;
  width: 28px;
  height: 28px;
  border: 0;
  background: transparent;
  color: var(--ink);
  cursor: pointer;
  font-size: 18px;
}
.divider-controls button:hover {
  background: var(--primary-soft);
}
@media (max-width: 760px) {
  .pane-divider {
    display: none;
  }
}
</style>
