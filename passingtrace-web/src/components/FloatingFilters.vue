<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, useId } from 'vue'
import type { FilterField, FilterValues } from './filter-types'
const props = defineProps<{ title: string; fields: FilterField[]; values: FilterValues }>()
const emit = defineEmits<{ apply: [values: FilterValues]; open: [] }>()
const panel = ref<HTMLDialogElement>()
const trigger = ref<HTMLButtonElement>()
const draft = ref<FilterValues>({})
const opened = ref(false)
const error = ref('')
const id = useId()
let previousOverflow: string | undefined
function unlockScroll() {
  if (previousOverflow !== undefined) document.body.style.overflow = previousOverflow
  previousOverflow = undefined
}
onBeforeUnmount(unlockScroll)
const visible = (field: FilterField, values: FilterValues) =>
  !field.ownOnly || values.scope !== 'joint'
function defaults(): FilterValues {
  return Object.fromEntries(
    props.fields.map((f) => [f.key, f.type === 'tags' ? [] : (f.defaultValue ?? '')]),
  )
}
function copy(values: FilterValues): FilterValues {
  return Object.fromEntries(
    Object.entries(values).map(([key, value]) => [key, Array.isArray(value) ? [...value] : value]),
  )
}
const chips = computed(() =>
  props.fields
    .filter((f) => visible(f, props.values))
    .flatMap((f) => {
      const value = props.values[f.key]
      const values = Array.isArray(value) ? value : value && value !== f.defaultValue ? [value] : []
      return values.map((value) => ({
        key: f.key,
        value,
        label: `${f.label}：${f.options?.find((o) => o.value === value)?.label ?? value}`,
      }))
    }),
)
async function open() {
  if (opened.value) return
  previousOverflow = document.body.style.overflow
  document.body.style.overflow = 'hidden'
  draft.value = { ...defaults(), ...copy(props.values) }
  error.value = ''
  opened.value = true
  emit('open')
  await nextTick()
  panel.value?.showModal()
}
function close() {
  panel.value?.close()
  opened.value = false
  unlockScroll()
  trigger.value?.focus()
}
function resetDraft() {
  draft.value = defaults()
  error.value = ''
}
function apply() {
  if (
    draft.value.scope !== 'joint' &&
    draft.value.from &&
    draft.value.to &&
    draft.value.from > draft.value.to
  ) {
    error.value = '结束日期不能早于开始日期。'
    panel.value?.querySelectorAll<HTMLInputElement>('input[type="date"]')[1]?.focus()
    return
  }
  const value = copy(draft.value)
  for (const field of props.fields)
    if (!visible(field, value)) value[field.key] = defaults()[field.key]!
  emit('apply', value)
  close()
}
function remove(key: string, value: string) {
  const next = copy(props.values)
  const current = next[key]
  next[key] = Array.isArray(current) ? current.filter((v) => v !== value) : defaults()[key]!
  emit('apply', next)
}
</script>

<template>
  <div v-if="chips.length" class="filter-summary" aria-label="已应用筛选">
    <button
      v-for="chip in chips"
      :key="`${chip.key}-${chip.value}`"
      type="button"
      :aria-label="`移除${chip.label}`"
      @click="remove(chip.key, chip.value)"
    >
      {{ chip.label }} <span aria-hidden="true">×</span>
    </button>
    <button type="button" class="filter-clear" @click="emit('apply', defaults())">清除全部</button>
  </div>
  <button
    ref="trigger"
    class="filter-fab"
    type="button"
    :aria-label="`${title}${chips.length ? `，已应用 ${chips.length} 项` : ''}`"
    aria-haspopup="dialog"
    :aria-expanded="opened"
    @click="open"
  >
    <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
      <path d="M4 6h16M7 12h10M10 18h4" />
    </svg>
    筛选 <span v-if="chips.length" class="filter-count">{{ chips.length }}</span>
  </button>
  <Teleport to="body">
    <dialog
      ref="panel"
      class="filter-dialog"
      :aria-labelledby="id"
      @cancel.prevent="close"
      @click="$event.target === panel && close()"
    >
      <form v-if="opened" class="filter-form" @submit.prevent="apply">
        <header class="filter-heading">
          <div>
            <h2 :id="id">{{ title }}</h2>
            <p>组合条件，找到想看的内容</p>
          </div>
          <button type="button" aria-label="关闭筛选" @click="close">×</button>
        </header>
        <div class="filter-body">
          <template v-for="field in fields" :key="field.key">
            <template v-if="visible(field, draft)">
              <details v-if="field.type === 'tags'" class="filter-tags">
                <summary>
                  {{ field.label }}
                  <small v-if="Array.isArray(draft[field.key]) && draft[field.key]?.length"
                    >已选 {{ draft[field.key]?.length }}</small
                  >
                </summary>
                <p v-if="field.unavailable">分类数据暂不可用，可继续使用其他筛选。</p>
                <div v-else class="tag-options">
                  <label v-for="option in field.options" :key="option.value"
                    ><input v-model="draft[field.key]" type="checkbox" :value="option.value" />{{
                      option.label
                    }}</label
                  >
                </div>
              </details>
              <label v-else class="filter-field"
                ><span>{{ field.label }}</span>
                <input
                  v-if="field.type === 'date'"
                  v-model="draft[field.key]"
                  type="date"
                  :aria-describedby="error ? `${id}-error` : undefined"
                />
                <select v-else v-model="draft[field.key]" :disabled="field.unavailable">
                  <option v-if="!field.defaultValue" value="">全部</option>
                  <option v-for="option in field.options" :key="option.value" :value="option.value">
                    {{ option.label }}
                  </option>
                </select>
                <small v-if="field.unavailable">分类数据暂不可用，请关闭后重试。</small>
              </label>
            </template>
          </template>
          <p v-if="draft.scope === 'joint'" class="filter-note">
            共同记录展示好友 @ 你的经历，暂不支持个人记录的细项筛选。
          </p>
          <p v-if="error" :id="`${id}-error`" role="alert">{{ error }}</p>
        </div>
        <footer class="filter-actions">
          <button type="button" class="button button-secondary" @click="resetDraft">重置</button
          ><button class="button button-primary" type="submit">应用筛选</button>
        </footer>
      </form>
    </dialog>
  </Teleport>
</template>

<style scoped>
.filter-fab {
  position: fixed;
  right: max(24px, env(safe-area-inset-right));
  bottom: calc(24px + env(safe-area-inset-bottom));
  z-index: 110;
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 48px;
  padding: 0 18px;
  border: 1px solid var(--line);
  border-radius: 99px;
  background: var(--primary);
  color: var(--on-primary);
  box-shadow: var(--shadow-2);
  cursor: pointer;
}
.filter-count {
  font-size: 12px;
  font-weight: 700;
}
.filter-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 24px;
}
.filter-summary button {
  min-height: 36px;
  padding: 6px 12px;
  border: 1px solid var(--line);
  border-radius: 99px;
  color: var(--primary-strong);
  background: var(--primary-soft);
  cursor: pointer;
  overflow-wrap: anywhere;
}
.filter-summary .filter-clear {
  background: transparent;
  border-color: transparent;
}
.filter-dialog {
  position: fixed;
  inset: 0 0 0 auto;
  margin: 0;
  width: min(420px, 100%);
  max-width: 100%;
  height: 100dvh;
  max-height: 100dvh;
  padding: 0;
  border: 0;
  border-left: 1px solid var(--line);
  background: var(--surface);
  color: var(--ink);
  box-shadow: var(--shadow-2);
}
.filter-dialog::backdrop {
  background: rgb(0 0 0 / 40%);
}
.filter-dialog[open] {
  animation: filter-enter 180ms ease-out;
}
.filter-form {
  height: 100%;
  display: flex;
  flex-direction: column;
}
.filter-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 24px;
  border-bottom: 1px solid var(--line);
  gap: 16px;
}
.filter-heading h2 {
  font-size: 20px;
  margin: 0;
}
.filter-heading p,
.filter-note {
  font-size: 13px;
  color: var(--ink-secondary);
}
.filter-heading p {
  margin: 6px 0 0;
}
.filter-heading button {
  width: 44px;
  height: 44px;
  flex-shrink: 0;
  border: 0;
  border-radius: 50%;
  background: var(--surface-soft);
  color: var(--ink);
  font-size: 26px;
  cursor: pointer;
}
.filter-body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 20px;
}
.filter-field {
  display: grid;
  gap: 8px;
  font-size: 14px;
}
.filter-field input,
.filter-field select {
  width: 100%;
  min-width: 0;
  min-height: 44px;
  padding: 8px 12px;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  background: var(--surface-soft);
  color: var(--ink);
  font: inherit;
}
.filter-tags summary {
  cursor: pointer;
  padding-block: 12px;
}
.filter-tags small,
.filter-field small {
  color: var(--ink-secondary);
}
.tag-options {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  padding-top: 12px;
}
.tag-options label {
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 44px;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  padding: 8px 12px;
  cursor: pointer;
}
.tag-options label:has(:checked) {
  background: var(--primary-soft);
  color: var(--primary-strong);
}
.filter-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 16px 24px max(16px, env(safe-area-inset-bottom));
  border-top: 1px solid var(--line);
}
@keyframes filter-enter {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}
@media (max-width: 600px) {
  .filter-dialog {
    inset: auto 0 0;
    width: 100%;
    height: 85dvh;
    border-radius: 24px 24px 0 0;
    border-left: 0;
  }
  .filter-dialog[open] {
    animation-name: filter-rise;
  }
  .filter-field input,
  .filter-field select {
    font-size: 16px;
  }
  .filter-fab {
    bottom: calc(20px + env(safe-area-inset-bottom));
    right: 16px;
  }
}
@keyframes filter-rise {
  from {
    transform: translateY(100%);
  }
  to {
    transform: translateY(0);
  }
}
@media (prefers-reduced-motion: reduce) {
  .filter-dialog[open] {
    animation: none;
  }
}
</style>
