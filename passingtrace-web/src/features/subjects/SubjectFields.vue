<script setup lang="ts">
import type { SubjectField, SubjectValue } from '@/api/subjects'
import { useId } from 'vue'
const prefix = useId()
const props = defineProps<{
  fields: SubjectField[]
  allowDefinitions?: boolean
  planned?: boolean
}>()
const model = defineModel<Record<string, SubjectValue>>({ default: () => ({}) })
const emit = defineEmits<{ definitions: [fields: SubjectField[]] }>()
function update(id: string, change: Partial<SubjectField>) {
  emit(
    'definitions',
    props.fields.map((x) => (x.id === id ? { ...x, ...change } : x)),
  )
}
function add() {
  emit('definitions', [
    ...props.fields,
    { id: crypto.randomUUID(), name: '新字段', type: 'text', removed: false },
  ])
}
function input(field: SubjectField, event: Event) {
  const value = (event.target as HTMLInputElement).value
  model.value[field.id] = !value ? null : field.type === 'number' ? Number(value) : value
}
</script>
<template>
  <fieldset class="subject-fields">
    <legend>{{ allowDefinitions ? '生活字段' : planned ? '预期变化' : '实际变化' }}</legend>
    <p class="subject-muted">
      {{
        allowDefinitions
          ? '字段可以自定义；展开编辑字段可修改名称和类型。'
          : planned
            ? '预期值在完成计划并确认实际值后生效。'
            : '只填写本次发生变化的字段。'
      }}
    </p>
    <div v-for="field in fields.filter((x) => !x.removed)" :key="field.id" class="field-row">
      <label :for="prefix + field.id"
        >{{ field.name }}{{ field.unit ? ` (${field.unit})` : '' }}
        <select
          v-if="field.type === 'boolean'"
          :id="prefix + field.id"
          :value="String(model[field.id] ?? '')"
          :aria-label="field.name"
          @change="
            model[field.id] =
              ($event.target as HTMLSelectElement).value === ''
                ? null
                : ($event.target as HTMLSelectElement).value === 'true'
          "
        >
          <option value="">未填写</option>
          <option value="true">是</option>
          <option value="false">否</option>
        </select>
        <select
          v-else-if="field.type === 'select'"
          :id="prefix + field.id"
          v-model="model[field.id]"
          :aria-label="field.name"
        >
          <option :value="null">未填写</option>
          <option v-for="option in field.options" :key="option">{{ option }}</option>
        </select>
        <input
          v-else
          :id="prefix + field.id"
          :value="model[field.id] ?? ''"
          :type="
            field.type === 'number'
              ? 'number'
              : field.type === 'date'
                ? 'date'
                : field.type === 'phone'
                  ? 'tel'
                  : 'text'
          "
          step="any"
          :aria-label="field.name"
          @input="input(field, $event)"
        />
      </label>
      <details v-if="allowDefinitions" class="field-definitions">
        <summary>编辑字段 · {{ field.name }}</summary>
        <div class="field-definition-grid">
          <label
            >字段名称<input
              :value="field.name"
              aria-label="字段名称"
              @input="update(field.id, { name: ($event.target as HTMLInputElement).value })"
          /></label>
          <label
            >字段类型<select
              :value="field.type"
              aria-label="字段类型"
              @change="
                update(field.id, {
                  type: ($event.target as HTMLSelectElement).value as SubjectField['type'],
                })
              "
            >
              <option value="text">文本</option>
              <option value="number">数字</option>
              <option value="date">日期</option>
              <option value="boolean">是／否</option>
              <option value="select">单选</option>
              <option value="phone">电话号码</option>
            </select></label
          >
          <label v-if="field.type === 'number'"
            >字段单位<input
              :value="field.unit"
              aria-label="字段单位"
              @input="update(field.id, { unit: ($event.target as HTMLInputElement).value })"
          /></label>
          <label v-if="field.type === 'select'"
            >单选选项<input
              :value="field.options?.join('，')"
              placeholder="选项，用逗号分隔"
              aria-label="单选选项"
              @input="
                update(field.id, {
                  options: ($event.target as HTMLInputElement).value.split(/[,，]/).filter(Boolean),
                })
              "
          /></label>
        </div>
        <button
          class="text-button subject-danger"
          type="button"
          :aria-label="`移除字段 ${field.name}`"
          @click="update(field.id, { removed: true })"
        >
          移除字段
        </button>
      </details>
    </div>
    <p v-if="!fields.some((x) => !x.removed)" class="subject-muted">暂无生活字段。</p>
    <button v-if="allowDefinitions" class="button button-secondary" type="button" @click="add">
      ＋ 添加自定义字段
    </button>
  </fieldset>
</template>
<style scoped>
.subject-fields {
  min-inline-size: 0;
  margin: 0;
  display: grid;
  gap: 1.25rem;
  border: 1px solid var(--line);
  border-radius: 20px;
  background: var(--surface);
  padding: clamp(1rem, 2vw, 1.5rem);
}
.subject-fields > p {
  margin: 0;
}
.field-row {
  min-width: 0;
  display: grid;
  gap: 0.5rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--line);
}
.field-definitions summary {
  cursor: pointer;
  color: var(--primary-strong);
  padding: 0.75rem 0;
}
.field-definition-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
  margin: 0.5rem 0;
}
@media (max-width: 600px) {
  .field-definition-grid {
    grid-template-columns: 1fr;
  }
}
</style>
