<script setup lang="ts">
import { RouterLink, useRoute } from 'vue-router'
import AccountAvatar from '@/components/AccountAvatar.vue'
import { useProfileStore } from '@/stores/profile'
import { useSocialStore } from '@/stores/social'

import BrandMark from '@/components/BrandMark.vue'
import { useAuthStore } from '@/stores/auth'
import { defaultWebEntry } from '@/utils/device'

const props = withDefaults(
  defineProps<{ variant?: 'marketing' | 'app'; downloadUrl?: string; downloadBusy?: boolean }>(),
  {
    variant: 'app',
    downloadUrl: '',
    downloadBusy: false,
  },
)
const emit = defineEmits<{ download: [] }>()

const auth = useAuthStore()
const account = useProfileStore()
const social = useSocialStore()
const route = useRoute()
const navigation = [
  {
    to: '/subjects',
    label: '人物',
    path: 'M9 3a4 4 0 1 1 0 8 4 4 0 0 1 0-8ZM2 21v-2a7 7 0 0 1 14 0v2M16 3a4 4 0 0 1 0 8M22 21v-2a7 7 0 0 0-4-6',
  },
  {
    to: '/events',
    label: '我的记录',
    path: 'M6 3h13v18H6a3 3 0 0 1-3-3V6a3 3 0 0 1 3-3Zm0 0v18M9 8h7M9 12h7M9 16h4',
  },
  {
    to: '/storylines',
    label: '故事线',
    path: 'M8 6h5a5 5 0 0 1 5 5v5M6 8v8M8 18h8M8 6a2 2 0 1 1-4 0 2 2 0 0 1 4 0ZM8 18a2 2 0 1 1-4 0 2 2 0 0 1 4 0ZM20 18a2 2 0 1 1-4 0 2 2 0 0 1 4 0Z',
  },
  {
    to: '/assistant',
    label: '问问 AI',
    path: 'm12 3 2.5 6.5L21 12l-6.5 2.5L12 21l-2.5-6.5L3 12l6.5-2.5L12 3Z',
  },
  { to: '/messages', label: '消息', path: 'M4 4h16v13H9l-5 4V4Zm4 5h8M8 13h5' },
]

function login() {
  const destination = props.variant === 'marketing' ? defaultWebEntry() : window.location.pathname
  void auth.login(destination)
}

function download(event: MouseEvent) {
  event.preventDefault()
  if (!props.downloadBusy) emit('download')
}
</script>

<template>
  <header class="site-header" :class="`site-header--${variant}`">
    <div class="site-header__inner">
      <RouterLink
        class="site-brand"
        to="/product"
        aria-label="星期八产品介绍"
        title="星期八 · 产品介绍"
      >
        <span class="site-brand__mark"><BrandMark /></span>
        <span class="site-brand__copy"><strong>星期八</strong><small>把生活收进记忆盒</small></span>
      </RouterLink>

      <nav v-if="variant === 'marketing'" class="site-nav" aria-label="产品导航">
        <a href="#features">产品能力</a><a href="#storylines">故事线</a
        ><a href="#assistant">问问 AI</a><a href="#privacy">隐私</a>
        <a v-if="downloadUrl" :href="downloadUrl" :aria-disabled="downloadBusy" @click="download">{{
          downloadBusy ? '正在准备…' : '下载'
        }}</a>
      </nav>
      <nav v-else class="site-nav site-nav--app" aria-label="应用导航">
        <RouterLink
          v-for="item in navigation"
          :key="item.to"
          :to="item.to"
          :aria-label="item.label"
        >
          <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true"><path :d="item.path" /></svg>
          <span class="nav-tooltip">{{ item.label }}</span>
          <span v-if="item.to === '/messages' && social.unread" class="message-badge">{{
            social.unread > 99 ? '99+' : social.unread
          }}</span></RouterLink
        >
      </nav>

      <div class="site-header__actions">
        <RouterLink
          v-if="variant === 'marketing'"
          class="button button-primary button-compact"
          :to="defaultWebEntry()"
          >进入应用</RouterLink
        >
        <template v-else-if="auth.isAuthenticated">
          <RouterLink
            class="account-entry"
            :to="{
              path: '/account',
              query: route.path === '/account' ? route.query : { from: route.fullPath },
            }"
            aria-label="进入用户中心"
            :title="account.nickname"
          >
            <AccountAvatar :src="account.avatarUrl" /><span>{{ account.nickname }}</span>
          </RouterLink>
        </template>
        <button
          v-else
          class="button button-secondary button-compact"
          :disabled="auth.busy"
          @click="login"
        >
          {{ auth.busy ? '正在打开…' : '登录' }}
        </button>
      </div>
    </div>
  </header>
</template>

<style scoped>
.message-badge {
  position: absolute;
  left: calc(100% - 2px);
  top: 0;
  padding: 0.1rem 0.4rem;
  border-radius: 1rem;
  background: var(--primary);
  color: var(--on-primary);
  font-size: 0.75rem;
}
.app-download-link {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 44px;
  padding-inline: 2px;
  color: var(--ink-secondary);
  font-size: 13px;
  white-space: nowrap;
  text-decoration: none;
}
.app-download-link:hover {
  color: var(--primary-strong);
  text-decoration: underline;
  text-underline-offset: 4px;
}
.app-download-link:focus-visible {
  outline: 2px solid var(--primary);
  outline-offset: 3px;
  border-radius: 4px;
}
.account-entry {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  min-height: 44px;
  text-decoration: none;
  color: var(--ink);
}
.account-entry > span:last-child {
  max-width: 9em;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
@media (max-width: 760px) {
  .account-entry > span:last-child {
    display: none;
  }
}
.site-nav a[aria-disabled='true'] {
  pointer-events: none;
  opacity: 0.55;
}
</style>
