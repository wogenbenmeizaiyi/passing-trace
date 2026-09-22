<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import WebAppHeader from '@/components/WebAppHeader.vue'
import SharedAttachment from '@/components/SharedAttachment.vue'
import { socialApi, type SharedDocument } from '@/api/social'
import { useSocialStore } from '@/stores/social'
import { useAuthStore } from '@/stores/auth'
const route = useRoute(),
  router = useRouter(),
  social = useSocialStore(),
  auth = useAuthStore()
const doc = ref<SharedDocument | null>(null),
  error = ref(''),
  author = ref('作者'),
  ownerId = ref(''),
  busy = ref(false)
const joint = computed(() => route.path.startsWith('/joint-records/'))
const endpoint = computed(() =>
  joint.value
    ? `/api/v1/friends/shared-records/${route.params.id}`
    : `/api/v1/shares/${route.params.id}`,
)
const back = computed(() =>
  typeof route.query.from === 'string' &&
  /^\/(messages(?:\/[a-f\d-]{36})?|assistant(?:\?conversation=[a-f\d-]{36})?|events)$/.test(
    route.query.from,
  )
    ? route.query.from
    : '/messages',
)
let generation = 0,
  disposed = false
async function load() {
  const turn = ++generation
  error.value = ''
  busy.value = true
  // Revoke already downloaded object URLs before a new permission check.
  doc.value = null
  try {
    const result = joint.value
      ? await socialApi.jointRecord(Number(route.params.id))
      : await socialApi.shared(String(route.params.id))
    if (disposed || turn !== generation) return
    doc.value = 'document' in result ? result.document : result
    ownerId.value = 'ownerId' in result ? result.ownerId : doc.value.authorId
    const people = await socialApi.profiles([doc.value.authorId])
    if (!disposed && turn === generation) author.value = people[0]?.nickname || '作者'
  } catch {
    if (!disposed && turn === generation)
      error.value = '内容已不可查看，可能已停止分享、移除关联或删除。'
  } finally {
    if (turn === generation) busy.value = false
  }
}
async function remove() {
  if (
    !window.confirm(
      joint.value
        ? '移除与你的关联后，你将无法继续查看这条共同记录。'
        : '停止分享后，好友将无法再打开这张卡片。已经保存的图片无法收回。',
    )
  )
    return
  try {
    if (joint.value) await socialApi.detach(Number(route.params.id))
    else await socialApi.revoke(String(route.params.id))
    await router.push(back.value)
  } catch {
    error.value = '暂时无法完成，请重试。'
  }
}
watch(
  () => route.fullPath,
  () => void load(),
)
watch(
  () => social.change,
  () => void load(),
)
onMounted(load)
onUnmounted(() => {
  disposed = true
  generation++
})
</script>
<template>
  <div>
    <WebAppHeader />
    <main class="shared-page">
      <header>
        <RouterLink :to="back"
          >← 返回{{ back.startsWith('/assistant') ? ' AI 会话' : '消息' }}</RouterLink
        ><button
          v-if="doc?.available && (joint || ownerId === auth.user?.profile.sub)"
          class="text-button"
          @click="remove"
        >
          {{ joint ? '移除与我的关联' : '停止分享' }}
        </button>
      </header>
      <p v-if="busy">正在查看…</p>
      <p v-if="error" role="alert">
        {{ error }} <button class="text-button" @click="load">重试</button>
      </p>
      <template v-if="doc"
        ><p class="eyebrow">
          {{ joint ? '共同参与 · 随作者更新' : '好友分享 · 发送时版本' }} · 作者 {{ author }}
        </p>
        <h1>{{ doc.available ? doc.title : '内容已不可查看' }}</h1>
        <p v-if="doc.description">{{ doc.description }}</p>
        <template v-if="doc.available"
          ><section v-if="doc.stages.length" class="stages">
            <h2>故事阶段</h2>
            <ol>
              <li v-for="s in [...doc.stages].sort((a, b) => a.order - b.order)" :key="s.key">
                {{ s.title }}
              </li>
            </ol>
          </section>
          <section v-for="r in doc.records" :key="r.eventId" class="record">
            <details :open="doc.kind === 'record'">
              <summary>
                {{ r.available ? r.title : '内容已不可查看'
                }}<small v-if="r.available"
                  >{{
                    r.status === 'Completed'
                      ? '已完成'
                      : r.status === 'Planned'
                        ? '待执行'
                        : '已取消'
                  }}
                  ·
                  {{
                    r.happenedAt
                      ? new Date(r.happenedAt).toLocaleString()
                      : r.plannedAt
                        ? new Date(r.plannedAt).toLocaleString()
                        : '未填写时间'
                  }}</small
                >
              </summary>
              <template v-if="r.available"
                ><p v-if="r.labels?.length">{{ r.labels.join(' · ') }}</p>
                <p class="record-content">{{ r.content }}</p>
                <p v-for="(place, i) in r.places" :key="i">
                  {{ place.name }}<br />{{ place.address }}
                </p>
                <SharedAttachment v-for="m in r.media" :key="m.id" :media="m" :endpoint="endpoint"
              /></template>
            </details>
          </section>
          <section v-if="doc.edges.length" class="connections">
            <h2>节点关联</h2>
            <p v-for="(edge, index) in doc.edges" :key="index">
              {{
                doc.records.find(
                  (r) => r.eventId === doc!.nodes.find((n) => n.key === edge.source)?.eventId,
                )?.title || '已不可查看的节点'
              }}
              →
              {{
                doc.records.find(
                  (r) => r.eventId === doc!.nodes.find((n) => n.key === edge.target)?.eventId,
                )?.title || '已不可查看的节点'
              }}<small v-if="edge.label"> · {{ edge.label }}</small>
            </p>
          </section></template
        ></template
      >
    </main>
  </div>
</template>
<style scoped>
.shared-page {
  padding: 2rem var(--workspace-gutter);
}
header {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 2rem;
}
.eyebrow,
small {
  color: var(--ink-secondary);
}
h1 {
  font-size: clamp(1.6rem, 3vw, 2.8rem);
}
.record,
.stages,
.connections {
  padding: 1.5rem;
  margin-block: 1rem;
  background: var(--surface);
  border: 1px solid var(--line);
  border-radius: var(--radius-xl);
}
summary {
  cursor: pointer;
  font-weight: 600;
  padding-block: 0.5rem;
}
summary small {
  display: block;
  margin-top: 0.5rem;
  font-weight: normal;
}
.record-content {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  line-height: 1.8;
}
ol {
  display: flex;
  gap: 2.5rem;
  flex-wrap: wrap;
}
[role='alert'] {
  color: var(--danger);
}
</style>
