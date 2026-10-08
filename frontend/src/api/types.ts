import type { components } from './schema'

type S = components['schemas']

export type ConnectionDto = S['ConnectionDto']
export type ConnectionInput = S['ConnectionInput']
export type ConnectionTestResult = S['ConnectionTestResult']
export type FieldsResponse = S['FieldsResponse']
export type JiraDiscoverRequest = S['JiraDiscoverRequest']
export type JiraDiscoverResult = S['JiraDiscoverResult']
export type HierarchyLinkDto = S['HierarchyLinkDto']
export type JiraLinkTypeDto = S['JiraLinkTypeDto']
export type EpicSearchItem = S['EpicSearchItem']
export type ImportSummary = S['ImportSummary']
export type UsersSyncResult = S['UsersSyncResult']
export type UserDto = S['UserDto']
export type EpicDto = S['EpicDto']
export type ProjectSummaryDto = S['ProjectSummaryDto']
export type ProjectDto = S['ProjectDto']
export type ProjectInput = S['ProjectInput']
export type AttachIssuesResult = S['AttachIssuesResult']
export type MemberDto = S['MemberDto']
export type IssueDto = S['IssueDto']
export type GanttDto = S['GanttDto']
export type GanttChange = S['GanttChange']
export type GanttSaveResult = S['GanttSaveResult']
export type WorkloadDto = S['WorkloadDto']
export type WorkloadTaskDto = S['WorkloadTaskDto']
export type WorkloadDayDto = S['WorkloadDayDto']
export type ExportResult = S['ExportResult']
export type CopyProjectRequest = S['CopyProjectRequest']
export type JiraPushPreview = S['JiraPushPreview']
export type JiraPushItem = S['JiraPushItem']
export type JiraPushResult = S['JiraPushResult']
export type JiraPushResultItem = S['JiraPushResultItem']

export type ExportFormat = 'plantuml' | 'mermaid'
export type ExportGroupBy = 'member' | 'epic'
export type ExportLabel = 'key' | 'keySummary' | 'keySummaryAssignee'

export type ExportParams = {
  format: ExportFormat
  groupBy?: ExportGroupBy
  from?: string
  to?: string
  label?: ExportLabel
  excludeWeekends?: boolean
  markToday?: boolean
}
