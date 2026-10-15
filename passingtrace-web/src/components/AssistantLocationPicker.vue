<script setup lang="ts">
import { onDeactivated, onUnmounted, ref, watch } from 'vue'
import { getAssistantLocation, type AssistantLocation } from '@/utils/assistant-location'

const props = defineProps<{ scopeKey: string | null; disabled: boolean }>()
const location = defineModel<AssistantLocation | null>({ required: true })
const emit = defineEmits<{ busy: [value: boolean] }>()
const busy = ref(false)
const error = ref('')
let request = 0

function clear() {
  ++request
  location.value = null
  error.value = ''
  busy.value = false
  emit('busy', false)
}

async function locate() {
  if (busy.value || props.disabled) return
  const current = ++request
  location.value = null
  busy.value = true
  error.value = ''
  emit('busy', true)
  try {
    const value = await getAssistantLocation()
    if (current === request) location.value = value
  } catch (reason) {
    if (current === request)
      error.value = reason instanceof Error ? reason.message : '定位失败，请重试。'
  } finally {
    if (current === request) {
      busy.value = false
      emit('busy', false)
    }
  }
}

watch(() => props.scopeKey, clear)
onDeactivated(clear)
onUnmounted(clear)
</script>

<template>
  <div class="location-picker" aria-label="消息定位">
    <div class="location-picker__row">
      <button type="button" :disabled="disabled || busy" :aria-busy="busy" @click="locate">
        <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M12 21s7-6 7-12a7 7 0 1 0-14 0c0 6 7 12 7 12Z" />
          <circle cx="12" cy="9" r="2" />
        </svg>
        {{ busy ? '正在定位…' : location ? '重新定位' : '使用当前位置' }}
      </button>
      <p role="status">
        {{
          location
            ? `已附加当前位置，精度约 ${Math.ceil(location.accuracyMeters)} 米。仅随下一条消息发送。`
            : '获取一次位置，发送后供 AI 和高德查询使用。'
        }}
      </p>
      <button v-if="location || busy" type="button" :disabled="disabled" @click="clear">
        {{ busy ? '取消定位' : '移除位置' }}
      </button>
    </div>
    <p v-if="error" class="location-picker__error" role="alert">{{ error }}</p>
  </div>
</template>

<style scoped>
.location-picker {
  flex: 0 0 auto;
}
.location-picker__row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px 12px;
}
.location-picker button {
  min-height: 44px;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  border: 0;
  border-radius: var(--radius-md);
  padding: 8px;
  color: var(--primary-strong);
  background: var(--surface-soft);
}
.location-picker button:focus-visible {
  outline: 2px solid var(--primary);
  outline-offset: 2px;
}
.location-picker p {
  flex: 1 1 180px;
  margin: 0;
  color: var(--ink-secondary);
  font-size: 13px;
  line-height: 1.5;
}
.location-picker .location-picker__error {
  margin-top: 4px;
  color: var(--danger);
}
</style>
