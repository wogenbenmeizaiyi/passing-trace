import { createRouter, createWebHistory } from 'vue-router'

import AuthCallbackView from '@/views/AuthCallbackView.vue'
import AuthLogoutCallbackView from '@/views/AuthLogoutCallbackView.vue'
import HomeView from '@/views/HomeView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/', name: 'home', component: HomeView },
    { path: '/auth/callback', name: 'auth-callback', component: AuthCallbackView },
    {
      path: '/auth/logout-callback',
      name: 'auth-logout-callback',
      component: AuthLogoutCallbackView,
    },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

export default router
