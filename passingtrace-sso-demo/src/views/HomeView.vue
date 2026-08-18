<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import type { User } from 'oidc-client-ts'

import { identityAuthority, mainWebUrl, oidc } from '@/auth/oidc'

const route = useRoute()
const currentOrigin = window.location.origin
const user = ref<User | null>(null)
const busy = ref(true)
const error = ref('')

const isAuthenticated = computed(() => Boolean(user.value && !user.value.expired))
const ssoSucceeded = computed(() => route.query.sso === 'success' && isAuthenticated.value)
const elapsed = computed(() => Number(route.query.elapsed ?? 0))
const claims = computed(() => [
  ['sub', String(user.value?.profile.sub ?? '—')],
  ['preferred_username', String(user.value?.profile.preferred_username ?? '—')],
  ['client_id', String(user.value?.profile.client_id ?? 'passingtrace-sso-demo')],
  ['scope', user.value?.scope ?? '—'],
  ['issuer', String(user.value?.profile.iss ?? identityAuthority)],
])

onMounted(async () => {
  user.value = await oidc.getUser()
  busy.value = false
})

async function startSso() {
  busy.value = true
  error.value = ''
  try {
    // prompt=none 是严格的静默 SSO：没有 Identity Cookie 时只会返回 login_required。
    await oidc.signinRedirect({ prompt: 'none', state: { startedAt: Date.now() } })
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '无法启动 SSO 授权。'
    busy.value = false
  }
}

async function clearLocalToken() {
  await oidc.removeUser()
  user.value = null
  window.history.replaceState({}, '', '/')
}

async function logoutIdentity() {
  if (!user.value?.id_token) {
    await clearLocalToken()
    return
  }
  await oidc.signoutRedirect()
}
</script>

<template>
  <div class="lab-shell">
    <header class="topbar">
      <a class="brand" :href="mainWebUrl" aria-label="返回 PassingTrace 主站">
        <span class="brand-mark">P</span>
        <span>PassingTrace</span>
      </a>
      <span class="kicker" style="margin: 0">SSO · 验证站</span>
      <div class="client-chip">
        <i></i>
        Client B · 独立站点
      </div>
    </header>

    <main>
      <section class="intro">
        <p class="eyebrow">SINGLE SIGN-ON PROOF</p>
        <h1>这是另一个网站，<br /><em>但认识同一个你。</em></h1>
        <p class="lede">
          本页运行在 5174，拥有独立的 Client ID 和 Token 存储。它唯一共享的是 Identity 域中不可被
          JavaScript 读取的登录 Cookie。
        </p>
      </section>

      <section class="topology" aria-label="SSO 拓扑">
        <article>
          <span>01</span>
          <strong>主站 Client A</strong>
          <code>localhost:5173</code>
          <small>passingtrace-web</small>
        </article>
        <div class="link-line"><b>共享 Identity Cookie</b><i></i></div>
        <article class="identity-node">
          <span>ID</span>
          <strong>Identity</strong>
          <code>localhost:56228</code>
          <small>用户名与密码只在这里</small>
        </article>
        <div class="link-line"><b>独立 Authorization Code</b><i></i></div>
        <article class="active-node">
          <span>02</span>
          <strong>验证站 Client B</strong>
          <code>localhost:5174</code>
          <small>passingtrace-sso-demo</small>
        </article>
      </section>

      <section class="verification-grid">
        <article class="test-panel">
          <div class="panel-head">
            <span>验证步骤</span>
            <span class="status-dot" :class="{ online: isAuthenticated }"></span>
          </div>
          <ol>
            <li>
              <span>1</span>
              <div>
                <strong>先在主站登录</strong>
                <p>打开 Client A，使用用户名和密码登录 Identity。</p>
              </div>
            </li>
            <li>
              <span>2</span>
              <div>
                <strong>回到这个独立站点</strong>
                <p>本站没有读取主站的 sessionStorage。</p>
              </div>
            </li>
            <li>
              <span>3</span>
              <div>
                <strong>发起 SSO 授权</strong>
                <p>若不再出现密码页而直接返回，SSO 即已成立。</p>
              </div>
            </li>
          </ol>
          <div class="actions">
            <a class="secondary-button" :href="mainWebUrl" target="_blank" rel="noopener">
              打开主站 ↗
            </a>
            <button class="primary-button" :disabled="busy" @click="startSso">
              {{ isAuthenticated ? '重新验证 SSO' : '发起 SSO 授权' }}
              <span aria-hidden="true">→</span>
            </button>
          </div>
          <p v-if="error" class="error" role="alert">
            {{ error }}<button @click="error = ''">关闭</button>
          </p>
        </article>

        <article class="result-panel" :class="{ success: isAuthenticated }">
          <template v-if="busy">
            <div class="loader" aria-hidden="true"></div>
            <p class="kicker">CLIENT B · CALLBACK</p>
            <h2>正在读取本站会话</h2>
            <p>校验本地 sessionStorage 中的 Token 状态。</p>
          </template>
          <template v-else-if="isAuthenticated">
            <div class="success-mark" aria-hidden="true">✓</div>
            <p class="kicker">SSO VERIFIED</p>
            <h2>单点登录成功</h2>
            <p v-if="ssoSucceeded" class="elapsed">
              授权往返约 {{ elapsed }} ms，没有再次提交密码。
            </p>
            <dl>
              <div v-for="claim in claims" :key="claim[0]">
                <dt>{{ claim[0] }}</dt>
                <dd>{{ claim[1] }}</dd>
              </div>
            </dl>
            <div class="token-note">
              <i></i>
              <span>Client B 已获得自己的 JWT<br />主站 Token 仍留在 Client A</span>
            </div>
            <div class="result-actions">
              <button @click="clearLocalToken">仅清除本站 Token</button>
              <button @click="logoutIdentity">退出 Identity SSO</button>
            </div>
          </template>
          <template v-else>
            <div class="empty-mark" aria-hidden="true">?</div>
            <p class="kicker">WAITING FOR AUTHORIZATION</p>
            <h2>本站还没有 Token</h2>
            <p>
              即使主站已经登录，这里仍不会自动拥有主站的 Token。点击授权后，Identity 会根据共享
              Cookie 判断是否需要再次输入密码。
            </p>
          </template>
        </article>
      </section>
    </main>

    <footer>
      <span>独立 Origin：{{ currentOrigin }}</span>
      <span>Authority：{{ identityAuthority }}</span>
    </footer>
  </div>
</template>
