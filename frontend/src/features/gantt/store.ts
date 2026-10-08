import { create } from 'zustand'
import type { GanttChange, IssueDto } from '../../api/types'

/** Плановые значения задачи (даты в формате yyyy-MM-dd). */
export type Plan = { start: string | null; end: string | null; assigneeId: string | null }

export type PlanChange = Plan & { issueId: string }

type Changes = ReadonlyMap<string, Plan>

const HISTORY_LIMIT = 200

export type GanttDraftState = {
  projectId: string | null
  /** Задачи как пришли с сервера. */
  server: ReadonlyMap<string, IssueDto>
  /** Несохранённые правки: только те, что отличаются от сервера. */
  changes: Changes
  history: Changes[]
  future: Changes[]

  /** Загрузить данные сервера. Правки сохраняются, если это тот же проект и keepChanges = true. */
  load(projectId: string, issues: readonly IssueDto[], keepChanges?: boolean): void
  apply(change: PlanChange | readonly PlanChange[]): void
  undo(): void
  redo(): void
  reset(): void
}

export const serverPlan = (issue: IssueDto): Plan => ({
  start: issue.planStartDate,
  end: issue.planEndDate,
  assigneeId: issue.planAssigneeId,
})

const samePlan = (a: Plan, b: Plan) => a.start === b.start && a.end === b.end && a.assigneeId === b.assigneeId

export const useGanttDraft = create<GanttDraftState>()((set, get) => ({
  projectId: null,
  server: new Map(),
  changes: new Map(),
  history: [],
  future: [],

  load(projectId, issues, keepChanges = true) {
    const server = new Map(issues.map((i) => [i.id, i]))
    const state = get()
    if (!keepChanges || state.projectId !== projectId) {
      set({ projectId, server, changes: new Map(), history: [], future: [] })
      return
    }
    // Та же диаграмма: оставляем правки для задач, которые ещё есть и ещё отличаются.
    const changes = new Map<string, Plan>()
    for (const [id, plan] of state.changes) {
      const issue = server.get(id)
      if (issue && !samePlan(plan, serverPlan(issue))) changes.set(id, plan)
    }
    set({ projectId, server, changes })
  },

  apply(input) {
    const list = Array.isArray(input) ? input : [input as PlanChange]
    const { server, changes, history } = get()
    const next = new Map(changes)
    for (const { issueId, ...plan } of list) {
      const issue = server.get(issueId)
      if (!issue) continue
      if (samePlan(plan, serverPlan(issue))) next.delete(issueId)
      else next.set(issueId, plan)
    }
    if (mapsEqual(next, changes)) return
    set({ changes: next, history: [...history, changes].slice(-HISTORY_LIMIT), future: [] })
  },

  undo() {
    const { history, changes, future } = get()
    if (history.length === 0) return
    set({ changes: history[history.length - 1], history: history.slice(0, -1), future: [changes, ...future] })
  },

  redo() {
    const { history, changes, future } = get()
    if (future.length === 0) return
    set({ changes: future[0], history: [...history, changes], future: future.slice(1) })
  },

  reset() {
    const { changes, history } = get()
    if (changes.size === 0) return
    set({ changes: new Map(), history: [...history, changes], future: [] })
  },
}))

function mapsEqual(a: Changes, b: Changes): boolean {
  if (a.size !== b.size) return false
  for (const [id, plan] of a) {
    const other = b.get(id)
    if (!other || !samePlan(plan, other)) return false
  }
  return true
}

/** Действующие значения задачи: сервер + черновик. */
export function effectivePlan(state: Pick<GanttDraftState, 'server' | 'changes'>, issueId: string): Plan | null {
  const change = state.changes.get(issueId)
  if (change) return change
  const issue = state.server.get(issueId)
  return issue ? serverPlan(issue) : null
}

/** Тело запроса PUT /gantt. */
export function toSaveRequest(state: Pick<GanttDraftState, 'server' | 'changes'>): GanttChange[] {
  return [...state.changes].map(([issueId, plan]) => ({
    issueId,
    startDate: plan.start,
    endDate: plan.end,
    assigneeId: plan.assigneeId,
    version: state.server.get(issueId)!.version,
  }))
}
