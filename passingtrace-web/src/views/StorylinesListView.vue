<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
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
  loading.value = true
  error.value = ''
  try {
    const page = await storylinesApi.list({
      status: status.value || undefined,
      categoryKey: category.value || undefined,
      from: from.value ? new Date(`${from.value}T00:00:00`).toISOString() : undefined,
      to: to.value ? new Date(`${to.value}T23:59:59.999`).toISOString() : undefined,
      limit: 60,
    })
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
    error.value = reason instanceof Error ? reason.message : '加载故事线失败。'
  } finally {
    loading.value = false
  }
}
function clearFilters() {
  status.value = ''
  category.value = ''
  from.value = ''
  to.value = ''
  void load()
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
    <main class="story-list-page">
      <header class="story-list-heading">
        <div>
          <p class="eyebrow">STORYLINES</p>
          <h1>把散落的记录，连成完整经历</h1>
          <p>按阶段整理旅行、项目、活动和长时间发生的故事。</p>
        </div>
        <RouterLink class="button button-primary" to="/storylines/new">新建故事线</RouterLink>
      </header>
      <section class="story-filter" aria-label="故事线筛选">
        <div class="story-filter__heading">
          <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
            <path d="M4 6h16M7 12h10M10 18h4" />
          </svg>
          <div><strong>筛选故事线</strong><small>可按状态、日期范围和分类组合筛选</small></div>
        </div>
        <div class="story-filter__controls">
          <label class="filter-field"
            ><span>进度</span
            ><select v-model="status" @change="load">
              <option value="">全部</option>
              <option :value="StorylineStatus.Ongoing">进行中</option>
              <option :value="StorylineStatus.Completed">已完成</option>
            </select></label
          >
          <fieldset class="date-range">
            <legend>日期范围</legend>
            <label>
              <span class="sr-only">开始日期</span>
              <span class="date-input" :class="{ 'has-value': from }" data-placeholder="开始日期">
                <input v-model="from" type="date" aria-label="开始日期" @change="load" />
              </span>
            </label>
            <span aria-hidden="true">至</span>
            <label>
              <span class="sr-only">结束日期</span>
              <span class="date-input" :class="{ 'has-value': to }" data-placeholder="结束日期">
                <input v-model="to" type="date" aria-label="结束日期" @change="load" />
              </span>
            </label>
          </fieldset>
          <label class="filter-field"
            ><span>分类</span
            ><select v-model="category" @change="load">
              <option value="">全部</option>
              <option value="trip">行程旅行</option>
              <option value="activity">活动纪实</option>
              <option value="project">项目过程</option>
              <option value="challenge">目标挑战</option>
              <option value="lifecycle">成长陪伴</option>
              <option value="series">主题系列</option>
              <option value="life-period">生活阶段</option>
              <option value="other">其他</option>
            </select></label
          >
          <button
            v-if="status !== '' || category || from || to"
            class="clear-filter"
            type="button"
            @click="clearFilters"
          >
            清除筛选
          </button>
        </div>
      </section>
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
        <h2>还没有故事线</h2>
        <p>可以从一次旅行、一个项目或一组主题记录开始。</p>
        <RouterLink class="button button-primary" to="/storylines/new">创建第一条故事线</RouterLink>
      </section>
    </main>
  </div>
</template>

<style scoped>
.story-list-page {
  width: min(1180px, calc(100% - 48px));
  margin: auto;
  padding: 56px 0 88px;
}
.story-list-heading {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: 32px;
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
.story-filter {
  padding: 18px 20px;
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  background: var(--surface);
}
.story-filter__heading {
  margin-bottom: 16px;
  display: flex;
  align-items: center;
  gap: 10px;
}
.story-filter__heading .ui-icon {
  width: 20px;
  height: 20px;
  color: var(--primary);
}
.story-filter__heading strong,
.story-filter__heading small {
  display: block;
}
.story-filter__heading strong {
  font-size: 14px;
}
.story-filter__heading small {
  margin-top: 2px;
  color: var(--ink-tertiary);
  font-size: 10px;
}
.story-filter__controls {
  display: grid;
  grid-template-columns: minmax(8rem, 0.7fr) minmax(20rem, 2fr) minmax(10rem, 1fr) auto;
  align-items: end;
  gap: clamp(8px, 1vw, 16px);
}
.filter-field {
  min-width: 0;
  display: grid;
  gap: 6px;
  color: var(--ink-secondary);
  font-size: 11px;
  font-weight: 700;
}
.date-range {
  min-width: 0;
  margin: 0;
  padding: 0;
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  align-items: end;
  border: 0;
}
.date-range legend {
  grid-column: 1 / -1;
  margin-bottom: 6px;
  color: var(--ink-secondary);
  font-size: 11px;
  font-weight: 700;
}
.date-range > label,
.date-range > span {
  display: flex;
  align-items: center;
}
.date-range > label {
  min-width: 0;
}
.date-range > span {
  width: clamp(28px, 3vw, 40px);
  justify-content: center;
  color: var(--ink-tertiary);
  font-size: 11px;
}
.story-filter select,
.story-filter input {
  width: 100%;
  min-width: 0;
  min-height: 44px;
  padding: 0 12px;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  color: var(--ink);
  background: var(--surface-soft);
}
.story-filter input[type='date'] {
  padding-right: 9px;
  font-variant-numeric: tabular-nums;
}
.date-input {
  width: 100%;
  min-width: 0;
  position: relative;
}
.date-input::before {
  content: attr(data-placeholder);
  position: absolute;
  z-index: 1;
  top: 50%;
  left: 13px;
  transform: translateY(-50%);
  pointer-events: none;
  color: var(--ink-tertiary);
  font-size: 12px;
}
.date-input.has-value::before,
.date-input:focus-within::before {
  content: none;
}
.date-input:not(.has-value):not(:focus-within) input::-webkit-datetime-edit {
  color: transparent;
}
.story-filter input[type='date']::-webkit-calendar-picker-indicator {
  cursor: pointer;
  opacity: 0.72;
  filter: var(--calendar-icon-filter, none);
}
.clear-filter {
  min-height: 44px;
  padding: 0 10px;
  border: 0;
  border-radius: var(--radius-sm);
  color: var(--primary-strong);
  background: transparent;
  white-space: nowrap;
}
.clear-filter:hover {
  background: var(--primary-soft);
}
.story-groups > div {
  margin-top: 42px;
}
.story-groups h2 {
  font-size: 18px;
}
.story-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 18px;
}
.story-card {
  min-height: 230px;
  display: grid;
  grid-template-columns: 160px 1fr;
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
  display: grid;
  place-items: center;
  background: linear-gradient(145deg, var(--primary-soft), var(--surface-tint));
  color: var(--primary-strong);
  font-size: 52px;
  font-weight: 800;
}
.story-cover img {
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
  padding: 22px;
  display: flex;
  flex-direction: column;
}
.story-meta {
  margin: 0;
  display: flex;
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
  gap: 5px;
}
.story-card footer {
  margin-top: auto;
  padding-top: 18px;
  display: flex;
  justify-content: space-between;
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
  .story-list-page {
    width: calc(100% - 32px);
    padding-top: 36px;
  }
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
  .story-card {
    grid-template-columns: 110px 1fr;
  }
  .story-filter__controls {
    grid-template-columns: 1fr;
  }
  .clear-filter {
    justify-self: start;
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
