<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink, onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import WebAppHeader from '@/components/WebAppHeader.vue'
import AccountAvatar from '@/components/AccountAvatar.vue'
import AvatarCropper from '@/components/AvatarCropper.vue'
import { useAuthStore } from '@/stores/auth'
import { useProfileStore } from '@/stores/profile'
import { HttpError } from '@/api/http-client'
import { profileError } from '@/api/profile'
import { accountReturnPath, profileTextLength } from '@/utils/avatar-crop'

const auth = useAuthStore(),
  account = useProfileStore(),
  route = useRoute(),
  router = useRouter()
const editing = ref(false),
  loading = ref(false),
  saving = ref(false),
  conflict = ref(false)
const error = ref(''),
  success = ref(''),
  nickname = ref(''),
  bio = ref(''),
  version = ref('')
const originalNickname = ref(''),
  originalBio = ref('')
const avatar = ref<Blob | null>(null),
  preview = ref(''),
  remove = ref(false),
  cropFile = ref<File | null>(null)
const input = ref<HTMLInputElement | null>(null)
const errorNotice = ref<HTMLElement | null>(null)
watch(error, async (message) => {
  if (message) {
    await nextTick()
    errorNotice.value?.scrollIntoView?.({ block: 'nearest' })
  }
})
const dirty = computed(
  () =>
    editing.value &&
    (nickname.value !== originalNickname.value ||
      bio.value !== originalBio.value ||
      avatar.value !== null ||
      remove.value),
)
const nicknameError = computed(() =>
  profileTextLength(nickname.value.trim()) < 1 ||
  profileTextLength(nickname.value.trim()) > 24 ||
  [...nickname.value].some((character) => {
    const code = character.charCodeAt(0)
    return code < 32 || (code >= 127 && code <= 159)
  })
    ? '昵称请填写 1～24 个字，不含换行。'
    : '',
)
const bioError = computed(() =>
  profileTextLength(bio.value.trim()) > 100 ? '个人简介最多 100 个字。' : '',
)
const touched = ref(false)
const joined = computed(() =>
  account.profile ? new Date(account.profile.createdAt).toLocaleDateString('zh-CN') : '',
)
const back = computed(() => accountReturnPath(route.query.from))
function clearPreview() {
  if (preview.value) URL.revokeObjectURL(preview.value)
  preview.value = ''
  avatar.value = null
  remove.value = false
}
function populate() {
  const profile = account.profile
  if (!profile) return
  nickname.value = originalNickname.value = profile.nickname
  bio.value = originalBio.value = profile.bio
  version.value = profile.version
  clearPreview()
  touched.value = false
  conflict.value = false
}
async function load() {
  loading.value = true
  error.value = ''
  try {
    await account.refresh(true)
    if (!editing.value) populate()
  } catch (reason) {
    error.value = profileError(reason)
  } finally {
    loading.value = false
  }
}
function startEdit() {
  populate()
  success.value = ''
  error.value = ''
  editing.value = true
}
function cancelEdit() {
  if (dirty.value && !window.confirm('放弃尚未保存的修改吗？')) return
  clearPreview()
  editing.value = false
  error.value = ''
  conflict.value = false
}
async function reloadConflict() {
  if (!window.confirm('重新加载会放弃本次未保存的修改，继续吗？')) return
  editing.value = false
  await load()
}
function choose(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (input.value) input.value.value = ''
  if (!file) return
  error.value = ''
  if (file.size > 5 * 1024 * 1024) {
    error.value = '头像不能超过 5MB，请选择较小的图片。'
    return
  }
  if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
    error.value = '请选择 JPG、PNG 或 WebP 图片。'
    return
  }
  cropFile.value = file
}
function cropped(blob: Blob) {
  clearPreview()
  avatar.value = blob
  preview.value = URL.createObjectURL(blob)
  cropFile.value = null
}
function resetAvatar() {
  clearPreview()
  remove.value = true
}
async function save() {
  touched.value = true
  if (saving.value || !dirty.value || nicknameError.value || bioError.value) return
  saving.value = true
  error.value = ''
  conflict.value = false
  try {
    await account.save(nickname.value, bio.value, version.value, avatar.value, remove.value)
    editing.value = false
    clearPreview()
    success.value = '个人资料已保存。'
  } catch (reason) {
    conflict.value = reason instanceof HttpError && reason.status === 409
    error.value = profileError(reason)
  } finally {
    saving.value = false
  }
}
function leave(event: BeforeUnloadEvent) {
  if (dirty.value || saving.value) {
    event.preventDefault()
    event.returnValue = ''
  }
}
onBeforeRouteLeave(() =>
  saving.value ? false : !dirty.value || window.confirm('资料尚未保存，确定离开吗？'),
)
onMounted(() => {
  if (auth.isAuthenticated) void load()
  window.addEventListener('beforeunload', leave)
})
onBeforeUnmount(() => {
  clearPreview()
  window.removeEventListener('beforeunload', leave)
})
</script>

<template>
  <WebAppHeader />
  <main class="account-page">
    <header class="account-heading">
      <button class="text-button" @click="router.push(back)">← 返回原页面</button>
      <h1>用户中心</h1>
      <p>你的个人资料，仅自己可见。</p>
    </header>
    <section v-if="!auth.isAuthenticated" class="account-panel">
      <p>登录后可以查看和编辑个人资料。</p>
      <button class="button button-primary" @click="auth.login(route.fullPath)">扫码登录</button>
    </section>
    <template v-else>
      <p v-if="error" ref="errorNotice" class="account-error" role="alert">
        {{ error }}
        <button v-if="conflict" class="text-button" :disabled="saving" @click="reloadConflict">
          重新加载资料</button
        ><button v-else-if="!account.profile" class="text-button" @click="load">重试</button>
      </p>
      <p v-if="success" role="status" class="account-success">{{ success }}</p>
      <p v-if="loading && !account.profile" role="status">正在加载个人资料…</p>
      <div v-if="account.profile" class="account-layout">
        <div class="account-sidebar">
          <aside class="account-panel profile-summary">
            <AccountAvatar
              :src="editing ? (remove ? '' : preview || account.avatarUrl) : account.avatarUrl"
              :size="112"
            />
            <h2>{{ account.nickname }}</h2>
            <p class="profile-bio">{{ account.profile.bio || '在这里，慢慢收集属于你的生活。' }}</p>
            <span class="profile-private">仅本人可见</span>
            <button
              v-if="!editing"
              class="button button-primary"
              :disabled="loading"
              @click="startEdit"
            >
              编辑个人资料
            </button>
            <template v-else>
              <input
                ref="input"
                class="visually-hidden"
                type="file"
                accept="image/jpeg,image/png,image/webp"
                tabindex="-1"
                aria-label="选择头像图片"
                :disabled="saving"
                @change="choose"
              />
              <button class="button button-secondary" :disabled="saving" @click="input?.click()">
                更换头像
              </button>
              <button
                class="text-button"
                :disabled="saving || (!account.profile.hasAvatar && !avatar) || remove"
                @click="resetAvatar"
              >
                恢复默认头像
              </button>
              <small>JPG、PNG、WebP · 不超过 5MB<br />裁剪后预览，保存修改后生效</small>
            </template>
          </aside>
          <section class="account-companion" aria-label="手机版">
            <RouterLink to="/product" class="account-companion__link">
              <svg
                class="account-companion__icon"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="1.6"
                aria-hidden="true"
              >
                <rect x="6.5" y="2.5" width="11" height="19" rx="2.5" />
                <path d="M10 5h4M11 18.5h2" stroke-linecap="round" />
              </svg>
              <span class="account-companion__copy">
                <strong>在手机上使用</strong>
                <small>了解与下载手机 App</small>
              </span>
              <svg
                class="account-companion__arrow"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="1.6"
                aria-hidden="true"
              >
                <path d="m9 6 6 6-6 6" stroke-linecap="round" stroke-linejoin="round" />
              </svg>
            </RouterLink>
          </section>
        </div>
        <section class="account-panel profile-details">
          <template v-if="!editing">
            <h2>账号资料</h2>
            <dl>
              <dt>昵称</dt>
              <dd>{{ account.profile.nickname }}</dd>
              <dt>个人简介</dt>
              <dd class="profile-bio">{{ account.profile.bio || '还没有填写简介' }}</dd>
              <dt>登录用户名</dt>
              <dd>{{ account.profile.username }}<small>用于登录，修改昵称不会改变它</small></dd>
              <dt>加入星期八</dt>
              <dd>{{ joined }}</dd>
            </dl>
            <div class="account-note">头像和简介不会作为记录保存，也不会自动加入 AI 记忆。</div>
          </template>
          <form v-else @submit.prevent="save">
            <h2>编辑个人资料</h2>
            <fieldset :disabled="saving">
              <label for="profile-nickname">昵称 <span>必填</span></label
              ><input
                id="profile-nickname"
                v-model="nickname"
                autocomplete="nickname"
                placeholder="填写你喜欢的称呼"
                :aria-invalid="touched && !!nicknameError"
                aria-describedby="nickname-help"
                @blur="touched = true"
              />
              <small id="nickname-help" :class="{ invalid: touched && nicknameError }">{{
                touched && nicknameError ? nicknameError : '1～24 个字，可以随时修改，不影响登录'
              }}</small>
              <label for="profile-bio">个人简介 <span>选填</span></label
              ><textarea
                id="profile-bio"
                v-model="bio"
                rows="4"
                placeholder="简单介绍一下自己"
                :aria-invalid="!!bioError"
                aria-describedby="bio-help"
              />
              <small id="bio-help" :class="{ invalid: bioError }">{{
                bioError || `${profileTextLength(bio)} / 100`
              }}</small>
              <div class="account-readonly">
                <span>登录用户名</span><strong>{{ account.profile.username }}</strong
                ><small>仅供查看，不能在这里修改</small>
              </div>
              <div class="account-readonly">
                <span>加入时间</span><strong>{{ joined }}</strong>
              </div>
            </fieldset>
            <footer>
              <button
                class="button button-secondary"
                type="button"
                :disabled="saving"
                @click="cancelEdit"
              >
                取消</button
              ><button
                class="button button-primary"
                :disabled="saving || !dirty || !!bioError || !!nicknameError || conflict"
              >
                {{ saving ? '正在保存…' : '保存修改' }}
              </button>
            </footer>
          </form>
        </section>
      </div>
    </template>
    <AvatarCropper v-if="cropFile" :file="cropFile" @cancel="cropFile = null" @confirm="cropped" />
  </main>
</template>

<style scoped>
.account-page {
  width: 100%;
  padding: clamp(20px, 3vw, 56px);
  color: var(--ink);
}
.account-heading {
  margin-bottom: 28px;
}
h1 {
  margin: 14px 0 6px;
  font-size: clamp(28px, 3vw, 44px);
}
.account-heading p {
  margin: 0;
  color: var(--ink-secondary);
}
.account-layout {
  display: grid;
  grid-template-columns: minmax(240px, 0.8fr) minmax(0, 2fr);
  align-items: start;
  gap: clamp(20px, 3vw, 48px);
}
.account-panel {
  min-width: 0;
  padding: clamp(24px, 3vw, 48px);
  border: 1px solid var(--line);
  border-radius: 24px;
  background: var(--surface);
}
.account-sidebar {
  display: grid;
  gap: 16px;
  min-width: 0;
}
.account-companion__link {
  display: flex;
  align-items: center;
  gap: 14px;
  min-height: 72px;
  padding: 18px 22px;
  border: 1px solid var(--line);
  border-radius: 18px;
  color: var(--ink);
  text-decoration: none;
}
.account-companion__link:hover {
  background: var(--surface-soft);
  border-color: var(--line-strong);
}
.account-companion__link:focus-visible {
  outline: 2px solid var(--primary);
  outline-offset: 4px;
}
.account-companion__icon {
  flex: 0 0 26px;
  height: 26px;
  color: var(--primary-strong);
}
.account-companion__copy {
  flex: 1;
  min-width: 0;
  overflow-wrap: anywhere;
}
.account-companion__copy strong {
  display: block;
  margin-bottom: 4px;
  font-size: 15px;
  font-weight: 600;
}
.account-companion__arrow {
  flex: 0 0 18px;
  height: 18px;
  color: var(--ink-secondary);
}
.profile-summary {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 18px;
  text-align: center;
}
.profile-summary h2 {
  margin: 0;
  overflow-wrap: anywhere;
}
.profile-summary p {
  margin: 0;
  color: var(--ink-secondary);
}
.profile-private {
  font-size: 13px;
  color: var(--primary-strong);
  background: var(--primary-soft);
  padding: 5px 12px;
  border-radius: 20px;
}
.profile-bio {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
small {
  display: block;
  font-size: 13px;
  color: var(--ink-secondary);
}
h2 {
  margin: 0 0 24px;
}
dl {
  display: grid;
  grid-template-columns: minmax(100px, 0.5fr) minmax(0, 2fr);
  gap: 24px 16px;
  margin: 0;
}
dt {
  color: var(--ink-secondary);
}
dd {
  margin: 0;
  overflow-wrap: anywhere;
}
dd small {
  margin-top: 5px;
}
.account-note {
  margin-top: 36px;
  padding-top: 24px;
  border-top: 1px solid var(--line);
  color: var(--ink-secondary);
}
fieldset {
  border: 0;
  padding: 0;
  margin: 0;
  min-width: 0;
}
label {
  display: flex;
  justify-content: space-between;
  margin-top: 24px;
  margin-bottom: 8px;
  font-weight: 600;
}
label span {
  color: var(--ink-secondary);
  font-size: 13px;
  font-weight: 400;
}
input:not([type='file']),
textarea {
  box-sizing: border-box;
  width: 100%;
  min-height: 48px;
  border: 1px solid var(--line-strong);
  border-radius: 12px;
  background: var(--surface-soft);
  color: var(--ink);
  padding: 12px 16px;
  font: inherit;
}
textarea {
  resize: vertical;
}
input:focus,
textarea:focus {
  outline: 2px solid var(--primary);
  outline-offset: 2px;
}
fieldset small {
  margin-top: 7px;
}
.account-readonly {
  display: grid;
  gap: 6px;
  padding-top: 24px;
  margin-top: 24px;
  border-top: 1px solid var(--line);
}
.account-readonly span {
  color: var(--ink-secondary);
}
.account-readonly strong {
  font-weight: 500;
  overflow-wrap: anywhere;
}
footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  margin-top: 32px;
}
.account-error,
.account-success {
  padding: 16px;
  border: 1px solid var(--line-strong);
  border-radius: 12px;
}
.account-error,
.invalid {
  color: var(--danger);
}
.account-success {
  color: var(--primary-strong);
  background: var(--primary-soft);
}
.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip-path: inset(50%);
}
@media (max-width: 760px) {
  .account-layout {
    grid-template-columns: 1fr;
  }
  .account-panel {
    padding: 24px;
  }
  dl {
    grid-template-columns: 1fr;
    gap: 6px;
  }
  dd {
    margin-bottom: 20px;
  }
  footer > button {
    flex: 1;
  }
}
</style>
