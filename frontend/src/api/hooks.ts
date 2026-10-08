import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { http } from './client'
import type {
  AttachIssuesResult,
  ConnectionDto,
  ConnectionInput,
  ConnectionTestResult,
  CopyProjectRequest,
  EpicSearchItem,
  ExportParams,
  ExportResult,
  GanttChange,
  GanttDto,
  GanttSaveResult,
  ImportSummary,
  JiraDiscoverRequest,
  JiraDiscoverResult,
  JiraPushPreview,
  JiraPushResult,
  IssueDto,
  MemberDto,
  ProjectDto,
  ProjectInput,
  ProjectSummaryDto,
  UserDto,
  UsersSyncResult,
  WorkloadDto,
} from './types'

export const keys = {
  connections: ['connections'] as const,
  epics: (id: string, search: string, project: string) => ['connections', id, 'epics', search, project] as const,
  users: (connectionId?: string) => ['users', connectionId ?? 'all'] as const,
  projects: ['projects'] as const,
  project: (id: string) => ['projects', id] as const,
  gantt: (id: string) => ['projects', id, 'gantt'] as const,
  workload: (userId: string, from?: string, to?: string) => ['workload', userId, from ?? '', to ?? ''] as const,
}

// ---------- Подключения

export const useConnections = () =>
  useQuery({ queryKey: keys.connections, queryFn: () => http.get<ConnectionDto[]>('/api/connections') })

export function useSaveConnection() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, input }: { id?: string; input: ConnectionInput }) =>
      id
        ? http.put<ConnectionDto>(`/api/connections/${id}`, input)
        : http.post<ConnectionDto>('/api/connections', input),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.connections }),
  })
}

export function useDeleteConnection() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => http.delete(`/api/connections/${id}`),
    onSuccess: () => qc.invalidateQueries(),
  })
}

export const useTestConnection = () =>
  useMutation({ mutationFn: (id: string) => http.post<ConnectionTestResult>(`/api/connections/${id}/test`) })

/** Подключение по адресу и токену до сохранения: пользователь, версия, проекты и поля Jira. */
export const useDiscoverJira = () =>
  useMutation({
    mutationFn: (request: JiraDiscoverRequest) => http.post<JiraDiscoverResult>('/api/connections/discover', request),
  })

/** Эпики во всех проектах подключения или в одном (project). */
export const useEpicSearch = (connectionId: string, search: string, project: string | null, enabled: boolean) =>
  useQuery({
    queryKey: keys.epics(connectionId, search, project ?? ''),
    queryFn: () =>
      http.get<EpicSearchItem[]>(`/api/connections/${connectionId}/epics`, { search, project: project ?? undefined }),
    enabled,
  })

export function useSyncUsers() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (connectionId: string) => http.post<UsersSyncResult>(`/api/connections/${connectionId}/users/sync`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }),
  })
}

// ---------- Пользователи

export const useUsers = (connectionId?: string) =>
  useQuery({ queryKey: keys.users(connectionId), queryFn: () => http.get<UserDto[]>('/api/users', { connectionId }) })

export const useWorkload = (userId: string | undefined, from?: string, to?: string) =>
  useQuery({
    queryKey: keys.workload(userId ?? '', from, to),
    queryFn: () => http.get<WorkloadDto>(`/api/users/${userId}/workload`, { from, to }),
    enabled: !!userId,
  })

// ---------- Проекты

export const useProjects = () =>
  useQuery({ queryKey: keys.projects, queryFn: () => http.get<ProjectSummaryDto[]>('/api/projects') })

export const useProject = (id: string) =>
  useQuery({ queryKey: keys.project(id), queryFn: () => http.get<ProjectDto>(`/api/projects/${id}`) })

/** Мутация, после которой перезапрашиваются данные проекта и списки. */
function useProjectMutation<TVars, TResult>(projectId: string, fn: (vars: TVars) => Promise<TResult>) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: async () => {
      await Promise.all([
        qc.invalidateQueries({ queryKey: keys.project(projectId) }),
        qc.invalidateQueries({ queryKey: keys.projects, exact: true }),
        qc.invalidateQueries({ queryKey: ['workload'] }),
        qc.invalidateQueries({ queryKey: ['users'] }),
      ])
    },
  })
}

export function useCreateProject() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: ProjectInput) => http.post<ProjectSummaryDto>('/api/projects', input),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.projects }),
  })
}

/** Новый проект на основе существующего. */
export function useCopyProject() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ sourceId, request }: { sourceId: string; request: CopyProjectRequest }) =>
      http.post<ProjectSummaryDto>(`/api/projects/${sourceId}/copy`, request),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.projects }),
  })
}

export const useUpdateProject = (id: string) =>
  useProjectMutation(id, (input: ProjectInput) => http.put<void>(`/api/projects/${id}`, input))

export function useDeleteProject() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => http.delete(`/api/projects/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.projects }),
  })
}

export const useAttachEpics = (projectId: string) =>
  useProjectMutation(projectId, (epicKeys: string[]) =>
    http.post<ImportSummary>(`/api/projects/${projectId}/epics`, { epicKeys }),
  )

export const useAttachIssues = (projectId: string) =>
  useProjectMutation(projectId, (issueKeys: string[]) =>
    http.post<AttachIssuesResult>(`/api/projects/${projectId}/issues`, { issueKeys }),
  )

export const useDetachIssue = (projectId: string) =>
  useProjectMutation(projectId, (issueId: string) => http.delete(`/api/projects/${projectId}/issues/${issueId}`))

export const useSyncProject = (projectId: string) =>
  useProjectMutation(projectId, () => http.post<ImportSummary>(`/api/projects/${projectId}/sync`))

export const useAddMember = (projectId: string) =>
  useProjectMutation(projectId, (jiraUserId: string) =>
    http.post<MemberDto>(`/api/projects/${projectId}/members`, { jiraUserId }),
  )

export const useRemoveMember = (projectId: string) =>
  useProjectMutation(projectId, (userId: string) => http.delete(`/api/projects/${projectId}/members/${userId}`))

export const useReorderMembers = (projectId: string) =>
  useProjectMutation(projectId, (userIds: string[]) =>
    http.put<void>(`/api/projects/${projectId}/members/order`, { userIds }),
  )

export const useResetToJira = (projectId: string) =>
  useProjectMutation(projectId, (issueId: string) => http.post<IssueDto>(`/api/issues/${issueId}/reset-to-jira`))

// ---------- Диаграмма

export const useGantt = (projectId: string) =>
  useQuery({ queryKey: keys.gantt(projectId), queryFn: () => http.get<GanttDto>(`/api/projects/${projectId}/gantt`) })

export const useSaveGantt = (projectId: string) =>
  useProjectMutation(projectId, (changes: GanttChange[]) =>
    http.put<GanttSaveResult>(`/api/projects/${projectId}/gantt`, { changes }),
  )

// ---------- Отправка сроков в Jira

export const useJiraPushPreview = (projectId: string, includeAssignee: boolean, enabled: boolean) =>
  useQuery({
    queryKey: ['projects', projectId, 'jira-push', includeAssignee],
    queryFn: () => http.get<JiraPushPreview>(`/api/projects/${projectId}/jira-push`, { includeAssignee }),
    enabled,
    staleTime: 0,
  })

export const useJiraPush = (projectId: string) =>
  useProjectMutation(projectId, (request: { issueIds: string[] | null; includeAssignee: boolean }) =>
    http.post<JiraPushResult>(`/api/projects/${projectId}/jira-push`, request),
  )

// ---------- Экспорт

export const fetchProjectExport = (projectId: string, params: ExportParams) =>
  http.get<ExportResult>(`/api/projects/${projectId}/export`, params)

export const fetchWorkloadExport = (userId: string, params: ExportParams) =>
  http.get<ExportResult>(`/api/users/${userId}/workload/export`, params)

export const seedDemo = () => http.post<{ projectId: string }>('/api/dev/seed')
