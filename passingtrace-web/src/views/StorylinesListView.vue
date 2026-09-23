<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import FloatingFilters from '@/components/FloatingFilters.vue'
import { dateBoundary, type FilterField, type FilterValues } from '@/components/filter-types'
import WebAppHeader from '@/components/WebAppHeader.vue'
import { mediaApi } from '@/api/media'
import { storylinesApi } from '@/api/storylines'
import { StorylineStatus, type StorylineSummary } from '@/api/storylines-types'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const items = ref<StorylineSummary[]>([])
const loading = ref(false)
const error = ref('')
const status = ref<number | ''>('')
const category = ref('')
const from = ref('')
const to = ref('')
const filterFields: FilterField[] = [
  {
    key: 'status',
    label: '进度',
    options: [
      { value: String(StorylineStatus.Ongoing), label: '进行中' },
      { value: String(StorylineStatus.Completed), label: '已完成' },
    ],
  },
  { key: 'from', label: '开始日期', type: 'date' },
  { key: 'to', label: '结束日期', type: 'date' },
  {
    key: 'category',
    label: '分类',
    options: [
      { value: 'trip', label: '行程旅行' },
      { value: 'activity', label: '活动纪实' },
      { value: 'project', label: '项目过程' },
      { value: 'challenge', label: '目标挑战' },
      { value: 'lifecycle', label: '成长陪伴' },
      { value: 'series', label: '主题系列' },
      { value: 'life-period', label: '生活阶段' },
      { value: 'other', label: '其他' },
    ],
  },
]
const filterValues = computed<FilterValues>(() => ({
  status: String(status.value),
  category: category.value,
  from: from.value,
  to: to.value,
}))
const hasFilters = computed(
  () => status.value !== '' || !!category.value || !!from.value || !!to.value,
)
function applyFilters(values: FilterValues) {
  status.value = values.status === '' ? '' : Number(values.status)
  category.value = String(values.category || '')
  from.value = String(values.from || '')
  to.value = String(values.to || '')
  if (auth.isAuthenticated) void load()
}
let loadVersion = 0
const coverUrls = ref<Record<string, string>>({})

const grouped = computed(() => ({
  ongoing: items.value.filter((x) => x.status === StorylineStatus.Ongoing),
  completed: items.value.filter((x) => x.status === StorylineStatus.Completed),
}))
function dateRange(item: StorylineSummary) {
  if (!item.rangeStart && !item.rangeEnd) return '时间范围待补充'
  const format = (value: string | null) =>
    value ? new Date(value).toLocaleDateString('zh-CN', { month: 'short', day: 'numeric' }) : '未定'
  return `${format(item.rangeStart)} — ${format(item.rangeEnd)}`
}
async function load() {
  const version = ++loadVersion
  loading.value = true
  error.value = ''
  try {
    const page = await storylinesApi.list({
      status: status.value === '' ? undefined : status.value,
      categoryKey: category.value || undefined,
      from: dateBoundary(from.value),
      to: dateBoundary(to.value, true),
      limit: 60,
    })
    if (version !== loadVersion) return
    items.value = page.items
    const covers = page.items
      .map((item) => item.coverMediaAssetId)
      .filter((id): id is string => id !== null && !coverUrls.value[id])
    await Promise.all(
      covers.map(async (id) => {
        try {
          coverUrls.value[id] = (await mediaApi.access(id)).url
        } catch {
          // 私有封面不可用时保留分类占位图，不阻断列表。
        }
      }),
    )
  } catch (reason) {
    if (version !== loadVersion) return
    error.value = reason instanceof Error ? reason.message : '加载故事线失败。'
  } finally {
    if (version === loadVersion) loading.value = false
  }
}
onMounted(() => {
  if (auth.isAuthenticated) void load()
})
watch(
  () => auth.isAuthenticated,
  (value) => {
    if (value) void load()
  },
)
</script>

<template>
  <div class="app-shell">
    <WebAppHeader />
    <main class="workspace-main story-list-page">
      <header class="story-list-heading">
        <div>
          <p class="eyebrow">STORYLINES</p>
          <h1>把散落的记录，连成完整经历</h1>
          <p>按阶段整理旅行、项目、活动和长时间发生的故事。</p>
        </div>
        <RouterLink class="button button-primary" to="/storylines/new">新建故事线</RouterLink>
      </header>
      <FloatingFilters
        title="筛选故事线"
        :fields="filterFields"
        :values="filterValues"
        @apply="applyFilters"
      />
      <p v-if="error" class="error-banner">{{ error }}</p>
      <div v-if="loading" class="story-empty">
        <span class="loading-ring" aria-label="正在加载"></span>
      </div>
      <section v-else-if="items.length" class="story-groups">
        <div v-if="grouped.ongoing.length">
          <h2>正在发生</h2>
          <div class="story-grid">
            <RouterLink
              v-for="item in grouped.ongoing"
              :key="item.id"
              class="story-card"
              :to="`/storylines/${item.id}`"
            >
              <div class="story-cover" :class="`story-cover--${item.categoryKey}`">
                <img
                  v-if="item.coverMediaAssetId && coverUrls[item.coverMediaAssetId]"
                  :src="coverUrls[item.coverMediaAssetId]"
                  :alt="`${item.title}封面`"
                />
                <span v-else aria-hidden="true">{{ item.categoryLabel.slice(0, 1) }}</span>
              </div>
              <div class="story-card__body">
                <p class="story-meta">
                  <span>{{ item.categoryLabel }}</span
                  ><span>进行中</span>
                </p>
                <h3>{{ item.title }}</h3>
                <p>{{ item.description || '从第一条记录开始，把经历慢慢连起来。' }}</p>
                <div class="story-tags">
                  <span v-for="tag in item.tags.slice(0, 3)" :key="tag">{{ tag }}</span>
                </div>
                <footer>
                  <span>{{ dateRange(item) }}</span
                  ><strong>{{ item.nodeCount }} 个节点</strong>
                </footer>
              </div>
            </RouterLink>
          </div>
        </div>
        <div v-if="grouped.completed.length">
          <h2>已经收好</h2>
          <div class="story-grid">
            <RouterLink
              v-for="item in grouped.completed"
              :key="item.id"
              class="story-card"
              :to="`/storylines/${item.id}`"
              ><div class="story-cover story-cover--completed">
                <img
                  v-if="item.coverMediaAssetId && coverUrls[item.coverMediaAssetId]"
                  :src="coverUrls[item.coverMediaAssetId]"
                  :alt="`${item.title}封面`"
                />
                <span v-else aria-hidden="true">✓</span>
              </div>
              <div class="story-card__body">
                <p class="story-meta">
                  <span>{{ item.categoryLabel }}</span
                  ><span>已完成</span>
                </p>
                <h3>{{ item.title }}</h3>
                <p>{{ item.description || '一段已经整理完成的经历。' }}</p>
                <footer>
                  <span>{{ dateRange(item) }}</span
                  ><strong>{{ item.nodeCount }} 个节点</strong>
                </footer>
              </div></RouterLink
            >
          </div>
        </div>
      </section>
      <section v-else class="story-empty">
        <h2>{{ hasFilters ? '没有符合条件的故事线' : '还没有故事线' }}</h2>
        <p>
          {{
            hasFilters
              ? '试试调整筛选条件，或清除全部条件。'
              : '可以从一次旅行、一个项目或一组主题记录开始。'
          }}
        </p>
        <RouterLink v-if="!hasFilters" class="button button-primary" to="/storylines/new"
          >创建第一条故事线</RouterLink
        >
      </section>
    </main>
  </div>
</template>

<style scoped>
.story-list-page {
  padding-bottom: 100px;
}
.story-list-heading {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: var(--workspace-gap);
  margin-bottom: 28px;
}
.story-list-heading h1 {
  max-width: 48rem;
  margin: 0;
  font-size: clamp(34px, 5vw, 58px);
  line-height: 1.05;
  letter-spacing: -0.055em;
  text-wrap: balance;
}
.story-list-heading p:last-child {
  color: var(--ink-secondary);
}
.story-list-heading > div {
  min-width: 0;
}
.story-list-heading > .button {
  flex-shrink: 0;
}
.story-groups > div {
  margin-top: 42px;
}
.story-groups h2 {
  font-size: 18px;
}
.story-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 28rem), 1fr));
  gap: var(--workspace-gap);
}
.story-card {
  min-width: 0;
  min-height: 230px;
  display: grid;
  grid-template-columns: minmax(0, 28%) minmax(0, 1fr);
  overflow: hidden;
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  background: var(--surface);
  transition:
    transform var(--motion-fast),
    box-shadow var(--motion-fast);
}
.story-card:hover {
  transform: translateY(-2px);
  box-shadow: var(--shadow-1);
}
.story-cover {
  min-width: 0;
  position: relative;
  display: grid;
  place-items: center;
  background: linear-gradient(145deg, var(--primary-soft), var(--surface-tint));
  color: var(--primary-strong);
  font-size: 52px;
  font-weight: 800;
}
.story-cover img {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.story-cover--trip {
  background: linear-gradient(145deg, #cfe8df, #e8dbc7);
}
.story-cover--project {
  background: linear-gradient(145deg, #d8e2ef, #e5efe9);
}
.story-cover--completed {
  background: var(--surface-soft);
}
.story-card__body {
  min-width: 0;
  padding: clamp(16px, 1.5vw, 24px);
  display: flex;
  flex-direction: column;
  overflow-wrap: anywhere;
}
.story-meta {
  margin: 0;
  display: flex;
  flex-wrap: wrap;
  gap: 7px;
}
.story-meta span,
.story-tags span {
  padding: 3px 8px;
  border-radius: 99px;
  color: var(--primary-strong);
  background: var(--primary-soft);
  font-size: 10px;
}
.story-card h3 {
  margin: 14px 0 7px;
  font-size: 23px;
}
.story-card__body > p:not(.story-meta) {
  margin: 0;
  color: var(--ink-secondary);
  font-size: 13px;
}
.story-tags {
  margin-top: 13px;
  display: flex;
  flex-wrap: wrap;
  gap: 5px;
}
.story-card footer {
  margin-top: auto;
  padding-top: 18px;
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 8px 16px;
  color: var(--ink-tertiary);
  font-size: 11px;
}
.story-card footer strong {
  color: var(--ink-secondary);
}
.story-empty {
  min-height: 340px;
  margin-top: 32px;
  display: grid;
  place-items: center;
  align-content: center;
  text-align: center;
  gap: 10px;
  border: 1px dashed var(--line-strong);
  border-radius: var(--radius-xl);
  background: var(--surface-soft);
}
.story-empty h2,
.story-empty p {
  margin: 0;
}
.story-empty p {
  margin-bottom: 12px;
  color: var(--ink-secondary);
}
@media (max-width: 800px) {
  .story-list-heading {
    align-items: start;
    flex-direction: column;
  }
  .story-list-heading h1 {
    font-size: 38px;
  }
  .story-grid {
    grid-template-columns: 1fr;
  }
}
@media (prefers-reduced-motion: reduce) {
  .story-card {
    transition: none;
  }
  .story-card:hover {
    transform: none;
  }
}
.sr-only {
  width: 1px;
  height: 1px;
  padding: 0;
  position: absolute;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
