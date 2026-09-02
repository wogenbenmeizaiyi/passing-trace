import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/', name: 'home', component: () => import('@/views/HomeView.vue') },
    { path: '/events', name: 'events', component: () => import('@/views/EventsListView.vue') },
    {
      path: '/events/new',
      name: 'events-new',
      component: () => import('@/views/EventFormView.vue'),
    },
    {
      path: '/events/:id',
      name: 'events-detail',
      component: () => import('@/views/EventDetailView.vue'),
    },
    {
      path: '/events/:id/edit',
      name: 'events-edit',
      component: () => import('@/views/EventFormView.vue'),
    },
    { path: '/assistant', name: 'assistant', component: () => import('@/views/AssistantView.vue') },
    {
      path: '/storylines',
      name: 'storylines',
      component: () => import('@/views/StorylinesListView.vue'),
    },
    {
      path: '/storylines/new',
      name: 'storylines-new',
      component: () => import('@/views/StorylineEditorView.vue'),
    },
    {
      path: '/storylines/:id',
      name: 'storylines-detail',
      component: () => import('@/views/StorylineDetailView.vue'),
    },
    {
      path: '/storylines/:id/edit',
      name: 'storylines-edit',
      component: () => import('@/views/StorylineEditorView.vue'),
    },
    {
      path: '/storylines/:id/revisions/:revision',
      name: 'storylines-revision',
      component: () => import('@/views/StorylineDetailView.vue'),
    },
    {
      path: '/auth/callback',
      name: 'auth-callback',
      component: () => import('@/views/AuthCallbackView.vue'),
    },
    {
      path: '/auth/logout-callback',
      name: 'auth-logout-callback',
      component: () => import('@/views/AuthLogoutCallbackView.vue'),
    },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

export default router
