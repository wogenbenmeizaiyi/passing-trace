<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { subjectsApi, type Subject, type SubjectField, type SubjectValue } from '@/api/subjects'
import WebAppHeader from '@/components/WebAppHeader.vue'
import SubjectFields from './SubjectFields.vue'
import SubjectAvatar from './SubjectAvatar.vue'
import SubjectMediaEditor from './SubjectMediaEditor.vue'
import { defaultTimezone, toDatetimeLocal, toIsoWithOffset } from '@/utils/datetime'
import './subjects.css'
const route = useRoute(),
  router = useRouter()
const id = computed(() => route.params.id as string | undefined)
const current = ref<Subject | null>(null),
  subjects = ref<Subject[]>([]),
  kind = ref(0),
  itemType = ref('vehicle'),
  name = ref(''),
  description = ref(''),
  startedAt = ref(''),
  effectiveAt = ref('')
const fields = ref<SubjectField[]>([]),
  values = ref<Record<string, SubjectValue>>({}),
  mediaIds = ref<string[]>([]),
  coverMediaId = ref(''),
  target = ref(''),
  relationName = ref('关联'),
  directed = ref(false)
const error = ref(''),
  busy = ref(false),
  ready = ref(false),
  mediaEditor = ref<InstanceType<typeof SubjectMediaEditor> | null>(null)
const key = crypto.randomUUID(),
  timezone = ref(defaultTimezone())
const originalValues = ref<Record<string, SubjectValue>>({})
async function presets() {
  if (!id.value) {
    fields.value = await subjectsApi.presets(
      kind.value,
      kind.value === 2 ? itemType.value : undefined,
    )
    values.value = {}
  }
}
watch([kind, itemType], () => {
  if (ready.value) void presets().catch((e) => (error.value = String(e)))
})
onMounted(async () => {
  try {
    subjects.value = await subjectsApi.list()
    target.value = subjects.value.find((x) => x.isSelf)?.id ?? ''
    if (id.value) {
      current.value = await subjectsApi.get(id.value)
      const c = current.value
      name.value = c.name
      description.value = c.description ?? ''
      kind.value = c.kind
      itemType.value = c.itemType ?? 'other'
      fields.value = c.fields.map((x) => ({
        ...x,
        options: x.options ? [...x.options] : x.options,
      }))
      values.value = { ...c.values }
      originalValues.value = { ...c.values }
      mediaIds.value = [...c.mediaIds]
      coverMediaId.value = c.coverMediaId ?? ''
      startedAt.value = toDatetimeLocal(c.startedAt)
    } else await presets()
    ready.value = true
  } catch (e) {
    error.value = e instanceof Error ? e.message : '加载失败'
  }
})
async function submit() {
  if (busy.value || mediaEditor.value?.busy || !ready.value) return
  busy.value = true
  error.value = ''
  try {
    const changed = Object.fromEntries(
      Object.entries(values.value).filter(
        ([key, v]) =>
          fields.value.some((f) => f.id === key && !f.removed) &&
          (!current.value || originalValues.value[key] !== v),
      ),
    )
    const shared = {
      description: description.value,
      fields: fields.value,
      values: changed,
      mediaIds: mediaIds.value,
      coverMediaId: coverMediaId.value || undefined,
      clearCover: !coverMediaId.value,
    }
    const result = current.value
      ? await subjectsApi.update(
          current.value.id,
          {
            ...shared,
            name: current.value.isSelf ? undefined : name.value,
            effectiveAt: effectiveAt.value
              ? toIsoWithOffset(effectiveAt.value, timezone.value)
              : undefined,
          },
          current.value.version,
        )
      : await subjectsApi.create(
          {
            ...shared,
            kind: kind.value,
            name: name.value,
            itemType: kind.value === 2 ? itemType.value : null,
            relations: [
              { toSubjectId: target.value, label: relationName.value, directed: directed.value },
            ],
            timezone: timezone.value,
            startedAt: startedAt.value ? toIsoWithOffset(startedAt.value, timezone.value) : null,
          },
          key,
        )
    await router.replace(`/subjects/${result.id}`)
  } catch (e) {
    error.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    busy.value = false
  }
}
</script>
<template>
  <div class="app-shell">
    <WebAppHeader />
    <main class="workspace-main subject-page">
      <RouterLink :to="id ? `/subjects/${id}` : '/subjects'">← 返回人物</RouterLink>
      <h1>{{ id ? '编辑资料' : '新建人物档案' }}</h1>
      <p v-if="error" class="error-banner" role="alert">{{ error }}</p>
      <p v-if="!ready && !error" role="status">正在加载档案…</p>
      <form v-if="ready" class="subject-form" @submit.prevent="submit">
        <section class="subject-panel">
          <h2>基本信息</h2>
          <div v-if="current" class="subject-profile">
            <SubjectAvatar :subject="current" />
            <p class="subject-muted">
              {{
                current.isSelf ? '自己的昵称和头像沿用账号资料。' : '可在下方媒体区域设置头像图片。'
              }}
            </p>
          </div>
          <label v-if="!current?.isSelf"
            >名称<input v-model="name" required maxlength="200"
          /></label>
          <template v-if="!id">
            <label
              >类型<select v-model="kind">
                <option :value="0">人</option>
                <option :value="1">宠物</option>
                <option :value="2">物品</option>
              </select></label
            >
            <label v-if="kind === 2"
              >物品分类<select v-model="itemType">
                <option value="vehicle">车辆</option>
                <option value="property">房屋</option>
                <option value="bicycle">自行车</option>
                <option value="collectible">收藏品</option>
                <option value="other">其他</option>
              </select></label
            >
            <label>起始日期（可不填写）<input v-model="startedAt" type="datetime-local" /></label>
          </template>
          <label>说明<textarea v-model="description" /></label>
        </section>
        <fieldset v-if="!id" class="subject-panel">
          <legend>关联已有档案</legend>
          <p class="subject-muted">选择与新档案关联的人物、宠物或物品。</p>
          <label
            >关联档案<select v-model="target" required>
              <option v-for="s in subjects" :key="s.id" :value="s.id">
                {{ s.isSelf ? '自己' : s.name }}
              </option>
            </select></label
          >
          <label>关系名称<input v-model="relationName" required maxlength="100" /></label>
          <label><input v-model="directed" type="checkbox" />从新档案指向所选档案</label>
        </fieldset>
        <SubjectFields
          v-model="values"
          :fields="fields"
          allow-definitions
          @definitions="fields = $event"
        />
        <section v-if="id" class="subject-panel">
          <label
            >字段变化实际时间（留空为现在）<input v-model="effectiveAt" type="datetime-local"
          /></label>
          <p class="subject-muted">只保存本次修改的值，并加入该档案的时间轴。</p>
        </section>
        <SubjectMediaEditor ref="mediaEditor" v-model="mediaIds" />
        <section v-if="!current?.isSelf" class="subject-panel">
          <h2>头像图片</h2>
          <label
            >选择头像<select v-model="coverMediaId">
              <option value="">不设置</option>
              <option v-if="coverMediaId && !mediaIds.includes(coverMediaId)" :value="coverMediaId">
                保留当前头像
              </option>
              <option v-for="(m, index) in mediaIds" :key="m" :value="m">
                附件 {{ index + 1 }}
              </option>
            </select></label
          >
          <p class="subject-muted">先上传图片，再选择用作头像；视频与三维模型仅作为附件展示。</p>
        </section>
        <footer class="subject-form-footer">
          <RouterLink class="button" :to="id ? `/subjects/${id}` : '/subjects'">取消</RouterLink>
          <button class="button button-primary" :disabled="busy || mediaEditor?.busy">
            {{ busy ? '保存中…' : '保存档案' }}
          </button>
        </footer>
      </form>
    </main>
  </div>
</template>
