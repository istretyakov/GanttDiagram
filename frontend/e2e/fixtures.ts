import type { Page, Route } from '@playwright/test'
import type { GanttChange, GanttDto, IssueDto } from '../src/api/types'

export const PROJECT_ID = '11111111-1111-1111-1111-111111111111'
export const IVANOV = 'aaaaaaaa-0000-0000-0000-000000000001'
export const PETROV = 'aaaaaaaa-0000-0000-0000-000000000002'

function issue(id: string, key: string, start: string | null, end: string | null, assignee: string | null, hours = 16): IssueDto {
  return {
    id,
    key,
    summary: `Задача ${key}`,
    status: 'К выполнению',
    statusCategory: 'new',
    epicId: 'e1',
    epicKey: 'PRJ-1',
    epicSummary: 'Эпик',
    originalEstimateSeconds: hours * 3600,
    planStartDate: start,
    planEndDate: end,
    planAssigneeId: assignee,
    planAssigneeName: null,
    jiraStartDate: start,
    jiraEndDate: end,
    jiraAssigneeId: assignee,
    jiraAssigneeName: null,
    isLocallyModified: false,
    differsFromJira: false,
    version: 1,
    jiraUrl: `https://jira.local/browse/${key}`,
  }
}

/** Неделя 05.10–09.10.2026; шкала начинается с понедельника 28.09. */
export const gantt: GanttDto = {
  project: { id: PROJECT_ID, name: 'Релиз E2E', connectionId: 'c1', jiraBaseUrl: 'https://jira.local', hoursPerDay: 8 },
  members: [
    { userId: IVANOV, name: 'ivanov', displayName: 'Иван Иванов', avatarUrl: null, sortOrder: 0 },
    { userId: PETROV, name: 'petrov', displayName: 'Пётр Петров', avatarUrl: null, sortOrder: 1 },
  ],
  issues: [
    issue('i1', 'PRJ-10', '2026-10-05', '2026-10-06', IVANOV),
    issue('i2', 'PRJ-11', null, null, null, 24),
  ],
  epics: [{ id: 'e1', key: 'PRJ-1', summary: 'Эпик', lastSyncedAt: '2026-10-01T00:00:00Z' }],
  range: { from: '2026-09-28', to: '2026-11-08' },
  today: '2026-10-08',
}

/** Мокает API и возвращает массив тел запросов PUT /gantt. */
export async function mockApi(page: Page): Promise<GanttChange[][]> {
  const saved: GanttChange[][] = []
  await page.route((url) => url.pathname.startsWith('/api/'), async (route: Route) => {
    const url = new URL(route.request().url())
    const method = route.request().method()
    if (url.pathname === `/api/projects/${PROJECT_ID}/gantt` && method === 'GET') return route.fulfill({ json: gantt })
    if (url.pathname === `/api/projects/${PROJECT_ID}/gantt` && method === 'PUT') {
      const body = route.request().postDataJSON() as { changes: GanttChange[] }
      saved.push(body.changes)
      return route.fulfill({ json: { saved: body.changes.length } })
    }
    if (url.pathname === '/api/users') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404, json: { title: 'not mocked' } })
  })
  return saved
}
