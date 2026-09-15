<script setup lang="ts">
import { ref, watch } from 'vue'
const props = withDefaults(defineProps<{ src?: string; size?: number }>(), { src: '', size: 40 })
const failed = ref(false)
watch(
  () => props.src,
  () => {
    failed.value = false
  },
)
</script>

<template>
  <span
    class="account-avatar"
    :style="{ width: `${size}px`, height: `${size}px` }"
    aria-hidden="true"
  >
    <img v-if="src && !failed" :src="src" alt="" @error="failed = true" />
    <svg v-else viewBox="0 0 48 48">
      <circle cx="24" cy="17" r="7" />
      <path d="M10 39c0-8 6-13 14-13s14 5 14 13" />
    </svg>
  </span>
</template>

<style scoped>
.account-avatar {
  display: inline-flex;
  flex: none;
  overflow: hidden;
  border-radius: 50%;
  color: var(--primary-strong);
  background: var(--primary-soft);
  border: 1px solid var(--line-strong);
}
img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
svg {
  margin: 15%;
  width: 70%;
  height: 70%;
  fill: none;
  stroke: currentColor;
  stroke-width: 2.4;
  stroke-linecap: round;
}
</style>
