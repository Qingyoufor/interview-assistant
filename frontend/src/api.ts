export interface Anchor {
  id: string
  kind: string
  label: string
  quote: string
  quoteExact: boolean
}

export interface ResumeProfile {
  summary: string
  totalYears: number
  skills: string[]
  experiences: { company: string; title: string; period: string; highlights: string[] }[]
  projects: { name: string; period: string; tech: string[]; description: string; metrics: string[] }[]
  strengths: string[]
  gaps: string[]
}

export interface JobProfile {
  title: string
  level: string
  responsibilities: string[]
  mustHave: string[]
  niceToHave: string[]
  focusAreas: string[]
}

export interface ResumeListItem {
  id: string
  title: string
  createdAt: string
}

export interface ResumeDetail {
  id: string
  title: string
  rawText: string
  createdAt: string
  profile: ResumeProfile
  anchors: Anchor[]
}

export interface JobTargetListItem {
  id: string
  title: string
  level?: string | null
  createdAt: string
}

export interface JobTargetDetail {
  id: string
  title: string
  level: string
  rawText: string
  createdAt: string
  profile: JobProfile
  anchors: Anchor[]
}

export interface ResumeParseResponse extends ResumeDetail {
  truncated: boolean
}

export interface JobTargetParseResponse extends JobTargetDetail {
  truncated: boolean
}

export interface MatchItem {
  requirement: string
  category: string
  jdAnchorId: string
  resumeAnchorIds: string[]
  evidence: string
  verdict: string
  risk?: string | null
}

export interface MatchBreakdown {
  mustHave: number
  responsibilities: number
  evidenceQuality: number
  bonus: number
}

export interface MatchResult {
  overallMatch: number
  breakdown: MatchBreakdown
  hardBlock: boolean
  items: MatchItem[]
  topRisks: string[]
}

export interface MatchResponse {
  id: string
  resumeId: string
  jobTargetId: string
  cached: boolean
  result: MatchResult
  createdAt: string
}

export interface Assessment {
  atoms: Record<string, number>
  display: { structure: number; depth: number; evidence: number; delivery: number }
  claimChecks: { claim: string; verdict: string; evidenceAnchorIds: string[] }[]
  weaknesses: string[]
  strengths: string[]
  improvements: string[]
  remediation?: string | null
  hasFactualError: boolean
}

export interface SessionQuestion {
  id: string
  ordinal: number
  type: string
  source: string
  content: string
  intent: string
  anchorIds: string[]
  answered: boolean
  followUp?: string | null
  followUpAnswered: boolean
  assessment?: Assessment | null
}

export interface SessionResponse {
  id: string
  resumeId: string
  jobTargetId: string
  matchId: string
  status: string
  startedAt: string
  match: MatchResult
  questions: SessionQuestion[]
}

export interface AnswerResponse {
  action: string
  followUp?: string | null
  followUpFallback: boolean
  assessment: Assessment
  nextQuestion?: SessionQuestion | null
}

export interface SessionListItem {
  id: string
  status: string
  startedAt: string
  completedAt?: string | null
  overallScore?: number | null
  jobTitle: string
  resumeTitle: string
  questionCount: number
  answeredCount: number
}

export interface DimensionAverage {
  dimension: string
  average: number
  count: number
}

export interface GapEvidence {
  requirement: string
  remediation: string
  question: string
}

export interface WeaknessCount {
  tag: string
  count: number
}

export interface SessionReport {
  sessionId: string
  overallScore: number
  matchOverall: number
  breakdown: MatchBreakdown
  hardBlock: boolean
  topRisks: string[]
  dimensions: DimensionAverage[]
  totalQuestions: number
  answeredQuestions: number
  weaknesses: WeaknessCount[]
  topGaps: GapEvidence[]
  strengths: string[]
  summary: string
}

/** 后端统一返回 `{ error }`，前端把状态码、文案和原始 body 一起抛出去。 */
export class ApiError extends Error {
  readonly status: number
  readonly payload: unknown

  constructor(status: number, message: string, payload: unknown = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.payload = payload
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(`/api${path}`, {
      ...init,
      headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) }
    })
  } catch {
    // 网络层就失败了：最常见是后端没起，给一句能照着做的提示
    throw new ApiError(0, '连不上后端：请确认 API 已在 http://localhost:5052 启动')
  }

  if (!response.ok) {
    let message = `请求失败（${response.status}）`
    let payload: unknown = null
    try {
      payload = await response.json()
      const body = payload as { error?: string }
      if (body?.error) message = body.error
    } catch {
      // 错误体不是 JSON，用默认文案
    }
    throw new ApiError(response.status, message, payload)
  }

  if (response.status === 204) return undefined as T

  return (await response.json()) as T
}

export const api = {
  listResumes: () => request<ResumeListItem[]>('/resumes'),
  getResume: (id: string) => request<ResumeDetail>(`/resumes/${id}`),
  createResume: (title: string, rawText: string) =>
    request<ResumeParseResponse>('/resumes', { method: 'POST', body: JSON.stringify({ title, rawText }) }),
  deleteResume: (id: string, force = false) =>
    request<void>(`/resumes/${id}?force=${force}`, { method: 'DELETE' }),

  listJobTargets: () => request<JobTargetListItem[]>('/job-targets'),
  getJobTarget: (id: string) => request<JobTargetDetail>(`/job-targets/${id}`),
  createJobTarget: (title: string, jdText: string) =>
    request<JobTargetParseResponse>('/job-targets', { method: 'POST', body: JSON.stringify({ title, jdText }) }),
  deleteJobTarget: (id: string, force = false) =>
    request<void>(`/job-targets/${id}?force=${force}`, { method: 'DELETE' }),

  listSessions: () => request<SessionListItem[]>('/sessions'),

  createMatch: (resumeId: string, jobTargetId: string, refresh = false) =>
    request<MatchResponse>('/matches', { method: 'POST', body: JSON.stringify({ resumeId, jobTargetId, refresh }) }),

  createSession: (resumeId: string, jobTargetId: string) =>
    request<SessionResponse>('/sessions', { method: 'POST', body: JSON.stringify({ resumeId, jobTargetId }) }),
  getSession: (id: string) => request<SessionResponse>(`/sessions/${id}`),
  answer: (sessionId: string, questionId: string, answer: string) =>
    request<AnswerResponse>(`/sessions/${sessionId}/questions/${questionId}/answer`, {
      method: 'POST',
      body: JSON.stringify({ answer })
    }),
  followUp: (sessionId: string, questionId: string, answer: string) =>
    request<AnswerResponse>(`/sessions/${sessionId}/questions/${questionId}/followup`, {
      method: 'POST',
      body: JSON.stringify({ answer })
    }),

  getReport: (sessionId: string) => request<SessionReport>(`/sessions/${sessionId}/report`),
  completeSession: (sessionId: string) =>
    request<SessionReport>(`/sessions/${sessionId}/complete`, { method: 'POST' })
}
