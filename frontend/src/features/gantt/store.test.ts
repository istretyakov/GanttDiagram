import { beforeEach, describe, expect, it } from 'vitest'
import type { IssueDto } from '../../api/types'
import { effectivePlan, toSaveRequest, useGanttDraft } from './store'

function issue(id: string, start: string | null, end: string | null, assigneeId: string | null = 'u1'): IssueDto {
  return {
    id,
    key: `PRJ-${id}`,
    summary: 'Задача',
    status: 'К выполнению',
    statusCategory: 'new',
    epicId: null,
    epicKey: null,
    epicSummary: null,
    originalEstimateSeconds: 28800,
    planStartDate: start,
    planEndDate: end,
    planAssigneeId: assigneeId,
    planAssigneeName: null,
    jiraStartDate: start,
    jiraEndDate: end,
    jiraAssigneeId: assigneeId,
    jiraAssigneeName: null,
    isLocallyModified: false,
    differsFromJira: false,
    version: 7,
    jiraUrl: 'https://jira/browse/PRJ-1',
  }
}

const store = () => useGanttDraft.getState()

describe('gantt draft store', () => {
  beforeEach(() => {
    useGanttDraft.setState({ projectId: null, server: new Map(), changes: new Map(), history: [], future: [] })
    store().load('p1', [issue('1', '2026-10-05', '2026-10-06'), issue('2', null, null, null)])
  })

  it('apply records a change and effective() merges it with server data', () => {
    store().apply({ issueId: '1', start: '2026-10-07', end: '2026-10-08', assigneeId: 'u2' })

    expect(store().changes.size).toBe(1)
    expect(effectivePlan(store(), '1')).toEqual({ start: '2026-10-07', end: '2026-10-08', assigneeId: 'u2' })
    expect(effectivePlan(store(), '2')).toEqual({ start: null, end: null, assigneeId: null })
  })

  it('a change back to server values is not dirty', () => {
    store().apply({ issueId: '1', start: '2026-10-07', end: '2026-10-08', assigneeId: 'u1' })
    store().apply({ issueId: '1', start: '2026-10-05', end: '2026-10-06', assigneeId: 'u1' })

    expect(store().changes.size).toBe(0)
  })

  it('undo and redo walk the history', () => {
    store().apply({ issueId: '1', start: '2026-10-07', end: '2026-10-08', assigneeId: 'u1' })
    store().apply({ issueId: '2', start: '2026-10-12', end: '2026-10-12', assigneeId: 'u1' })
    expect(store().changes.size).toBe(2)

    store().undo()
    expect(store().changes.size).toBe(1)
    expect(store().changes.has('2')).toBe(false)

    store().undo()
    expect(store().changes.size).toBe(0)
    store().undo() // пустая история — ничего не происходит
    expect(store().changes.size).toBe(0)

    store().redo()
    store().redo()
    expect(store().changes.size).toBe(2)
    expect(store().future).toHaveLength(0)
  })

  it('a new change clears redo stack; reset is undoable', () => {
    store().apply({ issueId: '1', start: '2026-10-07', end: '2026-10-08', assigneeId: 'u1' })
    store().undo()
    store().apply({ issueId: '2', start: '2026-10-12', end: '2026-10-12', assigneeId: 'u1' })
    expect(store().future).toHaveLength(0)

    store().reset()
    expect(store().changes.size).toBe(0)
    store().undo()
    expect(store().changes.size).toBe(1)
  })

  it('reload of the same project keeps changes that still differ', () => {
    store().apply({ issueId: '1', start: '2026-10-07', end: '2026-10-08', assigneeId: 'u1' })
    store().apply({ issueId: '2', start: '2026-10-12', end: '2026-10-12', assigneeId: 'u1' })

    // Сервер теперь содержит правку задачи 2 (например, сохранена в другой вкладке).
    store().load('p1', [issue('1', '2026-10-05', '2026-10-06'), issue('2', '2026-10-12', '2026-10-12')])

    expect([...store().changes.keys()]).toEqual(['1'])
  })

  it('loading another project or keepChanges=false drops the draft', () => {
    store().apply({ issueId: '1', start: '2026-10-07', end: '2026-10-08', assigneeId: 'u1' })
    store().load('p1', [issue('1', '2026-10-05', '2026-10-06')], false)
    expect(store().changes.size).toBe(0)
    expect(store().history).toHaveLength(0)
  })

  it('builds the save request with server versions', () => {
    store().apply({ issueId: '1', start: null, end: null, assigneeId: null })
    expect(toSaveRequest(store())).toEqual([
      { issueId: '1', startDate: null, endDate: null, assigneeId: null, version: 7 },
    ])
  })
})
