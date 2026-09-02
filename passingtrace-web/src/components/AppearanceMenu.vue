<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

import {
  appearanceMode,
  appearancePalette,
  setAppearanceMode,
  setAppearancePalette,
  type AppearanceMode,
  type AppearancePalette,
} from '@/theme/appearance'

const open = ref(false)
const root = ref<HTMLElement | null>(null)

const modes: { key: AppearanceMode; label: string }[] = [
  { key: 'system', label: '跟随系统' },
  { key: 'light', label: '浅色' },
  { key: 'dark', label: '深色' },
]
const palettes: { key: AppearancePalette; label: string; description: string }[] = [
  { key: 'pine', label: '松间', description: '温和自然' },
  { key: 'tide', label: '潮汐', description: '清醒安静' },
  { key: 'plum', label: '暮紫', description: '柔和内敛' },
  { key: 'dune', label: '沙丘', description: '温暖沉静' },
]

function closeOnOutsideClick(event: PointerEvent) {
  if (open.value && !root.value?.contains(event.target as Node)) open.value = false
}

function closeOnEscape(event: KeyboardEvent) {
  if (event.key === 'Escape') open.value = false
}

onMounted(() => {
  document.addEventListener('pointerdown', closeOnOutsideClick)
  document.addEventListener('keydown', closeOnEscape)
})
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', closeOnOutsideClick)
  document.removeEventListener('keydown', closeOnEscape)
})
</script>

<template>
  <div ref="root" class="appearance-menu">
    <button
      class="appearance-trigger"
      type="button"
      aria-label="主题与外观"
      :aria-expanded="open"
      aria-haspopup="dialog"
      @click="open = !open"
    >
      <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
        <path
          d="M12 3a9 9 0 1 0 0 18c1.2 0 1.8-.7 1.8-1.5 0-.5-.2-.9-.2-1.3 0-1 .8-1.8 1.8-1.8h1.8c2.1 0 3.8-1.7 3.8-3.8C21 7.3 17 3 12 3Z"
        />
        <circle cx="7.7" cy="10" r=".8" />
        <circle cx="10" cy="6.8" r=".8" />
        <circle cx="14" cy="6.7" r=".8" />
        <circle cx="17" cy="9.3" r=".8" />
      </svg>
    </button>
    <section v-if="open" class="appearance-popover" role="dialog" aria-label="主题与外观">
      <header><strong>主题与外观</strong><small>只保存在这台设备</small></header>
      <fieldset>
        <legend>显示模式</legend>
        <div class="mode-options">
          <button
            v-for="mode in modes"
            :key="mode.key"
            type="button"
            :aria-pressed="appearanceMode === mode.key"
            :class="{ selected: appearanceMode === mode.key }"
            @click="setAppearanceMode(mode.key)"
          >
            {{ mode.label }}
          </button>
        </div>
      </fieldset>
      <fieldset>
        <legend>颜色主题</legend>
        <div class="palette-options">
          <button
            v-for="palette in palettes"
            :key="palette.key"
            type="button"
            :class="[`palette-${palette.key}`, { selected: appearancePalette === palette.key }]"
            :aria-pressed="appearancePalette === palette.key"
            @click="setAppearancePalette(palette.key)"
          >
            <span class="palette-swatch" aria-hidden="true"><i></i><i></i><i></i></span>
            <span
              ><strong>{{ palette.label }}</strong
              ><small>{{ palette.description }}</small></span
            >
            <svg
              v-if="appearancePalette === palette.key"
              class="ui-icon"
              viewBox="0 0 24 24"
              aria-hidden="true"
            >
              <path d="m5 12 4 4L19 6" />
            </svg>
          </button>
        </div>
      </fieldset>
    </section>
  </div>
</template>

<style scoped>
.appearance-menu {
  position: relative;
}
.appearance-trigger {
  width: 44px;
  height: 44px;
  display: grid;
  place-items: center;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  color: var(--ink-secondary);
  background: var(--surface-soft);
}
.appearance-trigger:hover {
  color: var(--primary-strong);
  border-color: var(--line-strong);
}
.appearance-popover {
  width: min(360px, calc(100vw - 32px));
  padding: 20px;
  position: absolute;
  z-index: 500;
  top: calc(100% + 10px);
  right: 0;
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  color: var(--ink);
  background: var(--surface-raised);
  box-shadow: var(--shadow-2);
}
.appearance-popover header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
}
.appearance-popover header strong {
  font-size: 18px;
}
.appearance-popover header small {
  color: var(--ink-tertiary);
  font-size: 11px;
}
fieldset {
  margin: 18px 0 0;
  padding: 0;
  border: 0;
}
legend {
  margin-bottom: 8px;
  color: var(--ink-secondary);
  font-size: 12px;
  font-weight: 700;
}
.mode-options {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 6px;
}
.mode-options button {
  min-height: 40px;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  color: var(--ink-secondary);
  background: var(--surface-soft);
  font-size: 12px;
}
.mode-options button.selected {
  color: var(--primary-strong);
  border-color: var(--primary);
  background: var(--primary-soft);
  font-weight: 700;
}
.palette-options {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
}
.palette-options > button {
  min-width: 0;
  min-height: 72px;
  padding: 10px;
  display: grid;
  grid-template-columns: 46px minmax(0, 1fr) 16px;
  align-items: center;
  gap: 8px;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  color: var(--ink);
  background: var(--surface-soft);
  text-align: left;
}
.palette-options > button.selected {
  border-color: var(--primary);
  background: var(--surface);
}
.palette-options strong,
.palette-options small {
  display: block;
}
.palette-options strong {
  font-size: 12px;
}
.palette-options small {
  margin-top: 2px;
  overflow: hidden;
  color: var(--ink-tertiary);
  font-size: 9px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.palette-options .ui-icon {
  width: 16px;
  height: 16px;
  color: var(--primary);
}
.palette-swatch {
  height: 36px;
  padding: 7px;
  display: flex;
  align-items: center;
  gap: 4px;
  border-radius: 9px;
  background: var(--swatch-soft);
}
.palette-swatch i {
  display: block;
  border-radius: 50%;
  background: var(--swatch-primary);
}
.palette-swatch i:first-child {
  width: 18px;
  height: 18px;
}
.palette-swatch i:nth-child(2) {
  width: 12px;
  height: 12px;
  background: var(--swatch-accent);
}
.palette-swatch i:last-child {
  width: 8px;
  height: 8px;
  background: var(--swatch-ink);
}
.palette-pine {
  --swatch-soft: #dcebe4;
  --swatch-primary: #2f6b57;
  --swatch-accent: #c96c47;
  --swatch-ink: #1c2520;
}
.palette-tide {
  --swatch-soft: #d8eaf0;
  --swatch-primary: #2b6678;
  --swatch-accent: #c76849;
  --swatch-ink: #17252a;
}
.palette-plum {
  --swatch-soft: #e9ddec;
  --swatch-primary: #725a7d;
  --swatch-accent: #b95863;
  --swatch-ink: #291f2c;
}
.palette-dune {
  --swatch-soft: #ece4c7;
  --swatch-primary: #77622a;
  --swatch-accent: #b85f38;
  --swatch-ink: #28241a;
}
@media (max-width: 520px) {
  .appearance-popover {
    position: fixed;
    top: 76px;
    right: 16px;
  }
}
</style>
