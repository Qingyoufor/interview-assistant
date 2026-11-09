<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NAlert, NButton, NCard, NCheckbox, NEmpty, NSelect, NSpin, NTag } from 'naive-ui'
import { api, ApiError, type JobTargetListItem, type MatchResponse, type ResumeListItem } from '../api'

const STORE_KEY = 'interview-assistant:last-match'
const route = useRoute()
const router = useRouter()

const resumes = ref<ResumeListItem[]>([])
const jobs = ref<JobTargetListItem[]>([])
const resumeId = ref<string | null>(null)
const jobTargetId = ref<string | null>(null)
const forceRefresh = ref(false)
const loading = ref(false)
const starting = ref(false)
const error = ref('')
const match = ref<MatchResponse | null>(null)

const resumeOptions = computed(() => resumes.value.map((item) => ({ label: item.title, value: item.id })))
const jobOptions = computed(() =>
  jobs.value.map((item) => ({ label: item.level ? `${item.title}（${item.level}）` : item.title, value: item.id }))
)

const verdicts: Record<string, { text: string; className: string }> = {
  strong: { text: '有证据', className: 'verdict-strong' },
  partial: { text: '部分匹配', className: 'verdict-partial' },
  missing: { text: '缺失', className: 'verdict-missing' }
}

const categories: Record<string, string> = {
  mustHave: '硬性要求',
  responsibility: '职责',
  bonus: '加分'
}

function fail(err: unknown) {
  error.value = err instanceof ApiError ? err.message : '请求失败，请确认后端已启动'
}

onMounted(async () => {
  try {
    const [nextResumes, nextJobs] = await Promise.all([api.listResumes(), api.listJobTargets()])
    resumes.value = nextResumes
    jobs.value = nextJobs

    const saved = localStorage.getItem(STORE_KEY)
    const parsed = saved ? (JSON.parse(saved) as { resumeId?: string; jobTargetId?: string }) : {}
    const queryResume = typeof route.query.resumeId === 'string' ? route.query.resumeId : undefined
    const queryJob = typeof route.query.jobTargetId === 'string' ? route.query.jobTargetId : undefined

    resumeId.value = queryResume ?? parsed.resumeId ?? nextResumes[0]?.id ?? null
    jobTargetId.value = queryJob ?? parsed.jobTargetId ?? nextJobs[0]?.id ?? null

    // 支持 /match?resumeId=..&jobTargetId=..&auto=1 直接出结果（报告页「再练一场」也走这条路）
    if (route.query.auto === '1' && resumeId.value && jobTargetId.value) await runMatch()
  } catch (err) {
    fail(err)
  }
})

async function runMatch() {
  if (!resumeId.value || !jobTargetId.value) return
  loading.value = true
  error.value = ''
  try {
    match.value = await api.createMatch(resumeId.value, jobTargetId.value, forceRefresh.value)
    localStorage.setItem(
      STORE_KEY,
      JSON.stringify({ resumeId: resumeId.value, jobTargetId: jobTargetId.value })
    )
  } catch (err) {
    fail(err)
  } finally {
    loading.value = false
  }
}

async function startPractice() {
  if (!resumeId.value || !jobTargetId.value) return
  starting.value = true
  error.value = ''
  try {
    const session = await api.createSession(resumeId.value, jobTargetId.value)
    await router.push(`/practice/${session.id}`)
  } catch (err) {
    fail(err)
  } finally {
    starting.value = false
  }
}
</script>

<template>
  <section>
    <header class="page-head">
      <h1>匹配</h1>
      <p class="muted small">简历 × JD 逐条对齐，分数由四项加权得出，硬性要求缺失会直接阻断。</p>
    </header>

    <NAlert v-if="error" type="error" closable style="margin-bottom: 12px" @close="error = ''">{{ error }}</NAlert>

    <NCard size="small">
      <div class="row" style="gap: 12px">
        <NSelect
          v-model:value="resumeId"
          :options="resumeOptions"
          placeholder="选择简历"
          style="min-width: 240px; flex: 1"
        />
        <NSelect
          v-model:value="jobTargetId"
          :options="jobOptions"
          placeholder="选择 JD"
          style="min-width: 240px; flex: 1"
        />
        <NCheckbox v-model:checked="forceRefresh">强制重算</NCheckbox>
        <NButton
          type="primary"
          :loading="loading"
          :disabled="!resumeId || !jobTargetId"
          @click="runMatch"
        >
          开始匹配
        </NButton>
      </div>
      <p v-if="resumes.length === 0 || jobs.length === 0" class="muted small" style="margin: 8px 0 0">
        先去「资料」页录入至少一份简历和一份 JD。
      </p>
    </NCard>

    <div v-if="loading" class="row" style="margin-top: 16px">
      <NSpin size="small" />
      <span class="muted small">匹配中，通常 10～30 秒…</span>
    </div>

    <NCard v-if="match" size="small" style="margin-top: 16px">
      <template #header>
        <div class="row">
          <span>对齐结果</span>
          <NTag v-if="match.cached" size="small" type="info">复用上次结果</NTag>
          <NTag v-if="match.result.hardBlock" size="small" type="error">硬性要求缺失</NTag>
        </div>
      </template>

      <div class="row" style="align-items: flex-end; gap: 24px">
        <div>
          <div class="score">
            {{ match.result.overallMatch }}<span class="score-unit">/ 100</span>
          </div>
          <div class="muted small">加权总分</div>
        </div>
        <div class="breakdown" style="flex: 1">
          <div class="breakdown-item">
            <div class="num">{{ match.result.breakdown.mustHave }}<span class="muted small"> / 45</span></div>
            <div class="muted small">硬性要求</div>
          </div>
          <div class="breakdown-item">
            <div class="num">{{ match.result.breakdown.responsibilities }}<span class="muted small"> / 25</span></div>
            <div class="muted small">职责</div>
          </div>
          <div class="breakdown-item">
            <div class="num">{{ match.result.breakdown.evidenceQuality }}<span class="muted small"> / 20</span></div>
            <div class="muted small">证据质量</div>
          </div>
          <div class="breakdown-item">
            <div class="num">{{ match.result.breakdown.bonus }}<span class="muted small"> / 10</span></div>
            <div class="muted small">加分</div>
          </div>
        </div>
      </div>

      <NAlert v-if="match.result.hardBlock" type="warning" style="margin: 16px 0">
        有硬性要求被判为缺失，投递前必须补上或换岗位。
      </NAlert>

      <table v-if="match.result.items.length" class="table" style="margin-top: 16px">
        <thead>
          <tr>
            <th style="width: 28%">要求</th>
            <th style="width: 10%">分类</th>
            <th style="width: 10%">判定</th>
            <th>简历证据</th>
            <th style="width: 20%">可能被追问</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(item, index) in match.result.items" :key="index">
            <td>{{ item.requirement }}</td>
            <td class="muted small">{{ categories[item.category] ?? item.category }}</td>
            <td :class="verdicts[item.verdict]?.className">{{ verdicts[item.verdict]?.text ?? item.verdict }}</td>
            <td class="small">
              <div v-if="item.evidence">{{ item.evidence }}</div>
              <div v-if="item.resumeAnchorIds.length" class="mono muted">
                {{ item.resumeAnchorIds.join('、') }}
              </div>
              <span v-if="!item.evidence && !item.resumeAnchorIds.length" class="muted">无</span>
            </td>
            <td class="small muted">{{ item.risk || '—' }}</td>
          </tr>
        </tbody>
      </table>

      <div v-if="match.result.topRisks.length" style="margin-top: 16px">
        <b>最可能被问倒的点</b>
        <ul class="list-plain">
          <li v-for="(risk, index) in match.result.topRisks" :key="index">{{ risk }}</li>
        </ul>
      </div>

      <div class="row" style="margin-top: 16px">
        <span class="muted small">下一步会按「简历 3 / 缺口 3 / 通用 2」出 8 道题。</span>
        <span class="spacer" />
        <NButton type="primary" :loading="starting" @click="startPractice">开始练习</NButton>
      </div>
    </NCard>

    <NEmpty v-if="!match && !loading" description="选好简历和 JD，点「开始匹配」" style="margin-top: 48px" />
  </section>
</template>
