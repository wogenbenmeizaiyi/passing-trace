import { createRouter, createWebHistory } from 'vue-router'

import CallbackView from '@/views/CallbackView.vue'
import HomeView from '@/views/HomeView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/', name: 'home', component: HomeView },
    { path: '/auth/callback', name: 'callback', component: CallbackView },
    { path: '/auth/logout-callback', name: 'logout-callback', component: CallbackView },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

export default router
