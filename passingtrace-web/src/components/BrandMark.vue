<script setup lang="ts">
withDefaults(defineProps<{ decorative?: boolean }>(), { decorative: true })
</script>

<template>
  <svg
    class="brand-mark-svg"
    viewBox="0 0 108 108"
    :aria-hidden="decorative ? 'true' : undefined"
    :role="decorative ? undefined : 'img'"
    focusable="false"
  >
    <title v-if="!decorative">星期八记忆盒</title>
    <rect x="3" y="3" width="102" height="102" rx="28" fill="currentColor" />
    <rect x="28" y="22" width="28" height="40" rx="5" fill="#fff6e8" transform="rotate(-8 42 42)" />
    <circle cx="40" cy="34" r="6" fill="#e28a62" />
    <path d="M31 51 39 42 46 48 53 40 57 58H31Z" fill="#a8ccba" transform="rotate(-8 44 49)" />
    <rect x="53" y="25" width="28" height="40" rx="5" fill="#e28a62" transform="rotate(7 67 45)" />
    <rect x="60" y="33" width="14" height="5" rx="2.5" fill="#fff6e8" transform="rotate(7 67 35)" />
    <rect x="60" y="43" width="11" height="5" rx="2.5" fill="#fff6e8" transform="rotate(7 65 45)" />
    <path d="M19 49H89L82 63H26Z" fill="#d97856" />
    <path d="M25 59H83L78 85H30Z" fill="#fff6e8" />
    <rect x="45" y="65" width="18" height="8" rx="4" fill="#e28a62" />
  </svg>
</template>

<style scoped>
.brand-mark-svg {
  display: block;
  width: 100%;
  height: 100%;
  color: var(--primary);
}
</style>
