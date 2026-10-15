<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { socialApi, type JointRecord, type Person } from '@/api/social'
const rows = ref<JointRecord[]>([])
const cursor = ref<string | null>(null)
const people = ref<Person[]>([])
const error = ref('')
const busy = ref(false)
async function load() {
  busy.value = true
  try {
    const page = await socialApi.joint(cursor.value ? Number(cursor.value) : undefined)
    rows.value.push(...page.items)
    cursor.value = page.nextCursor
    people.value = await socialApi.profiles(
      [...new Set(rows.value.map((x) => x.authorId))].slice(0, 100),
    )
  } catch {
    error.value = '共同记录暂时无法加载。'
  } finally {
    busy.value = false
  }
}
onMounted(load)
</script>
<template>
  <section class="joint-records">
    <h2>共同参与的记录</h2>
    <p>这里是好友 @ 你一起完成的经历。</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <RouterLink v-for="r in rows" :key="r.id" :to="`/joint-records/${r.id}`"
      ><strong>{{ r.title }}</strong
      ><small
        >作者：{{ people.find((p) => p.id === r.authorId)?.nickname || '好友' }} ·
        {{ r.happenedAt ? new Date(r.happenedAt).toLocaleDateString() : '未填写发生时间' }}</small
      ></RouterLink
    >
    <p v-if="!rows.length && !busy">还没有共同参与的记录。</p>
    <button v-if="cursor" class="button button-secondary" :disabled="busy" @click="load">
      加载更多
    </button>
  </section>
</template>
<style scoped>
.joint-records {
  padding: var(--workspace-gutter);
}
a {
  display: grid;
  gap: 0.5rem;
  padding: 1.25rem;
  margin-block: 0.75rem;
  border: 1px solid var(--line);
  background: var(--surface);
  border-radius: var(--radius-lg);
  color: var(--ink);
  text-decoration: none;
}
small,
p {
  color: var(--ink-secondary);
}
</style>
