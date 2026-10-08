import { Anchor, Badge, Button, Group, Stack, Text } from '@mantine/core'
import { IconExternalLink } from '@tabler/icons-react'
import type { IssueDto } from '../../api/types'
import { formatIso } from '../../shared/dateMath'
import { formatHours } from '../../shared/format'
import type { Plan } from './store'

type BarCardProps = {
  issue: IssueDto
  plan: Plan
  assigneeName?: string
  jiraAssigneeName?: string | null
  readOnly?: boolean
  onResetToJira?(): void
  onUnplan?(): void
}

/** Всплывающая карточка полосы: даты плана и Jira, оценка, статус, ссылка на Jira. */
export function BarCard({ issue, plan, assigneeName, readOnly, onResetToJira, onUnplan }: BarCardProps) {
  const differs =
    plan.start !== issue.jiraStartDate || plan.end !== issue.jiraEndDate || plan.assigneeId !== issue.jiraAssigneeId

  return (
    <Stack gap={6}>
      <Group justify="space-between" wrap="nowrap">
        <Anchor href={issue.jiraUrl} target="_blank" rel="noreferrer" fw={600} size="sm">
          {issue.key} <IconExternalLink size={12} />
        </Anchor>
        <Badge size="sm" variant="light" color={issue.statusCategory === 'done' ? 'green' : issue.statusCategory === 'indeterminate' ? 'blue' : 'gray'}>
          {issue.status || '—'}
        </Badge>
      </Group>
      <Text size="sm">{issue.summary}</Text>
      {issue.epicKey && (
        <Text size="xs" c="dimmed">
          Эпик: {issue.epicKey} · {issue.epicSummary}
        </Text>
      )}
      <Text size="xs">
        План: {formatIso(plan.start)} – {formatIso(plan.end)}
        {assigneeName ? ` · ${assigneeName}` : ''}
      </Text>
      {differs && (
        <Text size="xs" c="orange.8">
          ≠ В Jira: {formatIso(issue.jiraStartDate)} – {formatIso(issue.jiraEndDate)} · {issue.jiraAssigneeName ?? 'без исполнителя'}
        </Text>
      )}
      <Text size="xs" c="dimmed">
        Оценка: {formatHours(issue.originalEstimateSeconds)}
      </Text>
      {!readOnly && (
        <Group gap="xs" mt={4}>
          {differs && onResetToJira && (
            <Button size="compact-xs" variant="light" color="orange" onClick={onResetToJira}>
              Сбросить к Jira
            </Button>
          )}
          {onUnplan && (
            <Button size="compact-xs" variant="subtle" color="gray" onClick={onUnplan}>
              Снять с плана
            </Button>
          )}
        </Group>
      )}
    </Stack>
  )
}
