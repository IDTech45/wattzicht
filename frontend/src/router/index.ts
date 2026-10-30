import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    name: 'home',
    component: () => import('@/views/HomeView.vue'),
    meta: { title: 'Wattzicht – zie wat je bespaart' },
  },
  {
    path: '/installateurs',
    name: 'installers',
    component: () => import('@/views/InstallersView.vue'),
    meta: { title: 'Voor installateurs – Wattzicht' },
  },
  {
    path: '/app',
    name: 'app',
    component: () => import('@/views/AppDownloadView.vue'),
    meta: { title: 'Download de app – Wattzicht' },
  },
  {
    path: '/privacy',
    name: 'privacy',
    component: () => import('@/views/PrivacyView.vue'),
    meta: { title: 'Privacy – Wattzicht' },
  },
  {
    path: '/portal/login',
    name: 'portal-login',
    component: () => import('@/views/portal/LoginView.vue'),
    meta: { title: 'Inloggen – Wattzicht portal', layout: 'portal' },
  },
  {
    path: '/portal',
    component: () => import('@/views/portal/PortalLayout.vue'),
    meta: { requiresAuth: true, layout: 'portal' },
    children: [
      {
        path: '',
        name: 'portal-home',
        component: () => import('@/views/portal/HouseholdsView.vue'),
        meta: { title: 'Klanten – Wattzicht portal' },
      },
      {
        path: 'households/:id',
        name: 'portal-household',
        component: () => import('@/views/portal/HouseholdDetailView.vue'),
        meta: { title: 'Klant – Wattzicht portal' },
      },
      {
        path: 'branding',
        name: 'portal-branding',
        component: () => import('@/views/portal/BrandingView.vue'),
        meta: { title: 'Jouw bedrijf – Wattzicht portal' },
      },
    ],
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('@/views/NotFoundView.vue'),
    meta: { title: 'Pagina niet gevonden – Wattzicht' },
  },
]

const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior(to, _from, saved) {
    if (saved) return saved
    if (to.hash) return { el: to.hash, top: 80, behavior: 'smooth' }
    return { top: 0 }
  },
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  if (to.meta.requiresAuth && !auth.isLoggedIn) {
    return { name: 'portal-login', query: { redirect: to.fullPath } }
  }
  if (to.name === 'portal-login' && auth.isLoggedIn) {
    return { name: 'portal-home' }
  }
})

router.afterEach((to) => {
  const title = to.meta.title as string | undefined
  if (title) document.title = title
})

export default router
