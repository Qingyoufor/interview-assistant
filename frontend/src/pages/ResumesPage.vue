<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { NAlert, NButton, NCard, NEmpty, NInput, NModal, NPopconfirm, NSpin, NTabPane, NTabs, NTag } from 'naive-ui'
import { extractText } from '../extract'
import {
  api,
  ApiError,
  type Anchor,
  type JobProfile,
  type JobTargetListItem,
  type ResumeListItem,
  type ResumeProfile
} from '../api'

const mode = ref<string>('resume')
const title = ref('')
const body = ref('')
const busy = ref(false)
const error = ref('')
const notice = ref('')

const resumes = ref<ResumeListItem[]>([])
const jobs = ref<JobTargetListItem[]>([])

const resumeDetail = ref<{ id: string; title: string; profile: ResumeProfile; anchors: Anchor[] } | null>(null)
const jobDetail = ref<{ id: string; title: string; level: string; profile: JobProfile; anchors: Anchor[] } | null>(
  null
)

const fileInput = ref<HTMLInputElement | null>(null)
const extracting = ref(false)
const extractNote = ref('')

const canParse = computed(() => body.value.trim().length >= 50 && !busy.value && !extracting.value)

// 简历和 JD 各存一份草稿，切 tab 不会把另一类的内容带过去
const drafts: Record<string, { title: string; body: string }> = {
  resume: { title: '', body: '' },
  job: { title: '', body: '' }
}

watch(mode, (next, previous) => {
  drafts[previous] = { title: title.value, body: body.value }
  title.value = drafts[next]?.title ?? ''
  body.value = drafts[next]?.body ?? ''
  extractNote.value = ''
  error.value = ''
})

function fail(err: unknown) {
  error.value = err instanceof ApiError ? err.message : '请求失败，请确认后端已启动'
}

function pickFile(kind: 'pdf' | 'image') {
  const input = fileInput.value
  if (!input) return
  extractNote.value = ''
  error.value = ''
  input.accept = kind === 'pdf' ? 'application/pdf' : 'image/*'
  input.value = ''
  input.click()
}

async function onFileChange(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file) return

  const isPdf = file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf')
  extracting.value = true
  error.value = ''
  extractNote.value = isPdf ? '正在抽取 PDF 文字…' : '正在识别图片文字（第一次会下载语言包，可能要几十秒）…'

  try {
    const text = await extractText(file)
    const chars = text.replace(/\s/g, '').length

    if (chars === 0) {
      extractNote.value = ''
      error.value = '没识别出文字（扫描版 PDF 没有文字层，或图片太模糊）。可以换张清晰图片重试，或直接粘贴文本。'
      return
    }

    body.value = text
    extractNote.value = `已从「${file.name}」提取 ${chars} 字，确认无误后点「解析并保存」`
    error.value = chars < 50 ? '提取到的文字少于 50 字，可手工补充后再解析（输入框里已经是识别结果）。' : ''
  } catch (err) {
    extractNote.value = ''
    error.value = err instanceof Error ? err.message : '文件读取失败'
  } finally {
    extracting.value = false
  }
}

async function refreshLists() {
  try {
    const [nextResumes, nextJobs] = await Promise.all([api.listResumes(), api.listJobTargets()])
    resumes.value = nextResumes
    jobs.value = nextJobs
  } catch (err) {
    fail(err)
  }
}

async function parse() {
  busy.value = true
  error.value = ''
  notice.value = ''

  try {
    if (mode.value === 'resume') {
      const result = await api.createResume(title.value.trim(), body.value)
      resumeDetail.value = { id: result.id, title: result.title, profile: result.profile, anchors: result.anchors }
      jobDetail.value = null
      notice.value = result.truncated ? '超过 12000 字，已截断后解析' : `已解析并保存：${result.title}`
    } else {
      const result = await api.createJobTarget(title.value.trim(), body.value)
      jobDetail.value = {
        id: result.id,
        title: result.title,
        level: result.level,
        profile: result.profile,
        anchors: result.anchors
      }
      resumeDetail.value = null
      notice.value = result.truncated ? '超过 12000 字，已截断后解析' : `已解析并保存：${result.title}`
    }

    title.value = ''
    body.value = ''
    drafts[mode.value] = { title: '', body: '' }
    await refreshLists()
  } catch (err) {
    fail(err)
  } finally {
    busy.value = false
  }
}

async function openResume(id: string) {
  busy.value = true
  error.value = ''
  try {
    const detail = await api.getResume(id)
    resumeDetail.value = { id: detail.id, title: detail.title, profile: detail.profile, anchors: detail.anchors }
    jobDetail.value = null
    title.value = detail.title
    body.value = detail.rawText
    notice.value = '已载入原文，可修改后重新解析（会保存成一份新的）'
  } catch (err) {
    fail(err)
  } finally {
    busy.value = false
  }
}

async function openJob(id: string) {
  busy.value = true
  error.value = ''
  try {
    const detail = await api.getJobTarget(id)
    jobDetail.value = {
      id: detail.id,
      title: detail.title,
      level: detail.level,
      profile: detail.profile,
      anchors: detail.anchors
    }
    resumeDetail.value = null
    title.value = detail.title
    body.value = detail.rawText
    notice.value = '已载入原文，可修改后重新解析（会保存成一份新的）'
  } catch (err) {
    fail(err)
  } finally {
    busy.value = false
  }
}

/** 被面试记录引用时后端返回 409，用弹窗让用户确认是否连带删除。 */
const pending = ref<{ kind: 'resume' | 'job'; id: string; message: string } | null>(null)
const pendingVisible = computed({
  get: () => pending.value !== null,
  set: (visible: boolean) => {
    if (!visible) pending.value = null
  }
})

async function removeResume(id: string, force = false): Promise<void> {
  try {
    await api.deleteResume(id, force)
    if (resumeDetail.value?.id === id) resumeDetail.value = null
    notice.value = '简历已删除'
    error.value = ''
    await refreshLists()
  } catch (err) {
    if (err instanceof ApiError && err.status === 409 && !force) {
      pending.value = { kind: 'resume', id, message: err.message }
      return
    }
    fail(err)
  }
}

async function removeJob(id: string, force = false): Promise<void> {
  try {
    await api.deleteJobTarget(id, force)
    if (jobDetail.value?.id === id) jobDetail.value = null
    notice.value = 'JD 已删除'
    error.value = ''
    await refreshLists()
  } catch (err) {
    if (err instanceof ApiError && err.status === 409 && !force) {
      pending.value = { kind: 'job', id, message: err.message }
      return
    }
    fail(err)
  }
}

async function confirmPending() {
  const target = pending.value
  if (!target) return
  pending.value = null
  if (target.kind === 'resume') await removeResume(target.id, true)
  else await removeJob(target.id, true)
}

onMounted(refreshLists)
</script>

<template>
  <section>
    <header class="page-head">
      <h1>资料</h1>
      <p class="muted small">粘贴简历和 JD，解析出画像与证据锚点。锚点是后面「为什么问这道题」的依据。</p>
    </header>

    <NAlert v-if="error" type="error" closable style="margin-bottom: 12px" @close="error = ''">{{ error }}</NAlert>
    <NModal
      v-model:show="pendingVisible"
      preset="dialog"
      type="warning"
      title="确认删除"
      :content="`${pending?.message ?? ''} 该操作不可恢复。`"
      :positive-button-props="{ type: 'error' }"
      positive-text="确认一起删除"
      negative-text="取消"
      @positive-click="confirmPending"
      @negative-click="pending = null"
    />
    <NAlert v-if="notice" type="success" closable style="margin-bottom: 12px" @close="notice = ''">{{ notice }}</NAlert>

    <NTabs v-model:value="mode" type="line" animated size="large">
      <NTabPane name="resume" tab="简历">
        <div class="grid-2">
          <NCard size="small">
            <template #header>录入简历</template>
            <div class="stack">
              <NInput v-model:value="title" placeholder="标题（可留空，自动生成）" />
              <div class="row">
                <NButton size="small" secondary :disabled="extracting" @click="pickFile('pdf')">上传 PDF</NButton>
                <NButton size="small" secondary :disabled="extracting" @click="pickFile('image')">上传图片</NButton>
                <span class="muted small">PDF 抽文本 · 图片走 OCR</span>
              </div>
              <p v-if="extractNote" class="muted small" style="margin: 0">{{ extractNote }}</p>
              <NInput
                v-model:value="body"
                type="textarea"
                :autosize="{ minRows: 8, maxRows: 16 }"
                placeholder="粘贴简历正文，至少 50 字。建议保留项目、职责、数字，锚点会从这里定位。"
              />
              <div class="row">
                <span class="muted small">{{ body.trim().length }} / 12000 字</span>
                <span class="spacer" />
                <NButton type="primary" :loading="busy" :disabled="!canParse" @click="parse">解析并保存</NButton>
              </div>
              <p class="muted small">解析会调一次模型，约 5～20 秒。</p>
            </div>
          </NCard>

          <NCard size="small">
            <template #header>已保存的简历（{{ resumes.length }}）</template>
            <NEmpty v-if="resumes.length === 0" description="还没有简历" size="small" />
            <ul v-else class="stack" style="list-style: none; padding: 0">
              <li v-for="item in resumes" :key="item.id" class="row">
                <a href="#" @click.prevent="openResume(item.id)">{{ item.title }}</a>
                <span class="spacer" />
                <span class="muted small">{{ new Date(item.createdAt).toLocaleString() }}</span>
                <NPopconfirm @positive-click="removeResume(item.id)">
                  <template #trigger>
                    <NButton size="tiny" quaternary type="error">删除</NButton>
                  </template>
                  删除这份简历？
                </NPopconfirm>
              </li>
            </ul>
          </NCard>
        </div>

        <NCard v-if="resumeDetail" size="small" style="margin-top: 16px">
          <template #header>解析结果 · {{ resumeDetail.title }}</template>
          <div class="stack">
            <p>{{ resumeDetail.profile.summary }}</p>
            <div class="row small">
              <span class="muted">经验年限</span>
              <b>{{ resumeDetail.profile.totalYears }} 年</b>
              <span class="muted">技能</span>
              <NTag v-for="skill in resumeDetail.profile.skills" :key="skill" size="small" type="info">
                {{ skill }}
              </NTag>
            </div>

            <div v-if="resumeDetail.profile.experiences.length">
              <b>经历</b>
              <ul class="list-plain">
                <li v-for="(exp, index) in resumeDetail.profile.experiences" :key="index">
                  {{ exp.company }} · {{ exp.title }} · {{ exp.period }}
                  <ul class="list-plain small">
                    <li v-for="(line, i) in exp.highlights" :key="i">{{ line }}</li>
                  </ul>
                </li>
              </ul>
            </div>

            <div v-if="resumeDetail.profile.projects.length">
              <b>项目</b>
              <ul class="list-plain">
                <li v-for="(project, index) in resumeDetail.profile.projects" :key="index">
                  {{ project.name }} · {{ project.period }}
                  <div class="small muted">{{ project.description }}</div>
                  <div class="row small">
                    <NTag v-for="tech in project.tech" :key="tech" size="small">{{ tech }}</NTag>
                  </div>
                </li>
              </ul>
            </div>

            <div>
              <b>证据锚点（{{ resumeDetail.anchors.length }}）</b>
              <div v-for="anchor in resumeDetail.anchors" :key="anchor.id" class="anchor">
                <div class="row small">
                  <span class="mono">{{ anchor.id }}</span>
                  <span class="muted">{{ anchor.kind }} · {{ anchor.label }}</span>
                  <NTag v-if="!anchor.quoteExact" size="small" type="warning">转述</NTag>
                </div>
                <p class="anchor-quote">{{ anchor.quote }}</p>
              </div>
            </div>
          </div>
        </NCard>
      </NTabPane>

      <NTabPane name="job" tab="岗位 JD">
        <div class="grid-2">
          <NCard size="small">
            <template #header>录入 JD</template>
            <div class="stack">
              <NInput v-model:value="title" placeholder="标题（可留空，用 JD 里的岗位名）" />
              <div class="row">
                <NButton size="small" secondary :disabled="extracting" @click="pickFile('pdf')">上传 PDF</NButton>
                <NButton size="small" secondary :disabled="extracting" @click="pickFile('image')">上传图片</NButton>
                <span class="muted small">PDF 抽文本 · 图片走 OCR</span>
              </div>
              <p v-if="extractNote" class="muted small" style="margin: 0">{{ extractNote }}</p>
              <NInput
                v-model:value="body"
                type="textarea"
                :autosize="{ minRows: 8, maxRows: 16 }"
                placeholder="粘贴 JD 正文，至少 50 字。要求和职责会各生成一个锚点，缺口题只能引用原文里真实存在的句子。"
              />
              <div class="row">
                <span class="muted small">{{ body.trim().length }} / 12000 字</span>
                <span class="spacer" />
                <NButton type="primary" :loading="busy" :disabled="!canParse" @click="parse">解析并保存</NButton>
              </div>
            </div>
          </NCard>

          <NCard size="small">
            <template #header>已保存的 JD（{{ jobs.length }}）</template>
            <NEmpty v-if="jobs.length === 0" description="还没有 JD" size="small" />
            <ul v-else class="stack" style="list-style: none; padding: 0">
              <li v-for="item in jobs" :key="item.id" class="row">
                <a href="#" @click.prevent="openJob(item.id)">{{ item.title }}</a>
                <NTag v-if="item.level" size="small">{{ item.level }}</NTag>
                <span class="spacer" />
                <span class="muted small">{{ new Date(item.createdAt).toLocaleString() }}</span>
                <NPopconfirm @positive-click="removeJob(item.id)">
                  <template #trigger>
                    <NButton size="tiny" quaternary type="error">删除</NButton>
                  </template>
                  删除这份 JD？
                </NPopconfirm>
              </li>
            </ul>
          </NCard>
        </div>

        <NCard v-if="jobDetail" size="small" style="margin-top: 16px">
          <template #header>解析结果 · {{ jobDetail.title }}</template>
          <div class="stack">
            <div class="row small">
              <span class="muted">岗位</span>
              <b>{{ jobDetail.profile.title || jobDetail.title }}</b>
              <NTag v-if="jobDetail.level" size="small">{{ jobDetail.level }}</NTag>
            </div>

            <div v-if="jobDetail.profile.mustHave.length">
              <b>硬性要求</b>
              <ul class="list-plain">
                <li v-for="(item, index) in jobDetail.profile.mustHave" :key="index">{{ item }}</li>
              </ul>
            </div>

            <div v-if="jobDetail.profile.responsibilities.length">
              <b>职责</b>
              <ul class="list-plain">
                <li v-for="(item, index) in jobDetail.profile.responsibilities" :key="index">{{ item }}</li>
              </ul>
            </div>

            <div v-if="jobDetail.profile.niceToHave.length" class="small">
              <b>加分项</b>
              <ul class="list-plain">
                <li v-for="(item, index) in jobDetail.profile.niceToHave" :key="index">{{ item }}</li>
              </ul>
            </div>

            <div>
              <b>证据锚点（{{ jobDetail.anchors.length }}）</b>
              <div v-for="anchor in jobDetail.anchors" :key="anchor.id" class="anchor">
                <div class="row small">
                  <span class="mono">{{ anchor.id }}</span>
                  <span class="muted">{{ anchor.kind }} · {{ anchor.label }}</span>
                  <NTag v-if="!anchor.quoteExact" size="small" type="warning">转述</NTag>
                </div>
                <p class="anchor-quote">{{ anchor.quote }}</p>
              </div>
            </div>
          </div>
        </NCard>
      </NTabPane>
    </NTabs>

    <div v-if="busy" class="row" style="margin-top: 12px">
      <NSpin size="small" />
      <span class="muted small">解析中…</span>
    </div>

    <input ref="fileInput" type="file" hidden @change="onFileChange" />
  </section>
</template>
