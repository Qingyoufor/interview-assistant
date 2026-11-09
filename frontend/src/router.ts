import { createRouter, createWebHistory } from 'vue-router'

// 路由级懒加载：每个页面各自一个 chunk，首屏只加载当前页
export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/resumes' },
    { path: '/resumes', component: () => import('./pages/ResumesPage.vue') },
    { path: '/match', component: () => import('./pages/MatchPage.vue') },
    { path: '/history', component: () => import('./pages/HistoryPage.vue') },
    { path: '/practice/:sessionId', component: () => import('./pages/PracticePage.vue') },
    { path: '/report/:sessionId', component: () => import('./pages/ReportPage.vue') },
    { path: '/:pathMatch(.*)*', redirect: '/resumes' }
  ]
})
