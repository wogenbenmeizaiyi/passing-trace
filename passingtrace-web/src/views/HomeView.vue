<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { identityAuthority } from '@/auth/oidc'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const expiryLabel = computed(() =>
  auth.expiresAt?.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' }),
)
const moments = [
  {
    day: '17',
    month: '八月',
    tag: '城市漫步',
    title: '傍晚沿着河岸走了很久',
    meta: '杭州 · 6.2 km',
  },
  {
    day: '12',
    month: '八月',
    tag: '第一次',
    title: '在巷口发现一家安静的小店',
    meta: '咖啡 · ¥36',
  },
  { day: '03', month: '八月', tag: '计划完成', title: '读完搁置许久的那本书', meta: '阅读 · 9 天' },
]
</script>

<template>
  <div class="app-shell">
    <header class="topbar">
      <RouterLink class="brand" to="/" aria-label="PassingTrace 首页"
        ><span class="brand-mark">P</span><span>PassingTrace</span></RouterLink
      >
      <nav class="nav-links" aria-label="主导航">
        <a href="#timeline">时间线</a><a href="#insight">洞察</a>
      </nav>
      <div class="account-actions">
        <template v-if="auth.isAuthenticated"
          ><span class="signed-user"><i></i>{{ auth.username }}</span
          ><button class="text-button" :disabled="auth.busy" @click="auth.logout">
            退出
          </button></template
        >
        <template v-else
          ><button class="button button-dark compact-button" :disabled="auth.busy" @click="auth.login">
            手机扫码登录
          </button></template
        >
      </div>
    </header>

    <main>
      <section class="hero">
        <div class="hero-copy">
          <p class="eyebrow">YOUR LIFE, IN CONTEXT</p>
          <h1>把生活留下来，<br /><em>看见时间的形状。</em></h1>
          <p class="hero-lede">
            记录经历，也写下计划。PassingTrace 会将零散的文字整理成只属于你的时间线与生活洞察。
          </p>
          <div class="hero-actions">
            <button
              class="button button-accent"
              :disabled="auth.busy || auth.isAuthenticated"
              @click="auth.login"
            >
              {{ auth.isAuthenticated ? '身份已确认' : '手机扫码登录' }}
              <span aria-hidden="true">↗</span>
            </button>
            <span class="security-note"><i>✓</i> 标准 OIDC · PKCE</span>
          </div>
        </div>

        <aside class="session-card" :class="{ active: auth.isAuthenticated }">
          <div class="session-topline">
            <span>{{ auth.isAuthenticated ? '当前会话' : '私人空间' }}</span
            ><span class="live-dot"></span>
          </div>
          <template v-if="auth.isAuthenticated">
            <div class="avatar">{{ auth.username.slice(0, 1).toUpperCase() }}</div>
            <h2>欢迎回来，{{ auth.username }}</h2>
            <p>身份已由 PassingTrace Identity 确认。</p>
            <dl class="session-details">
              <div>
                <dt>令牌到期</dt>
                <dd>{{ expiryLabel }}</dd>
              </div>
              <div>
                <dt>资源权限</dt>
                <dd>passingtrace.api</dd>
              </div>
            </dl>
          </template>
          <template v-else>
            <p class="card-number">01</p>
            <h2>一个账号，连接所有 PassingTrace 客户端。</h2>
            <p>账号由 Android 手机端创建；网页和桌面端通过手机扫码批准，各端独立保存令牌。</p>
            <button class="inline-link" :disabled="auth.busy" @click="auth.login">
              显示登录二维码 <span>→</span>
            </button>
          </template>
        </aside>
      </section>

      <p v-if="auth.error" class="error-banner" role="alert">
        {{ auth.error }}<button @click="auth.clearError">关闭</button>
      </p>

      <section id="timeline" class="timeline-section">
        <div class="section-heading">
          <div>
            <p class="eyebrow">AUGUST 2026</p>
            <h2>最近的轨迹</h2>
          </div>
          <span>按发生时间整理</span>
        </div>
        <div class="moment-list">
          <article v-for="moment in moments" :key="moment.title" class="moment-card">
            <time
              ><strong>{{ moment.day }}</strong
              ><span>{{ moment.month }}</span></time
            >
            <div class="moment-rule"><i></i></div>
            <div class="moment-content">
              <span class="tag">{{ moment.tag }}</span>
              <h3>{{ moment.title }}</h3>
              <p>{{ moment.meta }}</p>
            </div>
            <span class="arrow" aria-hidden="true">↗</span>
          </article>
        </div>
      </section>
      <section id="insight" class="insight-band">
        <p>“你在这个月去过 <strong>3 个新地点</strong>，步行记录比上月同期多了 28%。”</p>
        <span>基于你的私人数据生成</span>
      </section>
    </main>
    <footer>
      <span>PassingTrace © 2026</span><span>Identity: {{ identityAuthority }}</span>
    </footer>
  </div>
</template>
