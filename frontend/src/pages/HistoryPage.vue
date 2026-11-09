<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NAlert, NButton, NCard, NEmpty, NSpin, NTag } from 'naive-ui'
import { api, ApiError, type SessionListItem } from '../api'

const router = useRouter()
const sessions = ref<SessionListItem[]>([])
const loading = ref(true)
const error = ref('')

const statuses: Record<string, { text: string; type: 'success' | 'warning' | 'default' }> = {
  completed: { text: '已完成', type: 'success' },
  active: { text: '未完成', type: 'warning' },
  abandoned: { text: '已放弃', type: 'default' }
}

onMounted(async () => {
  try {
    sessions.value = await api.listSessions()
  } catch (err) {
    error.value = err instanceof ApiError ? err.message : '请求失败，请确认后端已启动'
  } finally {
    loading.value = false
  }
})

function formatDate(value: string) {
  return new Date(value).toLocaleString()
}
</script>

<template>
  <section>
    <header class="page-head">
      <h1>历史记录</h1>
      <p class="muted small">每场面试的岗位、进度和分数；已完成的可以直接看报告，没答完的接着答。</p>
    </header>

    <NAlert v-if="error" type="error" closable style="margin-bottom: 12px" @close="error = ''">{{ error }}</NAlert>

    <div v-if="loading" class="row">
      <NSpin size="small" />
      <span class="muted small">加载历史…</span>
    </div>

    <NEmpty v-else-if="sessions.length === 0" description="还没有面试记录，先去「匹配」页开始一场" style="margin-top: 48px" />

    <div v-else class="stack">
      <NCard v-for="session in sessions" :key="session.id" size="small">
        <div class="row" style="gap: 12px">
          <div style="min-width: 0">
            <div class="row">
              <b>{{ session.jobTitle || '未命名岗位' }}</b>
              <NTag size="small" :type="statuses[session.status]?.type ?? 'default'">
                {{ statuses[session.status]?.text ?? session.status }}
              </NTag>
              <NTag v-if="session.overallScore !== null && session.overallScore !== undefined" size="small" type="info">
                {{ session.overallScore }} 分
              </NTag>
            </div>
            <div class="muted small">
              简历：{{ session.resumeTitle || '—' }} · 开始于 {{ formatDate(session.startedAt) }}
            </div>
          </div>
          <span class="spacer" />
          <span class="muted small">{{ session.answeredCount }} / {{ session.questionCount }} 题</span>
          <NButton
            v-if="session.status === 'completed'"
            size="small"
            type="primary"
            @click="router.push(`/report/${session.id}`)"
          >
            查看报告
          </NButton>
          <NButton v-else size="small" @click="router.push(`/practice/${session.id}`)">继续练习</NButton>
        </div>
      </NCard>
    </div>
  </section>
</template>
