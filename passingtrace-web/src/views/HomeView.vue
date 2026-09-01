<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'
import { RouterLink } from 'vue-router'

import BrandMark from '@/components/BrandMark.vue'
import WebAppHeader from '@/components/WebAppHeader.vue'
import { getLatestAndroidDownloadUrl } from '@/api/app-updates'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const eventsApiBase = (import.meta.env.VITE_EVENTS_API_BASE_URL ?? '').replace(/\/$/, '')
const androidDownloadUrl = `${eventsApiBase}/api/v1/app-updates/android/latest/download`
const webActionLabel = computed(() => (auth.isAuthenticated ? '打开我的记录' : '在网页端登录'))
const downloadBusy = ref(false)
const downloadError = ref<string | null>(null)

async function downloadAndroid(event?: MouseEvent) {
  event?.preventDefault()
  if (downloadBusy.value) return

  downloadBusy.value = true
  downloadError.value = null
  const controller = new AbortController()
  const timeout = window.setTimeout(() => controller.abort(), 10_000)

  try {
    const url = await getLatestAndroidDownloadUrl(eventsApiBase, controller.signal)
    window.location.assign(url)
  } catch (reason) {
    downloadError.value =
      reason instanceof Error && reason.name !== 'AbortError'
        ? reason.message
        : '连接下载服务超时，请检查网络后重试。'
    await nextTick()
    document.querySelector<HTMLElement>('#download-error')?.focus()
  } finally {
    window.clearTimeout(timeout)
    downloadBusy.value = false
  }
}
</script>

<template>
  <div class="landing-page">
    <a class="skip-link" href="#main-content">跳到主要内容</a>
    <WebAppHeader
      variant="marketing"
      :download-url="androidDownloadUrl"
      :download-busy="downloadBusy"
      @download="downloadAndroid"
    />

    <main id="main-content">
      <section class="hero-section" aria-labelledby="hero-title">
        <div class="hero-copy">
          <p class="section-kicker">你的私人生活档案</p>
          <h1 id="hero-title">把散落的生活，<br /><span>收进会理解你的记忆盒。</span></h1>
          <p class="hero-lede">
            记录文字、照片、视频、文件和地点。PassingTrace 会替你整理分类、理解线索，
            在你想回望时，用真实记录回答问题。
          </p>
          <div class="hero-actions">
            <a
              class="button button-primary"
              :href="androidDownloadUrl"
              :aria-disabled="downloadBusy"
              @click="downloadAndroid"
            >
              <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
                <path d="M12 3v12m0 0 4-4m-4 4-4-4M5 20h14" />
              </svg>
              {{ downloadBusy ? '正在准备下载…' : '下载 Android 版' }}
            </a>
            <RouterLink v-if="auth.isAuthenticated" class="button button-secondary" to="/events">
              {{ webActionLabel }}
            </RouterLink>
            <button
              v-else
              class="button button-secondary"
              :disabled="auth.busy"
              @click="auth.login('/events')"
            >
              {{ auth.busy ? '正在打开…' : webActionLabel }}
            </button>
          </div>
          <div
            v-if="downloadError"
            id="download-error"
            class="inline-error download-error"
            role="alert"
            tabindex="-1"
          >
            <span>{{ downloadError }}</span>
            <span class="download-error__actions">
              <button class="text-button" :disabled="downloadBusy" @click="downloadAndroid()">
                重试
              </button>
              <button class="text-button" @click="downloadError = null">关闭</button>
            </span>
          </div>
          <p class="hero-note">
            <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
              <path d="M12 3 5 6v5c0 4.6 2.8 8.1 7 10 4.2-1.9 7-5.4 7-10V6l-7-3Z" />
              <path d="m9.2 12 1.8 1.8 3.8-4" />
            </svg>
            私有对象存储 · 用户数据隔离 · 回答附带记录证据
          </p>
          <p v-if="auth.error" class="inline-error" role="alert">
            {{ auth.error }}
            <button class="text-button" @click="auth.clearError">关闭</button>
          </p>
        </div>

        <div class="hero-product" aria-label="PassingTrace 应用界面预览">
          <div class="hero-orbit hero-orbit--one" aria-hidden="true"></div>
          <div class="hero-orbit hero-orbit--two" aria-hidden="true"></div>
          <article class="preview-window">
            <header class="preview-window__bar">
              <span class="preview-logo"><BrandMark /></span>
              <div><strong>我的记录</strong><small>2026 年 8 月</small></div>
              <span class="preview-add" aria-hidden="true">
                <svg class="ui-icon" viewBox="0 0 24 24"><path d="M12 5v14M5 12h14" /></svg>
              </span>
            </header>
            <div class="preview-window__body">
              <p class="preview-date"><strong>今天</strong><span>8 月 31 日</span></p>
              <div class="preview-timeline">
                <article class="preview-record">
                  <p><span>19:20</span><span>杭州 · 西湖边</span></p>
                  <h2>傍晚沿湖走了很久</h2>
                  <p>风比白天凉，水面把远处的灯慢慢拉长。</p>
                  <div>
                    <span class="preview-tag preview-tag--primary">美景</span>
                    <span class="preview-tag">步行</span>
                  </div>
                </article>
                <article class="preview-record preview-record--soft">
                  <p><span>12:10</span><span>留下了一张照片</span></p>
                  <h2>巷口新开的面馆</h2>
                  <div>
                    <span class="preview-tag preview-tag--primary">美食</span>
                    <span class="preview-tag">探店</span>
                  </div>
                </article>
              </div>
              <aside class="preview-insight">
                <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
                  <path d="m12 3 1.2 4.1L17 9l-3.8 1.9L12 15l-1.2-4.1L7 9l3.8-1.9L12 3Z" />
                  <path d="m18.5 14 .7 2.3 2.3.7-2.3.7-.7 2.3-.7-2.3-2.3-.7 2.3-.7.7-2.3Z" />
                </svg>
                <div>
                  <strong>这个月，你更常在傍晚留下记录</strong>
                  <span>基于 13 条记录与 4 个地点</span>
                </div>
              </aside>
            </div>
          </article>
          <span class="memory-box-badge"><BrandMark :decorative="false" /></span>
        </div>
      </section>

      <section id="features" class="feature-section" aria-labelledby="feature-title">
        <div class="section-heading">
          <p class="section-kicker">不只是记事</p>
          <h2 id="feature-title">留下信息，也留下它发生时的上下文。</h2>
          <p>你只管记录，整理、关联和回望交给 PassingTrace。</p>
        </div>
        <div class="feature-grid">
          <article>
            <span class="feature-index">01</span>
            <svg class="feature-icon" viewBox="0 0 32 32" aria-hidden="true">
              <path d="M8 5h12l5 5v17H8V5Z" />
              <path d="M20 5v6h5M12 16h9M12 21h6" />
            </svg>
            <h3>多媒体记录</h3>
            <p>文字、图片、视频和各种文件放进同一条记录，地点与时间也会一起保存。</p>
          </article>
          <article>
            <span class="feature-index">02</span>
            <svg class="feature-icon" viewBox="0 0 32 32" aria-hidden="true">
              <path
                d="M16 4v5M16 23v5M4 16h5M23 16h5M7.5 7.5l3.4 3.4M21.1 21.1l3.4 3.4M24.5 7.5l-3.4 3.4M10.9 21.1l-3.4 3.4"
              />
              <circle cx="16" cy="16" r="6" />
            </svg>
            <h3>AI 分类与理解</h3>
            <p>自动概括图片与正文，补充美食、旅行、运动等标签，原始内容始终由你掌控。</p>
          </article>
          <article>
            <span class="feature-index">03</span>
            <svg class="feature-icon" viewBox="0 0 32 32" aria-hidden="true">
              <path d="M16 28s9-8.4 9-16a9 9 0 1 0-18 0c0 7.6 9 16 9 16Z" />
              <circle cx="16" cy="12" r="3" />
            </svg>
            <h3>记忆与地点回溯</h3>
            <p>问“上个月去了哪里”，从自己的记录获得有证据的回答，还能导航回曾经保存的地点。</p>
          </article>
        </div>
      </section>

      <section id="experience" class="experience-section" aria-labelledby="experience-title">
        <div class="experience-copy">
          <p class="section-kicker">从记录到回答</p>
          <h2 id="experience-title">不用反复翻找，也不让 AI 凭空猜测。</h2>
          <p>
            每次分析都和原始记录、修订版本与证据绑定。问题需要精确数字时，系统重新统计；
            问到经历与偏好时，只在你的数据范围内检索。
          </p>
          <ol class="experience-steps">
            <li><strong>随手留下</strong><span>把当下的文字、照片、文件与地点放进去。</span></li>
            <li><strong>后台整理</strong><span>AI 生成摘要、分类、标签与检索索引。</span></li>
            <li>
              <strong>随时问回</strong><span>答案带着可点击的记录标题，而不是模糊的猜测。</span>
            </li>
          </ol>
        </div>
        <article class="assistant-demo">
          <header>
            <span class="assistant-mark"><BrandMark /></span>
            <div><strong>问问 AI</strong><small>只回答你的生活记录</small></div>
          </header>
          <div class="demo-question">帮我总结一下这个月的生活。</div>
          <div class="demo-answer">
            <p>这个月有三条清晰的主线：个人项目继续推进，运动保持稳定，也留下了几次散步和探店。</p>
            <a href="#experience"><span>八月生活小结</span><small>8 月 30 日 · 生活</small></a>
            <a href="#experience"><span>沿江骑行 12 公里</span><small>8 月 23 日 · 运动</small></a>
          </div>
          <div class="demo-composer">
            <span>继续问你的记录…</span>
            <i aria-hidden="true"
              ><svg class="ui-icon" viewBox="0 0 24 24"><path d="m12 19V5m0 0-5 5m5-5 5 5" /></svg
            ></i>
          </div>
        </article>
      </section>

      <section id="privacy" class="privacy-section" aria-labelledby="privacy-title">
        <span class="privacy-mark"><BrandMark /></span>
        <div>
          <p class="section-kicker">属于你的数据边界</p>
          <h2 id="privacy-title">记忆可以被理解，但不应该失去归属。</h2>
          <p>
            附件保持私有，访问地址短时有效；搜索与 AI
            工具从登录身份取得用户范围，不能请求其他人的数据。
          </p>
        </div>
        <ul>
          <li>私有 S3 对象存储</li>
          <li>每个回答保留证据</li>
          <li>长期记忆可确认、修正或遗忘</li>
        </ul>
      </section>

      <section class="download-section" aria-labelledby="download-title">
        <div>
          <p class="section-kicker">从今天开始</p>
          <h2 id="download-title">先留下一条，未来就多一个可以回去的地方。</h2>
        </div>
        <a
          class="button button-primary"
          :href="androidDownloadUrl"
          :aria-disabled="downloadBusy"
          @click="downloadAndroid"
        >
          {{ downloadBusy ? '正在准备下载…' : '下载 Android 安装包' }}
          <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
            <path d="m9 5 7 7-7 7" />
          </svg>
        </a>
      </section>
    </main>

    <footer class="landing-footer">
      <RouterLink class="site-brand" to="/">
        <span class="site-brand__mark"><BrandMark /></span>
        <span class="site-brand__copy"
          ><strong>PassingTrace</strong><small>私人生活档案</small></span
        >
      </RouterLink>
      <p>记录 · 理解 · 回望</p>
      <p>© 2026 PassingTrace</p>
    </footer>
  </div>
</template>

<style scoped>
.landing-page {
  min-height: 100dvh;
  overflow-x: clip;
  color: var(--ink);
  background: var(--canvas);
}
.hero-section {
  width: min(1280px, calc(100% - 48px));
  min-height: calc(100dvh - 72px);
  margin: 0 auto;
  padding: clamp(64px, 9vw, 132px) 0 96px;
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(380px, 0.82fr);
  align-items: center;
  gap: clamp(56px, 8vw, 112px);
}
.section-kicker {
  margin: 0 0 16px;
  color: var(--primary-strong);
  font-size: 12px;
  font-weight: 800;
  letter-spacing: 0.15em;
  text-transform: uppercase;
}
.hero-copy h1 {
  max-width: 780px;
  margin: 0;
  font-size: clamp(48px, 6.2vw, 86px);
  line-height: 1.08;
  letter-spacing: -0.065em;
  text-wrap: balance;
}
.hero-copy h1 span {
  color: var(--primary);
}
.hero-lede {
  max-width: 650px;
  margin: 32px 0;
  color: var(--ink-secondary);
  font-size: clamp(16px, 1.45vw, 19px);
  line-height: 1.85;
}
.hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}
.hero-note {
  margin: 20px 0 0;
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--ink-tertiary);
  font-size: 12px;
}
.hero-note .ui-icon {
  width: 18px;
  height: 18px;
  color: var(--success);
}
.inline-error {
  max-width: 620px;
  margin: 16px 0 0;
  padding: 12px 14px;
  border: 1px solid color-mix(in srgb, var(--danger) 35%, transparent);
  border-radius: var(--radius-md);
  color: var(--danger);
  background: color-mix(in srgb, var(--danger) 8%, var(--surface));
}
.download-error {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}
.download-error:focus-visible {
  outline: 2px solid var(--danger);
  outline-offset: 3px;
}
.download-error__actions {
  display: inline-flex;
  flex: 0 0 auto;
  gap: 8px;
}
.button[aria-disabled='true'] {
  pointer-events: none;
  opacity: 0.55;
}
.hero-product {
  min-height: 620px;
  position: relative;
  display: grid;
  place-items: center;
}
.hero-orbit {
  position: absolute;
  border: 1px solid color-mix(in srgb, var(--primary) 16%, transparent);
  border-radius: 50%;
  pointer-events: none;
}
.hero-orbit--one {
  width: 530px;
  height: 530px;
}
.hero-orbit--two {
  width: 390px;
  height: 390px;
}
.preview-window {
  width: min(100%, 410px);
  min-height: 560px;
  overflow: hidden;
  position: relative;
  z-index: 1;
  border: 1px solid var(--line);
  border-radius: 30px;
  background: var(--surface-soft);
  box-shadow: var(--shadow-2);
  transform: rotate(1.5deg);
}
.preview-window__bar {
  min-height: 72px;
  padding: 12px 18px;
  display: flex;
  align-items: center;
  gap: 12px;
  border-bottom: 1px solid var(--line);
  background: var(--surface);
}
.preview-window__bar div {
  flex: 1;
  min-width: 0;
}
.preview-window__bar strong,
.preview-window__bar small {
  display: block;
}
.preview-window__bar strong {
  font-size: 15px;
}
.preview-window__bar small {
  margin-top: 2px;
  color: var(--ink-tertiary);
  font-size: 10px;
}
.preview-logo {
  width: 38px;
  height: 38px;
}
.preview-add {
  width: 42px;
  height: 42px;
  display: grid;
  place-items: center;
  border-radius: 12px;
  color: var(--on-primary);
  background: var(--primary);
  font-size: 22px;
}
.preview-window__body {
  padding: 22px 18px;
}
.preview-date {
  margin: 0 0 14px;
  display: flex;
  align-items: baseline;
  gap: 8px;
}
.preview-date strong {
  font-size: 18px;
}
.preview-date span {
  color: var(--ink-tertiary);
  font-size: 11px;
}
.preview-timeline {
  display: grid;
  gap: 12px;
  position: relative;
  padding-left: 18px;
}
.preview-timeline::before {
  content: '';
  position: absolute;
  top: 8px;
  bottom: 8px;
  left: 4px;
  width: 1px;
  background: var(--line-strong);
}
.preview-record {
  padding: 15px;
  position: relative;
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-1);
}
.preview-record::before {
  content: '';
  position: absolute;
  top: 20px;
  left: -19px;
  width: 9px;
  height: 9px;
  border: 3px solid var(--surface-soft);
  border-radius: 50%;
  background: var(--primary);
  box-shadow: 0 0 0 1px var(--primary);
}
.preview-record--soft {
  opacity: 0.82;
}
.preview-record > p:first-child {
  margin: 0 0 7px;
  display: flex;
  justify-content: space-between;
  color: var(--ink-tertiary);
  font-size: 10px;
}
.preview-record h2 {
  margin: 0;
  font-size: 15px;
}
.preview-record h2 + p {
  margin: 7px 0 11px;
  color: var(--ink-secondary);
  font-size: 12px;
  line-height: 1.55;
}
.preview-record > div {
  display: flex;
  gap: 6px;
}
.preview-tag {
  min-height: 24px;
  padding: 3px 8px;
  border: 1px solid var(--line);
  border-radius: 999px;
  color: var(--ink-secondary);
  background: var(--surface-soft);
  font-size: 10px;
}
.preview-tag--primary {
  border-color: transparent;
  color: var(--primary-strong);
  background: var(--primary-soft);
  font-weight: 700;
}
.preview-insight {
  margin: 18px 0 0 18px;
  padding: 14px;
  display: flex;
  gap: 11px;
  border-radius: var(--radius-lg);
  color: var(--primary-strong);
  background: var(--primary-soft);
}
.preview-insight .ui-icon {
  flex: 0 0 auto;
}
.preview-insight strong,
.preview-insight span {
  display: block;
}
.preview-insight strong {
  font-size: 12px;
  line-height: 1.5;
}
.preview-insight span {
  margin-top: 4px;
  color: var(--ink-secondary);
  font-size: 9px;
}
.memory-box-badge {
  width: 92px;
  height: 92px;
  padding: 8px;
  position: absolute;
  z-index: 2;
  right: -8px;
  bottom: 48px;
  border: 1px solid var(--line);
  border-radius: 27px;
  background: var(--surface);
  box-shadow: var(--shadow-2);
  transform: rotate(-6deg);
}
.feature-section {
  padding: clamp(80px, 10vw, 144px) max(24px, calc((100vw - 1196px) / 2));
  background: var(--surface);
}
.section-heading {
  max-width: 760px;
  margin-bottom: 52px;
}
.section-heading h2,
.experience-copy h2,
.privacy-section h2,
.download-section h2 {
  margin: 0;
  font-size: clamp(34px, 4.2vw, 58px);
  line-height: 1.16;
  letter-spacing: -0.05em;
  text-wrap: balance;
}
.section-heading > p:last-child {
  max-width: 620px;
  margin: 20px 0 0;
  color: var(--ink-secondary);
  font-size: 16px;
  line-height: 1.75;
}
.feature-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  border: 1px solid var(--line);
  border-radius: var(--radius-xl);
  overflow: hidden;
}
.feature-grid article {
  min-height: 350px;
  padding: 32px;
  position: relative;
  background: var(--surface);
}
.feature-grid article + article {
  border-left: 1px solid var(--line);
}
.feature-index {
  position: absolute;
  top: 30px;
  right: 30px;
  color: var(--ink-tertiary);
  font-size: 11px;
  font-variant-numeric: tabular-nums;
}
.feature-icon {
  width: 42px;
  height: 42px;
  margin: 76px 0 28px;
  fill: none;
  stroke: var(--primary);
  stroke-width: 1.7;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.feature-grid h3 {
  margin: 0 0 13px;
  font-size: 21px;
}
.feature-grid p {
  margin: 0;
  color: var(--ink-secondary);
  font-size: 14px;
  line-height: 1.75;
}
.experience-section {
  width: min(1196px, calc(100% - 48px));
  margin: 0 auto;
  padding: clamp(88px, 11vw, 156px) 0;
  display: grid;
  grid-template-columns: minmax(0, 0.85fr) minmax(420px, 1fr);
  align-items: center;
  gap: clamp(56px, 9vw, 120px);
}
.experience-copy > p:not(.section-kicker) {
  margin: 24px 0 32px;
  color: var(--ink-secondary);
  line-height: 1.85;
}
.experience-steps {
  margin: 0;
  padding: 0;
  list-style: none;
  display: grid;
  gap: 18px;
  counter-reset: steps;
}
.experience-steps li {
  padding-left: 46px;
  position: relative;
  counter-increment: steps;
}
.experience-steps li::before {
  content: '0' counter(steps);
  position: absolute;
  left: 0;
  top: 2px;
  color: var(--accent);
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.08em;
}
.experience-steps strong,
.experience-steps span {
  display: block;
}
.experience-steps strong {
  font-size: 15px;
}
.experience-steps span {
  margin-top: 3px;
  color: var(--ink-secondary);
  font-size: 13px;
  line-height: 1.6;
}
.assistant-demo {
  padding: 24px;
  border: 1px solid var(--line);
  border-radius: var(--radius-xl);
  background: var(--surface);
  box-shadow: var(--shadow-1);
}
.assistant-demo > header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding-bottom: 18px;
  border-bottom: 1px solid var(--line);
}
.assistant-mark {
  width: 40px;
  height: 40px;
}
.assistant-demo header strong,
.assistant-demo header small {
  display: block;
}
.assistant-demo header small {
  margin-top: 2px;
  color: var(--ink-tertiary);
  font-size: 10px;
}
.demo-question {
  max-width: 75%;
  margin: 28px 0 18px auto;
  padding: 13px 15px;
  border-radius: 16px 16px 4px 16px;
  color: var(--on-primary);
  background: var(--primary);
  font-size: 13px;
}
.demo-answer {
  max-width: 88%;
  padding: 17px;
  border: 1px solid var(--line);
  border-radius: 4px 16px 16px;
  background: var(--surface-soft);
}
.demo-answer > p {
  margin: 0 0 15px;
  color: var(--ink-secondary);
  font-size: 13px;
  line-height: 1.7;
}
.demo-answer a {
  padding: 11px 0;
  display: flex;
  justify-content: space-between;
  gap: 14px;
  border-top: 1px solid var(--line);
}
.demo-answer a span {
  color: var(--primary-strong);
  font-size: 12px;
  font-weight: 700;
}
.demo-answer a small {
  color: var(--ink-tertiary);
  font-size: 9px;
  white-space: nowrap;
}
.demo-composer {
  min-height: 52px;
  margin-top: 28px;
  padding: 0 8px 0 15px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border: 1px solid var(--line-strong);
  border-radius: var(--radius-lg);
  color: var(--ink-tertiary);
  font-size: 12px;
}
.demo-composer i {
  width: 36px;
  height: 36px;
  display: grid;
  place-items: center;
  border-radius: 11px;
  color: var(--on-primary);
  background: var(--primary);
  font-style: normal;
}
.privacy-section {
  padding: clamp(64px, 7vw, 96px) max(24px, calc((100vw - 1196px) / 2));
  display: grid;
  grid-template-columns: 120px minmax(0, 1fr) minmax(260px, 0.5fr);
  gap: clamp(32px, 6vw, 80px);
  align-items: center;
  color: var(--surface);
  background: var(--ink);
}
.privacy-mark {
  width: 96px;
  height: 96px;
}
.privacy-section .section-kicker {
  color: var(--accent);
}
.privacy-section h2 {
  font-size: clamp(32px, 3.5vw, 50px);
}
.privacy-section div > p:last-child {
  max-width: 720px;
  color: color-mix(in srgb, var(--surface) 68%, transparent);
  line-height: 1.75;
}
.privacy-section ul {
  margin: 0;
  padding: 0;
  list-style: none;
  display: grid;
  gap: 14px;
  color: color-mix(in srgb, var(--surface) 76%, transparent);
  font-size: 13px;
}
.privacy-section li {
  padding-left: 20px;
  position: relative;
}
.privacy-section li::before {
  content: '';
  width: 7px;
  height: 7px;
  position: absolute;
  left: 0;
  top: 0.5em;
  border-radius: 50%;
  background: var(--accent);
}
.download-section {
  width: min(1196px, calc(100% - 48px));
  margin: 0 auto;
  padding: clamp(80px, 10vw, 132px) 0;
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 48px;
}
.download-section h2 {
  max-width: 790px;
}
.download-section .button {
  flex: 0 0 auto;
}
.landing-footer {
  min-height: 112px;
  padding: 24px max(24px, calc((100vw - 1196px) / 2));
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  align-items: center;
  border-top: 1px solid var(--line);
  color: var(--ink-tertiary);
  font-size: 11px;
}
.landing-footer > p:last-child {
  justify-self: end;
}
@media (max-width: 980px) {
  .hero-section,
  .experience-section {
    grid-template-columns: 1fr;
  }
  .hero-copy {
    max-width: 760px;
  }
  .hero-product {
    min-height: 570px;
  }
  .feature-grid {
    grid-template-columns: 1fr;
  }
  .feature-grid article {
    min-height: auto;
  }
  .feature-grid article + article {
    border-left: 0;
    border-top: 1px solid var(--line);
  }
  .feature-icon {
    margin-top: 48px;
  }
  .privacy-section {
    grid-template-columns: 88px 1fr;
  }
  .privacy-section ul {
    grid-column: 2;
  }
  .download-section {
    align-items: flex-start;
    flex-direction: column;
  }
}
@media (max-width: 640px) {
  .hero-section {
    width: min(100% - 32px, 1280px);
    min-height: auto;
    padding: 52px 0 72px;
  }
  .hero-copy h1 {
    font-size: clamp(42px, 13.5vw, 62px);
  }
  .hero-actions {
    align-items: stretch;
    flex-direction: column;
  }
  .hero-actions .button {
    width: 100%;
  }
  .hero-note {
    align-items: flex-start;
    line-height: 1.55;
  }
  .download-error {
    align-items: flex-start;
    flex-direction: column;
  }
  .hero-product {
    min-height: 500px;
  }
  .preview-window {
    min-height: 500px;
    border-radius: 24px;
  }
  .hero-orbit--one {
    width: 410px;
    height: 410px;
  }
  .hero-orbit--two {
    width: 300px;
    height: 300px;
  }
  .memory-box-badge {
    width: 74px;
    height: 74px;
    right: -6px;
    bottom: 10px;
  }
  .feature-section {
    padding-inline: 16px;
  }
  .section-heading {
    margin-bottom: 32px;
  }
  .feature-grid article {
    padding: 24px;
  }
  .experience-section,
  .download-section {
    width: calc(100% - 32px);
  }
  .assistant-demo {
    padding: 16px;
  }
  .privacy-section {
    grid-template-columns: 1fr;
    padding-inline: 24px;
  }
  .privacy-section ul {
    grid-column: auto;
  }
  .privacy-mark {
    width: 72px;
    height: 72px;
  }
  .landing-footer {
    grid-template-columns: 1fr;
    gap: 18px;
  }
  .landing-footer > p,
  .landing-footer > p:last-child {
    margin: 0;
    justify-self: start;
  }
}
@media (prefers-reduced-motion: reduce) {
  .preview-window,
  .memory-box-badge {
    transform: none;
  }
}
</style>
