<script setup lang="ts">
import type { ApprovalRequest } from '@/api/ai'
import AssistantMessageContent from '@/components/AssistantMessageContent'

defineProps<{ approval: ApprovalRequest; remaining: number; busy: boolean; error: string }>()
defineEmits<{ decide: [decision: 'confirm' | 'cancel'] }>()
</script>

<template>
  <section class="approval-panel" role="region" aria-label="删除授权">
    <p class="approval-panel__heading" role="status">
      确认删除{{
        approval.targetType === 'Storyline'
          ? '故事线'
          : approval.targetType === 'Plan'
            ? '计划'
            : approval.targetType === 'Subject'
              ? '人物档案'
              : approval.targetType === 'SubjectEntry'
                ? '人物专属内容'
                : approval.targetType === 'SubjectRelation'
                  ? '误关联'
                  : '记录'
      }}？
    </p>
    <p v-if="approval.targetType.startsWith('Subject')">{{ approval.title }}</p>
    <AssistantMessageContent
      v-else
      :content="
        approval.targetType === 'Storyline'
          ? `[Storyline #${approval.targetId}]`
          : `[Event #${approval.targetId}]`
      "
      :conversation-id="approval.conversationId"
      :records="
        approval.targetType === 'Storyline'
          ? []
          : [{ eventId: Number(approval.targetId), title: approval.title }]
      "
      :storylines="
        approval.targetType === 'Storyline'
          ? [{ storylineId: approval.targetId, title: approval.title }]
          : []
      "
    />
    <p>{{ approval.description }}</p>
    <p v-if="remaining > 1">另有 {{ remaining - 1 }} 项等待逐一确认。</p>
    <p v-if="error" class="approval-panel__error" role="alert">{{ error }}</p>
    <div class="approval-panel__buttons">
      <button type="button" :disabled="busy" @click="$emit('decide', 'cancel')">取消</button>
      <button
        type="button"
        class="approval-panel__confirm"
        :disabled="busy"
        @click="$emit('decide', 'confirm')"
      >
        {{ busy ? '正在处理…' : '确定' }}
      </button>
    </div>
  </section>
</template>

<style scoped>
.approval-panel {
  flex: none;
  padding: 16px;
  border: 1px solid var(--danger);
  border-radius: 12px;
  background: var(--surface);
  max-height: 40dvh;
  overflow-y: auto;
}
.approval-panel p {
  margin: 0 0 8px;
  overflow-wrap: anywhere;
}
.approval-panel__heading {
  font-weight: 700;
}
.approval-panel__buttons {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}
.approval-panel button {
  min-height: 44px;
  min-width: 80px;
  padding: 8px 16px;
  border: 1px solid currentColor;
  border-radius: 8px;
  cursor: pointer;
  background: transparent;
  font: inherit;
}
.approval-panel .approval-panel__confirm {
  color: var(--danger);
}
.approval-panel__error {
  color: var(--danger);
}
.approval-panel button:disabled {
  opacity: 0.6;
  cursor: wait;
}
.approval-panel button:focus-visible {
  outline: 2px solid currentColor;
  outline-offset: 3px;
}
</style>
