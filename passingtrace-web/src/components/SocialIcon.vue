<script setup lang="ts">
defineProps<{
  name:
    | 'chat'
    | 'friends'
    | 'add'
    | 'more'
    | 'back'
    | 'notice'
    | 'close'
    | 'chevron'
    | 'record'
    | 'storyline'
}>()
const paths = {
  chat: 'M4 4h16v12H9l-5 4V4Z M8 8h8 M8 12h5',
  friends:
    'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M16 4a4 4 0 0 1 0 8 M22 21v-2a4 4 0 0 0-3-3.87 M13 8a4 4 0 1 1-8 0 4 4 0 0 1 8 0Z',
  add: 'M12 5v14 M5 12h14',
  more: 'M5 12h.01 M12 12h.01 M19 12h.01',
  back: 'm14 5-7 7 7 7 M7 12h14',
  close: 'm6 6 12 12 M6 18 18 6',
  notice: 'M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9 M10 21h4',
  chevron: 'm9 5 7 7-7 7',
  record: 'M14 3H5v18h14V8l-5-5Z M14 3v5h5 M8 12h8 M8 16h6',
  storyline: 'M5 5h4v4H5z M15 15h4v4h-4z M7 9v6a2 2 0 0 0 2 2h6 M9 7h6a2 2 0 0 1 2 2v6',
}
</script>
<template>
  <svg
    viewBox="0 0 24 24"
    width="22"
    height="22"
    fill="none"
    stroke="currentColor"
    stroke-width="1.8"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path :d="paths[name]" />
  </svg>
</template>
