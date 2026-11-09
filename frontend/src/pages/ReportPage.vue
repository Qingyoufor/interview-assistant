<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NAlert, NButton, NCard, NProgress, NSpin, NTag } from 'naive-ui'
import { api, ApiError, type SessionReport } from '../api'

const route = useRoute()
const router = useRouter()
const sessionId = String(route.params.sessionId)

const loading = ref(true)
const completing = ref(false)
const error = ref('')
const report = ref<SessionReport | null>(null)
const incomplete = ref(false)
const saved = ref(false)

const dimensionLabels: Record<string, string> = {
  structure: '结构',
  depth: '深度',
  evidence: '证据',
  delivery: '表达'
}

function fail(err: unknown) {
  error.value = err instanceof ApiError ? err.message : '请求失败，请确认后端已启动'
}

onMounted(async () => {
  try {
    report.value = await api.getReport(sessionId)
  } catch (err) {
    if (err instanceof ApiError && err.status === 409) {
      incomplete.value = true
      error.value = err.message
    } else {
      fail(err)
    }
  } finally {
    loading.value = false
  }
})

async function saveToSession() {
  completing.value = true
  error.value = ''
  try {
    report.value = await api.completeSession(sessionId)
    saved.value = true
  } catch (err) {
    fail(err)
  } finally {
    completing.value = false
  }
}
</script>

<template>
  <section>
    <header class="page-head">
      <h1>报告</h1>
      <p class="muted small">四维均分换算成总分，加权拆分与「最该补的 3 条证据」都在这一页。</p>
    </header>

    <NAlert v-if="error" type="error" closable style="margin-bottom: 12px" @close="error = ''">{{ error }}</NAlert>

    <div v-if="loading" class="row">
      <NSpin size="small" />
      <span class="muted small">加载报告…</span>
    </div>

    <NCard v-else-if="incomplete" size="small">
      <p>这场练习还没答完，先把剩下的题答完再看报告。</p>
      <div class="row">
        <NButton type="primary" @click="router.push(`/practice/${sessionId}`)">继续练习</NButton>
      </div>
    </NCard>

    <template v-else-if="report">
      <NCard size="small">
        <template #header>
          <div class="row">
            <span>练习报告</span>
            <NTag v-if="saved" size="small" type="success">已保存到会话记录</NTag>
            <span class="spacer" />
            <NButton size="small" :loading="completing" @click="saveToSession">保存到会话记录</NButton>
          </div>
        </template>
        <div class="row" style="align-items: flex-end; gap: 24px">
          <div>
            <div class="score">
              {{ report.overallScore }}<span class="score-unit">/ 100</span>
            </div>
            <div class="muted small">练习总分</div>
          </div>
          <div class="stack" style="flex: 1">
            <div class="row small">
              <span class="muted">作答</span>
              <b>{{ report.answeredQuestions }} / {{ report.totalQuestions }}</b>
              <span class="muted">岗位匹配</span>
              <b>{{ report.matchOverall }}</b>
              <NTag v-if="report.hardBlock" size="small" type="error">硬性要求缺失</NTag>
            </div>
            <p style="margin: 0">{{ report.summary }}</p>
          </div>
        </div>
      </NCard>

      <div class="grid-2" style="margin-top: 16px">
        <NCard size="small">
          <template #header>四维均分</template>
          <div v-for="dimension in report.dimensions" :key="dimension.dimension" class="dim">
            <span class="muted small">{{ dimensionLabels[dimension.dimension] ?? dimension.dimension }}</span>
            <NProgress
              type="line"
              :percentage="(dimension.average / 5) * 100"
              :show-indicator="false"
              :height="6"
            />
            <span class="small">{{ dimension.average.toFixed(2) }} / 5</span>
          </div>
        </NCard>

        <NCard size="small">
          <template #header>加权匹配拆分</template>
          <div class="breakdown" style="grid-template-columns: repeat(2, minmax(0, 1fr))">
            <div class="breakdown-item">
              <div class="num">{{ report.breakdown.mustHave }}<span class="muted small"> / 45</span></div>
              <div class="muted small">硬性要求</div>
            </div>
            <div class="breakdown-item">
              <div class="num">{{ report.breakdown.responsibilities }}<span class="muted small"> / 25</span></div>
              <div class="muted small">职责</div>
            </div>
            <div class="breakdown-item">
              <div class="num">{{ report.breakdown.evidenceQuality }}<span class="muted small"> / 20</span></div>
              <div class="muted small">证据质量</div>
            </div>
            <div class="breakdown-item">
              <div class="num">{{ report.breakdown.bonus }}<span class="muted small"> / 10</span></div>
              <div class="muted small">加分</div>
            </div>
          </div>
        </NCard>
      </div>

      <NCard size="small" style="margin-top: 16px">
        <template #header>最该补的证据</template>
        <div v-for="(gap, index) in report.topGaps" :key="index" class="stack" style="margin-bottom: 12px">
          <div class="row">
            <NTag size="small" type="error">缺口 {{ index + 1 }}</NTag>
            <b>{{ gap.requirement }}</b>
          </div>
          <div class="small">{{ gap.remediation }}</div>
          <div class="muted small">对应题目：{{ gap.question }}</div>
        </div>
        <p v-if="report.topGaps.length === 0" class="muted small">本场没有生成缺口建议（缺口题都没答或都答得不错）。</p>
      </NCard>

      <div class="grid-2" style="margin-top: 16px">
        <NCard size="small">
          <template #header>弱点标签（本场）</template>
          <div v-if="report.weaknesses.length" class="row">
            <NTag v-for="weakness in report.weaknesses" :key="weakness.tag" size="small" type="warning">
              {{ weakness.tag }} × {{ weakness.count }}
            </NTag>
          </div>
          <p v-else class="muted small">本场没有记录到弱点。</p>
        </NCard>

        <NCard size="small">
          <template #header>做得好的地方</template>
          <ul v-if="report.strengths.length" class="list-plain small">
            <li v-for="(item, index) in report.strengths" :key="index">{{ item }}</li>
          </ul>
          <p v-else class="muted small">还没有足够数据。</p>
        </NCard>
      </div>

      <NCard v-if="report.topRisks.length" size="small" style="margin-top: 16px">
        <template #header>最可能被问倒的点</template>
        <ul class="list-plain small">
          <li v-for="(risk, index) in report.topRisks" :key="index">{{ risk }}</li>
        </ul>
      </NCard>
    </template>
  </section>
</template>
