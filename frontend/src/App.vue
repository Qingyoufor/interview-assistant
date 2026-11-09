<script setup lang="ts">
import { NConfigProvider } from 'naive-ui'
import { RouterLink, RouterView, useRoute } from 'vue-router'

const route = useRoute()

const links = [
  { to: '/resumes', label: '资料' },
  { to: '/match', label: '匹配' },
  { to: '/history', label: '历史' }
]

// naive-ui 默认主色是绿色，这里跟页面里的蓝色品牌色对齐
const themeOverrides = {
  common: {
    primaryColor: '#2563eb',
    primaryColorHover: '#1d4ed8',
    primaryColorPressed: '#1e40af',
    primaryColorSuppl: '#2563eb',
    borderRadius: '8px'
  }
}
</script>

<template>
  <NConfigProvider :theme-overrides="themeOverrides">
    <div class="shell">
      <header class="topbar">
        <RouterLink class="brand" to="/resumes">Interview Coach</RouterLink>
        <nav class="nav">
          <RouterLink
            v-for="link in links"
            :key="link.to"
            :to="link.to"
            class="nav-link"
            :class="{ active: route.path.startsWith(link.to) }"
          >
            {{ link.label }}
          </RouterLink>
        </nav>
        <span class="tagline">简历 × JD · 缺口驱动</span>
      </header>

      <main class="main">
        <RouterView />
      </main>
    </div>
  </NConfigProvider>
</template>
