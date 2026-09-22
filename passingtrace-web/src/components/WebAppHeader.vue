<script setup lang="ts">
import { RouterLink, useRoute } from 'vue-router'
import AccountAvatar from '@/components/AccountAvatar.vue'
import { useProfileStore } from '@/stores/profile'
import { useSocialStore } from '@/stores/social'

import BrandMark from '@/components/BrandMark.vue'
import AppearanceMenu from '@/components/AppearanceMenu.vue'
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
        :to="variant === 'marketing' ? '/product' : '/'"
        aria-label="星期八首页"
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
        <RouterLink to="/events">我的记录</RouterLink
        ><RouterLink to="/storylines">故事线</RouterLink
        ><RouterLink to="/assistant">问问 AI</RouterLink
        ><RouterLink to="/messages"
          >消息<span v-if="social.unread" class="message-badge">{{
            social.unread > 99 ? '99+' : social.unread
          }}</span></RouterLink
        >
      </nav>

      <div class="site-header__actions">
        <AppearanceMenu />
        <RouterLink
          v-if="variant === 'app' && !auth.isAuthenticated"
          class="app-download-link"
          to="/product"
        >
          下载 App
        </RouterLink>
        <template v-if="auth.isAuthenticated">
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
          <RouterLink
            v-if="variant === 'marketing'"
            class="button button-primary button-compact"
            to="/events"
            >进入应用</RouterLink
          >
          <button v-else class="text-button" :disabled="auth.busy" @click="auth.logout">
            退出
          </button>
        </template>
        <button
          v-else
          class="button button-secondary button-compact"
          :disabled="auth.busy"
          @click="login"
        >
          {{ auth.busy ? '正在打开…' : '扫码登录' }}
        </button>
      </div>
    </div>
  </header>
</template>

<style scoped>
.message-badge {
  margin-left: 0.4rem;
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
  .site-nav--app {
    gap: 0.5rem;
    overflow-x: auto;
  }
  .site-nav--app a {
    white-space: nowrap;
  }
  .account-entry > span:last-child {
    display: none;
  }
}
.site-nav a[aria-disabled='true'] {
  pointer-events: none;
  opacity: 0.55;
}
</style>
