<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NAlert, NButton, NCard, NInput, NProgress, NSpin, NTag } from 'naive-ui'
import {
  api,
  ApiError,
  type Anchor,
  type Assessment,
  type SessionQuestion,
  type SessionResponse
} from '../api'

type Phase = 'answer' | 'followup' | 'scored' | 'done'

const route = useRoute()
const router = useRouter()
const sessionId = String(route.params.sessionId)

const session = ref<SessionResponse | null>(null)
const anchors = ref<Record<string, Anchor>>({})
const loading = ref(true)
const submitting = ref(false)
const error = ref('')

const index = ref(0)
const phase = ref<Phase>('answer')
const answerText = ref('')
const followUpText = ref('')
const assessment = ref<Assessment | null>(null)

const current = computed<SessionQuestion | null>(() => session.value?.questions[index.value] ?? null)
const total = computed(() => session.value?.questions.length ?? 0)
const answeredCount = computed(
  () =>
    session.value?.questions.filter(
      (question) => question.answered && (!question.followUp || question.followUpAnswered)
    ).length ?? 0
)
const percent = computed(() => (total.value === 0 ? 0 : Math.round((answeredCount.value / total.value) * 100)))
const canSubmit = computed(() => answerText.value.trim().length >= 10 && !submitting.value)

const sourceLabels: Record<string, string> = {
  resume: '简历',
  gap: '缺口',
  general: '通用'
}

const typeLabels: Record<string, string> = {
  basic: '基础',
  project: '项目',
  scenario: '场景',
  behavior: '行为'
}

const dimensions: { key: keyof Assessment['display']; label: string }[] = [
  { key: 'structure', label: '结构' },
  { key: 'depth', label: '深度' },
  { key: 'evidence', label: '证据' },
  { key: 'delivery', label: '表达' }
]

const verdictLabels: Record<string, string> = {
  supported: '已被材料支撑',
  unverified: '没有支撑',
  contradicted: '与简历冲突',
  not_in_material: '简历里没有'
}

function fail(err: unknown) {
  error.value = err instanceof ApiError ? err.message : '请求失败，请确认后端已启动'
}

function goNext() {
  const list = session.value?.questions
  if (!list) return
  const next = list.findIndex((question) => !question.answered || (question.followUp && !question.followUpAnswered))
  if (next === -1) {
    phase.value = 'done'
    return
  }
  index.value = next
  phase.value = 'answer'
  assessment.value = null
  answerText.value = ''
}

function positionOf(questions: SessionQuestion[]): { index: number; phase: Phase } {
  const pending = questions.findIndex(
    (question) => !question.answered || (question.followUp && !question.followUpAnswered)
  )
  if (pending === -1) return { index: questions.length, phase: 'done' }

  const question = questions[pending]
  const needsFollowUp = question.answered && !!question.followUp && !question.followUpAnswered
  return { index: pending, phase: needsFollowUp ? 'followup' : 'answer' }
}

function applyPosition(loaded: SessionResponse) {
  session.value = loaded
  const position = positionOf(loaded.questions)
  index.value = position.index
  phase.value = position.phase
  followUpText.value = loaded.questions[position.index]?.followUp ?? ''
  assessment.value = null
}

/** 提交遇到 409（已作答、或响应丢失）时以服务端为准重新对位，而不是卡在旧状态。 */
async function resync() {
  try {
    applyPosition(await api.getSession(sessionId))
    answerText.value = ''
  } catch {
    // 再同步也失败就保留错误提示，用户可以刷新页面
  }
}

onMounted(async () => {
  try {
    const loaded = await api.getSession(sessionId)
    applyPosition(loaded)

    // 锚点只影响「引用原文」的展示，加载失败不该阻塞答题
    try {
      const [resume, job] = await Promise.all([api.getResume(loaded.resumeId), api.getJobTarget(loaded.jobTargetId)])
      const map: Record<string, Anchor> = {}
      for (const anchor of [...resume.anchors, ...job.anchors]) map[anchor.id] = anchor
      anchors.value = map
    } catch {
      anchors.value = {}
    }
  } catch (err) {
    fail(err)
  } finally {
    loading.value = false
  }
})

async function submitAnswer() {
  const question = current.value
  if (!question) return

  submitting.value = true
  error.value = ''
  try {
    const response = await api.answer(sessionId, question.id, answerText.value.trim())
    question.answered = true
    question.assessment = response.assessment
    assessment.value = response.assessment

    if (response.followUp) {
      question.followUp = response.followUp
      followUpText.value = response.followUp
      phase.value = 'followup'
    } else {
      phase.value = 'scored'
    }
    answerText.value = ''
  } catch (err) {
    fail(err)
    if (err instanceof ApiError && err.status === 409) await resync()
  } finally {
    submitting.value = false
  }
}

async function submitFollowUp() {
  const question = current.value
  if (!question) return

  submitting.value = true
  error.value = ''
  try {
    const response = await api.followUp(sessionId, question.id, answerText.value.trim())
    question.followUpAnswered = true
    question.assessment = response.assessment
    assessment.value = response.assessment
    phase.value = 'scored'
    answerText.value = ''
  } catch (err) {
    fail(err)
    if (err instanceof ApiError && err.status === 409) await resync()
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <section>
    <header class="page-head">
      <h1>练习</h1>
      <p class="muted small">一题一题来。回答后先评分，再决定追问还是下一题。</p>
    </header>

    <NAlert v-if="error" type="error" closable style="margin-bottom: 12px" @close="error = ''">{{ error }}</NAlert>

    <div v-if="loading" class="row">
      <NSpin size="small" />
      <span class="muted small">加载会话…</span>
    </div>

    <template v-else-if="session">
      <NCard size="small">
        <div class="row">
          <span class="muted small">进度</span>
          <NProgress
            type="line"
            :percentage="percent"
            :show-indicator="false"
            :height="6"
            style="flex: 1; min-width: 200px"
          />
          <span class="muted small">{{ answeredCount }} / {{ total }}</span>
        </div>
      </NCard>

      <NCard v-if="phase === 'answer' && current" size="small" style="margin-top: 16px">
        <template #header>
          <div class="row">
            <span>第 {{ index + 1 }} / {{ total }} 题</span>
            <NTag size="small" :type="current.source === 'gap' ? 'error' : current.source === 'resume' ? 'success' : 'default'">
              {{ sourceLabels[current.source] ?? current.source }}
            </NTag>
            <NTag size="small">{{ typeLabels[current.type] ?? current.type }}</NTag>
          </div>
        </template>

        <p class="question-text">{{ current.content }}</p>
        <p v-if="current.intent" class="muted small">出题意图：{{ current.intent }}</p>

        <div v-if="current.anchorIds.length" style="margin-top: 8px">
          <div v-for="id in current.anchorIds" :key="id">
            <details v-if="anchors[id]" class="anchor">
              <summary>
                <span class="mono">{{ id }}</span> · {{ anchors[id].label }}
                <span class="muted small">（{{ anchors[id].kind }}）</span>
              </summary>
              <p class="anchor-quote">{{ anchors[id].quote }}</p>
            </details>
          </div>
        </div>

        <div class="stack" style="margin-top: 16px">
          <NInput
            v-model:value="answerText"
            type="textarea"
            :autosize="{ minRows: 6, maxRows: 14 }"
            placeholder="按「结论 → 具体做法 → 数字/结果 → 边界」展开，至少 10 个字。"
          />
          <div class="row">
            <span class="muted small">{{ answerText.trim().length }} 字</span>
            <span class="spacer" />
            <NButton type="primary" :loading="submitting" :disabled="!canSubmit" @click="submitAnswer">
              提交回答
            </NButton>
          </div>
        </div>
      </NCard>

      <NCard v-if="phase === 'followup' && current" size="small" style="margin-top: 16px">
        <template #header>
          <div class="row">
            <span>第 {{ index + 1 }} / {{ total }} 题 · 追问</span>
            <NTag size="small" type="warning">追问</NTag>
          </div>
        </template>

        <div class="followup">
          <div class="muted small">面试官追问</div>
          <p class="question-text" style="font-size: 16px">{{ followUpText }}</p>
        </div>
        <p class="muted small">原题：{{ current.content }}</p>

        <div class="stack">
          <NInput
            v-model:value="answerText"
            type="textarea"
            :autosize="{ minRows: 5, maxRows: 12 }"
            placeholder="把追问的点补上：具体是谁做的、数字是多少、换个条件会怎样。"
          />
          <div class="row">
            <span class="muted small">{{ answerText.trim().length }} 字</span>
            <span class="spacer" />
            <NButton type="primary" :loading="submitting" :disabled="!canSubmit" @click="submitFollowUp">
              提交追问
            </NButton>
          </div>
        </div>
      </NCard>

      <NCard v-if="phase === 'scored' && assessment" size="small" style="margin-top: 16px">
        <template #header>
          <div class="row">
            <span>本题评分</span>
            <NTag v-if="assessment.hasFactualError" size="small" type="error">有事实错误</NTag>
          </div>
        </template>

        <div class="score-card">
          <div
            v-for="item in dimensions"
            :key="item.key"
            class="dim"
          >
            <span class="muted small">{{ item.label }}</span>
            <NProgress
              type="line"
              :percentage="(assessment.display[item.key] / 5) * 100"
              :show-indicator="false"
              :height="6"
            />
            <span class="small">{{ assessment.display[item.key] }} / 5</span>
          </div>
        </div>

        <div v-if="assessment.weaknesses.length" class="row" style="margin-top: 12px">
          <span class="muted small">弱点标签</span>
          <NTag v-for="tag in assessment.weaknesses" :key="tag" size="small" type="warning">{{ tag }}</NTag>
        </div>

        <div v-if="assessment.claimChecks.length" style="margin-top: 12px">
          <b class="small">声明核验</b>
          <ul class="list-plain small">
            <li v-for="(check, i) in assessment.claimChecks" :key="i">
              {{ check.claim }}
              <span class="muted">— {{ verdictLabels[check.verdict] ?? check.verdict }}</span>
            </li>
          </ul>
        </div>

        <div v-if="assessment.improvements.length" style="margin-top: 12px">
          <b class="small">可以怎么改</b>
          <ul class="list-plain small">
            <li v-for="(line, i) in assessment.improvements" :key="i">{{ line }}</li>
          </ul>
        </div>

        <div v-if="assessment.remediation" class="followup" style="margin-top: 12px">
          <div class="muted small">这个短板怎么补</div>
          <p>{{ assessment.remediation }}</p>
        </div>

        <div class="row" style="margin-top: 16px">
          <span class="spacer" />
          <NButton type="primary" @click="goNext">下一题</NButton>
        </div>
      </NCard>

      <NCard v-if="phase === 'done'" size="small" style="margin-top: 16px">
        <p>8 道题都答完了，可以看报告。</p>
        <div class="row">
          <NButton type="primary" @click="router.push(`/report/${sessionId}`)">查看报告</NButton>
        </div>
      </NCard>
    </template>
  </section>
</template>
