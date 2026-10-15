<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { socialApi, type Friend } from '@/api/social'
const ids = defineModel<string[]>({ default: () => [] })
const props = defineProps<{ mentionTrigger?: number }>()
const open = ref(false)
const query = ref('')
const friends = ref<Friend[]>([])
const error = ref('')
const visible = computed(() =>
  friends.value.filter((f) => `${f.person.nickname} ${f.remark}`.includes(query.value)),
)
async function load() {
  try {
    friends.value = await socialApi.friends()
  } catch {
    error.value = '好友暂时无法加载，请重试。'
  }
}
function toggle(id: string) {
  ids.value = ids.value.includes(id) ? ids.value.filter((x) => x !== id) : [...ids.value, id]
}
watch(
  () => props.mentionTrigger,
  () => {
    open.value = true
  },
)
onMounted(load)
</script>
<template>
  <fieldset class="participants">
    <legend>一起的人</legend>
    <p>添加后，好友会收到提醒，并能查看这条记录和后续更新。</p>
    <div class="people-chips">
      <button v-for="id in ids" :key="id" type="button" @click="toggle(id)">
        {{
          friends.find((f) => f.person.id === id)?.remark ||
          friends.find((f) => f.person.id === id)?.person.nickname ||
          '已添加的参与者'
        }}
        ×
      </button>
      <button type="button" :aria-expanded="open" @click="open = !open">＋ 添加好友</button>
    </div>
    <div v-if="open" class="people-options">
      <label>查找好友<input v-model="query" placeholder="昵称或备注" /></label>
      <p v-if="error" role="alert">{{ error }} <button type="button" @click="load">重试</button></p>
      <label v-for="friend in visible" :key="friend.id" class="person-option"
        ><input
          type="checkbox"
          :checked="ids.includes(friend.person.id)"
          @change="toggle(friend.person.id)"
        />{{ friend.remark || friend.person.nickname }}</label
      >
      <p v-if="!visible.length && !error">还没有匹配的好友。可以在“消息”里添加。</p>
      <button class="button button-secondary" type="button" @click="open = false">完成选择</button>
    </div>
  </fieldset>
</template>
<style scoped>
.participants {
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  padding: 1rem;
  min-width: 0;
}
p {
  color: var(--ink-secondary);
  font-size: 0.9rem;
}
.people-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}
.people-chips button {
  background: var(--primary-soft);
  color: var(--primary-strong);
  border: 0;
  border-radius: 2rem;
  padding: 0.7rem 1rem;
  cursor: pointer;
}
.people-options {
  margin-top: 1rem;
  max-height: 18rem;
  overflow: auto;
}
.people-options > label:first-child {
  display: grid;
  gap: 0.5rem;
}
.people-options input:not([type='checkbox']) {
  padding: 0.8rem;
  background: var(--surface);
  color: var(--ink);
  border: 1px solid var(--line);
  border-radius: 0.6rem;
}
.person-option {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  min-height: 44px;
}
</style>
