<script setup lang="ts">
import { RouterLink } from 'vue-router'

import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
</script>

<template>
  <div class="entry-page">
    <header class="entry-header">
      <RouterLink class="brand" to="/" aria-label="PassingTrace 首页">
        <span class="brand-mark">P</span><span>PassingTrace</span>
      </RouterLink>
      <button
        v-if="auth.isAuthenticated"
        class="text-button"
        :disabled="auth.busy"
        @click="auth.logout"
      >
        退出
      </button>
    </header>

    <main class="entry-main">
      <section class="entry-card" aria-labelledby="entry-title">
        <span class="entry-mark" aria-hidden="true">P</span>
        <p class="entry-kicker">PASSINGTRACE</p>
        <h1 id="entry-title">{{ auth.isAuthenticated ? '我的记录' : '登录 PassingTrace' }}</h1>
        <p class="entry-description">
          {{
            auth.isAuthenticated ? `已登录为 ${auth.username}` : '使用手机扫码确认后即可查看记录。'
          }}
        </p>

        <RouterLink
          v-if="auth.isAuthenticated"
          class="button button-accent entry-action"
          to="/events"
        >
          查看我的记录
        </RouterLink>
        <template v-else>
          <button
            class="button button-accent entry-action"
            :disabled="auth.busy"
            @click="auth.login"
          >
            {{ auth.busy ? '正在打开…' : '手机扫码登录' }}
          </button>
          <RouterLink class="entry-record-link" to="/events">查看我的记录</RouterLink>
        </template>

        <p v-if="auth.error" class="entry-error" role="alert">
          {{ auth.error }}
          <button class="text-button" @click="auth.clearError">关闭</button>
        </p>
      </section>
    </main>
  </div>
</template>

<style scoped>
.entry-page {
  min-height: 100vh;
  min-height: 100dvh;
  background: var(--paper, #f4efe4);
  color: var(--ink, #25211d);
}

.entry-header {
  width: min(1120px, calc(100% - 40px));
  min-height: 72px;
  margin: 0 auto;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid rgba(37, 33, 29, 0.14);
}

.entry-main {
  min-height: calc(100dvh - 72px);
  display: grid;
  place-items: center;
  padding: 48px 20px 96px;
}

.entry-card {
  width: min(100%, 440px);
  padding: 48px;
  text-align: center;
  background: rgba(255, 253, 248, 0.72);
  border: 1px solid rgba(37, 33, 29, 0.14);
}

.entry-mark {
  width: 56px;
  height: 56px;
  margin: 0 auto 24px;
  display: grid;
  place-items: center;
  background: var(--accent, #d9483b);
  color: white;
  font:
    600 28px/1 Georgia,
    serif;
}

.entry-kicker {
  margin: 0 0 12px;
  color: var(--accent, #b83c32);
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.18em;
}

.entry-card h1 {
  margin: 0;
  font:
    600 clamp(30px, 5vw, 42px) / 1.2 Georgia,
    'Noto Serif SC',
    serif;
}

.entry-description {
  margin: 16px 0 32px;
  line-height: 1.7;
  color: rgba(37, 33, 29, 0.7);
}

.entry-action {
  width: 100%;
  min-height: 48px;
  justify-content: center;
}

.entry-record-link {
  min-height: 44px;
  margin-top: 12px;
  display: inline-flex;
  align-items: center;
  color: inherit;
  text-underline-offset: 4px;
}

.entry-error {
  margin: 20px 0 0;
  color: var(--accent, #b83c32);
  line-height: 1.6;
}

@media (max-width: 560px) {
  .entry-header {
    width: calc(100% - 32px);
  }

  .entry-main {
    padding: 32px 16px 72px;
  }

  .entry-card {
    padding: 36px 24px;
  }
}
</style>
