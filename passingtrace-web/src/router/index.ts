import { createRouter, createWebHistory } from 'vue-router'

import AuthCallbackView from '@/views/AuthCallbackView.vue'
import AuthLogoutCallbackView from '@/views/AuthLogoutCallbackView.vue'
import EventDetailView from '@/views/EventDetailView.vue'
import EventFormView from '@/views/EventFormView.vue'
import EventsListView from '@/views/EventsListView.vue'
import HomeView from '@/views/HomeView.vue'
import AssistantView from '@/views/AssistantView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/', name: 'home', component: HomeView },
    { path: '/events', name: 'events', component: EventsListView },
    { path: '/events/new', name: 'events-new', component: EventFormView },
    { path: '/events/:id', name: 'events-detail', component: EventDetailView },
    { path: '/events/:id/edit', name: 'events-edit', component: EventFormView },
    { path: '/assistant', name: 'assistant', component: AssistantView },
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
