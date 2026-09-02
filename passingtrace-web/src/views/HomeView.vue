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
          <h1 id="hero-title">记下当下，串成故事，<br /><span>再问回你的生活。</span></h1>
          <p class="hero-lede">
            星期八把文字、照片、计划和地点收进同一条时间线；你可以把散落的记录拼成故事线， 也可以让
            AI 沿着真实记录，回答经历、地点与生活变化。
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
            私有对象存储 · 故事线保留原始记录 · AI 回答附带可点击证据
          </p>
          <p v-if="auth.error" class="inline-error" role="alert">
            {{ auth.error }}
            <button class="text-button" @click="auth.clearError">关闭</button>
          </p>
        </div>

        <div class="hero-product" aria-label="星期八应用界面预览">
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
                  <strong>这两条记录可以接入“西湖周末”故事线</strong>
                  <span>AI 已整理标签，原始内容保持不变</span>
                </div>
              </aside>
            </div>
          </article>
          <span class="memory-box-badge"><BrandMark :decorative="false" /></span>
        </div>
      </section>

      <section id="features" class="feature-section" aria-labelledby="feature-title">
        <div class="section-heading">
          <p class="section-kicker">一套完整的生活记录方式</p>
          <h2 id="feature-title">一条记录是当下，连起来就是一段经历。</h2>
          <p>从随手记下一件事，到整理一段完整的过程，再到用自然语言重新找到它。</p>
        </div>
        <div class="feature-grid">
          <article>
            <span class="feature-index">01</span>
            <svg class="feature-icon" viewBox="0 0 32 32" aria-hidden="true">
              <path d="M8 5h12l5 5v17H8V5Z" />
              <path d="M20 5v6h5M12 16h9M12 21h6" />
            </svg>
            <h3>记录每个当下</h3>
            <p>文字、图片、视频、文件、时间和地点都能留在同一条记录里，计划也可以提前写下。</p>
          </article>
          <article>
            <span class="feature-index">02</span>
            <svg class="feature-icon" viewBox="0 0 32 32" aria-hidden="true">
              <path
                d="M16 4v5M16 23v5M4 16h5M23 16h5M7.5 7.5l3.4 3.4M21.1 21.1l3.4 3.4M24.5 7.5l-3.4 3.4M10.9 21.1l-3.4 3.4"
              />
              <circle cx="16" cy="16" r="6" />
            </svg>
            <h3>自动整理线索</h3>
            <p>AI 理解正文与图片，补充分类、行为标签和检索线索；人工选择始终优先。</p>
          </article>
          <article>
            <span class="feature-index">03</span>
            <svg class="feature-icon" viewBox="0 0 32 32" aria-hidden="true">
              <rect x="4" y="5" width="9" height="7" rx="2" />
              <rect x="19" y="20" width="9" height="7" rx="2" />
              <path d="M13 8.5h3a5 5 0 0 1 5 5V20M9 12v8h10" />
            </svg>
            <h3>拼成完整故事线</h3>
            <p>把购票、出发、抵达和沿途分支接成一段完整经历，也能把还没发生的计划放进来。</p>
          </article>
          <article>
            <span class="feature-index">04</span>
            <svg class="feature-icon" viewBox="0 0 32 32" aria-hidden="true">
              <path d="M7 6h18v15H14l-6 5v-5H7V6Z" />
              <path d="M11 11h10M11 16h7" />
            </svg>
            <h3>问回自己的生活</h3>
            <p>询问去过哪里、做过什么或某段经历怎样发展，回答会带上记录标题、地点与导航入口。</p>
          </article>
        </div>
      </section>

      <section id="storylines" class="storyline-section" aria-labelledby="storyline-title">
        <div class="storyline-demo" aria-label="故事线编辑器示意">
          <header class="storyline-demo__header">
            <div>
              <span>故事线</span>
              <strong>周末去看海</strong>
            </div>
            <span class="storyline-status"><i aria-hidden="true"></i>进行中</span>
          </header>
          <div class="storyline-canvas">
            <p class="storyline-stage">准备</p>
            <div class="story-node story-node--plan">
              <small>未来安排</small><strong>订周六早班车</strong><span>周六 · 07:30</span>
            </div>
            <span class="story-edge story-edge--one" aria-hidden="true"></span>
            <p class="storyline-stage storyline-stage--second">抵达以后</p>
            <div class="story-node story-node--main">
              <small>当下记录</small><strong>沿海边慢慢走</strong><span>美景 · 步行 · 拍照</span>
            </div>
            <span class="story-edge story-edge--branch-a" aria-hidden="true"></span>
            <span class="story-edge story-edge--branch-b" aria-hidden="true"></span>
            <div class="story-node story-node--branch story-node--food">
              <small>分支</small><strong>在码头吃海鲜</strong><span>美食 · 聚餐</span>
            </div>
            <div class="story-node story-node--branch story-node--sunset">
              <small>分支</small><strong>等一场日落</strong><span>18:42 · 灯塔附近</span>
            </div>
          </div>
          <footer class="storyline-demo__footer">
            <span>4 个节点</span><span>2 个阶段</span><span>记录与计划共同组成</span>
          </footer>
        </div>

        <div class="storyline-copy">
          <p class="section-kicker">故事线</p>
          <h2 id="storyline-title">散装的记录，终于可以讲清一件事。</h2>
          <p>
            一次旅行、一个项目、一只宠物的成长，往往不是一条记录能说完的。故事线保留每条原始记录，
            只描述它们之间的阶段、先后、分支与汇合。
          </p>
          <ul class="storyline-benefits">
            <li>
              <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
                <path d="m5 12 4 4L19 6" />
              </svg>
              <span
                ><strong>记录与计划可以放在一起</strong
                >出发前的安排和发生后的经历，共用一条故事线。</span
              >
            </li>
            <li>
              <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
                <path d="m5 12 4 4L19 6" />
              </svg>
              <span
                ><strong>复杂关系交给网页整理</strong
                >大屏编辑分支与阶段，手机继续负责随时记录和查看。</span
              >
            </li>
            <li>
              <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
                <path d="m5 12 4 4L19 6" />
              </svg>
              <span
                ><strong>原始内容不会被复制或改写</strong
                >节点固定到记录修订，过去的证据依然清楚。</span
              >
            </li>
          </ul>
          <RouterLink v-if="auth.isAuthenticated" class="storyline-link" to="/storylines">
            打开我的故事线
            <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
              <path d="m9 5 7 7-7 7" />
            </svg>
          </RouterLink>
        </div>
      </section>

      <section id="assistant" class="experience-section" aria-labelledby="experience-title">
        <div class="experience-copy">
          <p class="section-kicker">问问 AI</p>
          <h2 id="experience-title">它不是替你编故事，而是帮你找回故事。</h2>
          <p>
            AI 能理解连续对话，也能检索记录、故事线和确认过的地点。回答中的记录标题可以直接打开；
            问到曾经去过的地方，还能使用保存的坐标唤起高德导航。
          </p>
          <ol class="experience-steps">
            <li>
              <strong>先找你的记录</strong><span>经历类问题优先从私人记录与故事线里检索证据。</span>
            </li>
            <li>
              <strong>再补实时地点能力</strong
              ><span>需要路线、天气或公共地点时，再调用高德能力。</span>
            </li>
            <li>
              <strong>把结果交还给你</strong><span>回答、记录证据与导航动作各自标明来源。</span>
            </li>
          </ol>
        </div>
        <article class="assistant-demo">
          <header>
            <span class="assistant-mark"><BrandMark /></span>
            <div><strong>问问 AI</strong><small>只回答你的生活记录</small></div>
          </header>
          <div class="demo-question">定位出我最近吃过的一家烤肉店。</div>
          <div class="demo-answer">
            <p>找到了。你最近一次吃烤肉是在“周末和朋友去吃烤肉”这条记录里。</p>
            <div class="demo-evidence">
              <span>周末和朋友去吃烤肉</span><small>8 月 31 日 · 美食 · 记录证据</small>
            </div>
            <div class="demo-place">
              <span class="demo-place__icon">
                <svg class="ui-icon" viewBox="0 0 24 24" aria-hidden="true">
                  <path d="M12 21s7-6.2 7-12a7 7 0 1 0-14 0c0 5.8 7 12 7 12Z" />
                  <circle cx="12" cy="9" r="2.2" />
                </svg>
              </span>
              <span><strong>山野炉端烧</strong><small>来自你的记录 · 已保存坐标</small></span>
              <span class="demo-place__action">高德导航</span>
            </div>
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
            工具从登录身份取得用户范围，不能请求其他人的数据。外部地图结果和私人记录证据也会分开标注。
          </p>
        </div>
        <ul>
          <li>私有 S3 对象存储</li>
          <li>每个回答保留证据</li>
          <li>AI 只读检索，不修改故事线</li>
          <li>长期记忆可确认、修正或遗忘</li>
        </ul>
      </section>

      <section class="download-section" aria-labelledby="download-title">
        <div>
          <p class="section-kicker">从今天开始</p>
          <h2 id="download-title">先留下一条，未来就有一段可以继续写下去的故事。</h2>
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
        <span class="site-brand__copy"><strong>星期八</strong><small>私人生活档案</small></span>
      </RouterLink>
      <p>记录 · 串联 · 回望</p>
      <p>© 2026 星期八</p>
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
  grid-template-columns: repeat(2, 1fr);
  border: 1px solid var(--line);
  border-radius: var(--radius-xl);
  overflow: hidden;
}
.feature-grid article {
  min-height: 320px;
  padding: 32px;
  position: relative;
  background: var(--surface);
}
.feature-grid article:nth-child(even) {
  border-left: 1px solid var(--line);
}
.feature-grid article:nth-child(n + 3) {
  border-top: 1px solid var(--line);
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
.storyline-section {
  width: min(1196px, calc(100% - 48px));
  margin: 0 auto;
  padding: clamp(88px, 11vw, 156px) 0;
  display: grid;
  grid-template-columns: minmax(500px, 1.08fr) minmax(0, 0.82fr);
  align-items: center;
  gap: clamp(56px, 8vw, 112px);
}
.storyline-demo {
  min-width: 0;
  overflow: hidden;
  border: 1px solid var(--line);
  border-radius: var(--radius-xl);
  background: var(--surface);
  box-shadow: var(--shadow-1);
}
.storyline-demo__header,
.storyline-demo__footer {
  padding: 18px 22px;
  display: flex;
  align-items: center;
  border-color: var(--line);
}
.storyline-demo__header {
  justify-content: space-between;
  gap: 20px;
  border-bottom: 1px solid var(--line);
}
.storyline-demo__header div span,
.storyline-demo__header div strong {
  display: block;
}
.storyline-demo__header div span {
  margin-bottom: 3px;
  color: var(--ink-tertiary);
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.12em;
}
.storyline-demo__header div strong {
  font-size: 16px;
}
.storyline-status {
  min-height: 30px;
  padding: 0 10px;
  display: inline-flex;
  align-items: center;
  gap: 7px;
  border: 1px solid var(--line);
  border-radius: 999px;
  color: var(--primary-strong);
  background: var(--surface-soft);
  font-size: 11px;
  font-weight: 700;
}
.storyline-status i {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--success);
}
.storyline-canvas {
  min-height: 470px;
  overflow: hidden;
  position: relative;
  background-color: var(--surface-soft);
  background-image: radial-gradient(var(--line-strong) 0.8px, transparent 0.8px);
  background-size: 18px 18px;
}
.storyline-stage {
  margin: 0;
  position: absolute;
  top: 22px;
  left: 26px;
  color: var(--ink-tertiary);
  font-size: 10px;
  font-weight: 800;
  letter-spacing: 0.12em;
}
.storyline-stage--second {
  top: 222px;
}
.story-node {
  min-width: 0;
  padding: 15px 17px;
  position: absolute;
  z-index: 2;
  border: 1px solid var(--line-strong);
  border-radius: var(--radius-md);
  background: var(--surface);
  box-shadow: var(--shadow-1);
}
.story-node small,
.story-node strong,
.story-node span {
  display: block;
}
.story-node small {
  margin-bottom: 6px;
  color: var(--accent);
  font-size: 9px;
  font-weight: 800;
  letter-spacing: 0.08em;
}
.story-node strong {
  overflow-wrap: anywhere;
  font-size: 13px;
}
.story-node span {
  margin-top: 7px;
  color: var(--ink-tertiary);
  font-size: 9px;
}
.story-node--plan {
  width: 36%;
  top: 58px;
  left: 6%;
  border-style: dashed;
}
.story-node--main {
  width: 40%;
  top: 150px;
  left: 53%;
  border-color: var(--primary);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--primary) 12%, transparent);
}
.story-node--branch {
  width: 36%;
  top: 286px;
}
.story-node--food {
  left: 8%;
}
.story-node--sunset {
  left: 56%;
}
.story-edge {
  height: 1px;
  position: absolute;
  z-index: 1;
  background: var(--primary);
  transform-origin: left center;
}
.story-edge::after {
  content: '';
  width: 7px;
  height: 7px;
  position: absolute;
  top: -3px;
  right: -1px;
  border-top: 1px solid var(--primary);
  border-right: 1px solid var(--primary);
  transform: rotate(45deg);
}
.story-edge--one {
  width: 18%;
  top: 124px;
  left: 40%;
  transform: rotate(18deg);
}
.story-edge--branch-a {
  width: 30%;
  top: 236px;
  left: 70%;
  transform: rotate(153deg);
}
.story-edge--branch-b {
  width: 18%;
  top: 236px;
  left: 72%;
  transform: rotate(66deg);
}
.storyline-demo__footer {
  flex-wrap: wrap;
  gap: 8px 20px;
  border-top: 1px solid var(--line);
  color: var(--ink-tertiary);
  font-size: 10px;
}
.storyline-demo__footer span:last-child {
  margin-left: auto;
}
.storyline-copy h2 {
  margin: 0;
  font-size: clamp(34px, 4.2vw, 58px);
  line-height: 1.16;
  letter-spacing: -0.05em;
  text-wrap: balance;
}
.storyline-copy > p:not(.section-kicker) {
  margin: 24px 0 30px;
  color: var(--ink-secondary);
  line-height: 1.85;
}
.storyline-benefits {
  margin: 0;
  padding: 0;
  display: grid;
  gap: 18px;
  list-style: none;
}
.storyline-benefits li {
  display: flex;
  align-items: flex-start;
  gap: 13px;
  color: var(--ink-secondary);
  font-size: 13px;
  line-height: 1.65;
}
.storyline-benefits .ui-icon {
  width: 20px;
  height: 20px;
  margin-top: 1px;
  padding: 3px;
  border-radius: 50%;
  color: var(--on-primary);
  background: var(--primary);
}
.storyline-benefits strong {
  display: block;
  color: var(--ink);
  font-size: 14px;
}
.storyline-link {
  min-height: 48px;
  width: max-content;
  margin-top: 30px;
  padding: 0 4px;
  display: inline-flex;
  align-items: center;
  gap: 8px;
  color: var(--primary-strong);
  font-size: 14px;
  font-weight: 800;
}
.storyline-link:hover {
  text-decoration: underline;
  text-underline-offset: 5px;
}
.storyline-link .ui-icon {
  width: 18px;
  height: 18px;
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
.demo-evidence {
  padding: 11px 0;
  display: flex;
  justify-content: space-between;
  gap: 14px;
  border-top: 1px solid var(--line);
}
.demo-evidence span {
  color: var(--primary-strong);
  font-size: 12px;
  font-weight: 700;
}
.demo-evidence small {
  color: var(--ink-tertiary);
  font-size: 9px;
  white-space: nowrap;
}
.demo-place {
  min-height: 62px;
  margin-top: 12px;
  padding: 10px;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 10px;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  background: var(--surface);
}
.demo-place__icon {
  width: 40px;
  height: 40px;
  display: grid;
  place-items: center;
  border-radius: 11px;
  color: var(--primary-strong);
  background: var(--primary-soft);
}
.demo-place__icon .ui-icon {
  width: 20px;
  height: 20px;
}
.demo-place > span:nth-child(2) {
  min-width: 0;
}
.demo-place strong,
.demo-place small {
  display: block;
}
.demo-place strong {
  overflow-wrap: anywhere;
  font-size: 11px;
}
.demo-place small {
  margin-top: 3px;
  color: var(--ink-tertiary);
  font-size: 8px;
}
.demo-place__action {
  min-height: 34px;
  padding: 0 10px;
  display: inline-flex;
  align-items: center;
  border-radius: 9px;
  color: var(--on-primary);
  background: var(--primary);
  font-size: 9px;
  font-weight: 800;
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
  .storyline-section,
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
  .storyline-copy {
    max-width: 760px;
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
  .storyline-section,
  .download-section {
    width: calc(100% - 32px);
  }
  .storyline-demo__header,
  .storyline-demo__footer {
    padding-inline: 16px;
  }
  .storyline-copy {
    order: -1;
  }
  .storyline-canvas {
    min-height: 500px;
  }
  .story-node--plan {
    width: 48%;
    left: 5%;
  }
  .story-node--main {
    width: 52%;
    top: 166px;
    left: 43%;
  }
  .story-node--branch {
    width: 43%;
    top: 318px;
  }
  .story-node--food {
    left: 5%;
  }
  .story-node--sunset {
    left: 52%;
  }
  .storyline-stage--second {
    top: 254px;
  }
  .story-edge--one {
    top: 129px;
    left: 49%;
  }
  .story-edge--branch-a {
    top: 252px;
    left: 68%;
  }
  .story-edge--branch-b {
    top: 252px;
    left: 70%;
  }
  .storyline-demo__footer span:last-child {
    width: 100%;
    margin-left: 0;
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
