<script setup lang="ts">
import { RouterLink } from 'vue-router'

import BrandMark from '@/components/BrandMark.vue'
import AppearanceMenu from '@/components/AppearanceMenu.vue'
import { useAuthStore } from '@/stores/auth'

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

function login() {
  const destination = props.variant === 'marketing' ? '/events' : window.location.pathname
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
      <RouterLink class="site-brand" to="/" aria-label="星期八产品首页">
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
        ><RouterLink to="/assistant">问问 AI</RouterLink>
      </nav>

      <div class="site-header__actions">
        <AppearanceMenu />
        <template v-if="auth.isAuthenticated">
          <span class="signed-user" :title="auth.username"
            ><i aria-hidden="true"></i><span>{{ auth.username || '已登录' }}</span></span
          >
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
.site-nav a[aria-disabled='true'] {
  pointer-events: none;
  opacity: 0.55;
}
</style>
