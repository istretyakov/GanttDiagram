import { useDraggable } from '@dnd-kit/core'
import { ActionIcon, Badge, Group, Select, Stack, Switch, Text, TextInput, Tooltip } from '@mantine/core'
import {
  IconChevronDown,
  IconChevronLeft,
  IconChevronRight,
  IconGripVertical,
  IconSearch,
} from '@tabler/icons-react'
import { memo, useMemo, useState } from 'react'
import type { EpicDto, IssueDto } from '../../api/types'
import { formatIso } from '../../shared/dateMath'
import { epicColor } from '../../shared/epicPalette'
import { compareIssueKeys, formatHours } from '../../shared/format'
import { buildTreeRows } from '../../shared/issueTree'
import type { Plan } from './store'
import classes from './TaskPanel.module.css'

export type PanelIssue = { issue: IssueDto; plan: Plan; scheduled: boolean; dirty: boolean }

type TaskPanelProps = {
  items: PanelIssue[]
  epics: EpicDto[]
  memberNames: ReadonlyMap<string, string>
  collapsed: boolean
  onToggle(): void
}

export function TaskPanel({ items, epics, memberNames, collapsed, onToggle }: TaskPanelProps) {
  const [search, setSearch] = useState('')
  const [epicId, setEpicId] = useState<string | null>(null)
  const [onlyUnplanned, setOnlyUnplanned] = useState(true)

  const unplannedCount = items.filter((i) => !i.scheduled).length

  const [collapsedNodes, setCollapsedNodes] = useState<ReadonlySet<string>>(new Set())

  // Иерархия из Jira (includes, tested by…): подходящие под фильтр задачи показываются вместе с предками.
  const { rows, matchCount } = useMemo(() => {
    const text = search.trim().toLowerCase()
    const matches = (i: PanelIssue) =>
      (!onlyUnplanned || !i.scheduled) &&
      (!epicId || i.issue.epicId === epicId) &&
      (!text || i.issue.key.toLowerCase().includes(text) || i.issue.summary.toLowerCase().includes(text))
    const rows = buildTreeRows(items, {
      id: (i) => i.issue.id,
      parentId: (i) => i.issue.parentIssueId,
      compare: (a, b) => compareIssueKeys(a.issue.key, b.issue.key),
      matches,
      collapsed: collapsedNodes,
    })
    return { rows, matchCount: items.filter(matches).length }
  }, [items, search, epicId, onlyUnplanned, collapsedNodes])

  const toggleNode = (id: string) =>
    setCollapsedNodes((s) => {
      const next = new Set(s)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  if (collapsed) {
    return (
      <aside className={`${classes.panel} ${classes.collapsed}`} data-unplan-zone data-testid="task-panel">
        <Tooltip label="Показать список задач" position="left">
          <ActionIcon variant="subtle" onClick={onToggle} aria-label="Показать список задач">
            <IconChevronLeft size={18} />
          </ActionIcon>
        </Tooltip>
        <span className={classes.collapsedCount}>Не запланировано: {unplannedCount}</span>
      </aside>
    )
  }

  return (
    <aside className={classes.panel} data-unplan-zone data-testid="task-panel">
      <Stack gap={8} className={classes.head}>
        <Group justify="space-between" wrap="nowrap">
          <Text fw={600}>
            Задачи{' '}
            <Text span c="dimmed" size="sm">
              {matchCount} из {items.length}
            </Text>
          </Text>
          <Tooltip label="Скрыть список" position="left">
            <ActionIcon variant="subtle" onClick={onToggle} aria-label="Скрыть список задач">
              <IconChevronRight size={18} />
            </ActionIcon>
          </Tooltip>
        </Group>
        <TextInput
          size="xs"
          placeholder="Поиск по ключу или названию"
          leftSection={<IconSearch size={14} />}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
        />
        <Select
          size="xs"
          placeholder="Эпик: все"
          clearable
          data={epics.map((e) => ({ value: e.id, label: `${e.key} · ${e.summary}` }))}
          value={epicId}
          onChange={setEpicId}
        />
        <Switch
          size="xs"
          label={`Только незапланированные (${unplannedCount})`}
          checked={onlyUnplanned}
          onChange={(e) => setOnlyUnplanned(e.currentTarget.checked)}
        />
      </Stack>
      <div className={classes.list}>
        {rows.map(({ item, depth, hasChildren, collapsed: nodeCollapsed, context }) => (
          <div key={item.issue.id} className={classes.treeRow} style={{ paddingLeft: depth * 16 }}>
            {hasChildren ? (
              <ActionIcon
                variant="subtle"
                color="gray"
                size="sm"
                className={classes.treeToggle}
                onClick={() => toggleNode(item.issue.id)}
                aria-label={nodeCollapsed ? 'Развернуть' : 'Свернуть'}
              >
                {nodeCollapsed ? <IconChevronRight size={14} /> : <IconChevronDown size={14} />}
              </ActionIcon>
            ) : (
              <span className={classes.treeSpacer} />
            )}
            <TaskCard
              item={item}
              context={context}
              assigneeName={item.plan.assigneeId ? memberNames.get(item.plan.assigneeId) : undefined}
            />
          </div>
        ))}
        {rows.length === 0 && (
          <Text c="dimmed" size="sm" ta="center" mt="md">
            {items.length === 0 ? 'В проекте нет задач. Добавьте эпики на странице проекта.' : 'Нет задач по фильтру.'}
          </Text>
        )}
      </div>
    </aside>
  )
}

const TaskCard = memo(function TaskCard({
  item,
  assigneeName,
  context,
}: {
  item: PanelIssue
  assigneeName?: string
  /** Показана только как предок подходящей под фильтр задачи. */
  context?: boolean
}) {
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: `issue:${item.issue.id}`,
    data: { issueId: item.issue.id },
  })
  return (
    <div
      ref={setNodeRef}
      {...listeners}
      {...attributes}
      className={`${classes.card} ${isDragging ? classes.cardDragging : ''} ${context ? classes.cardContext : ''}`}
      data-testid={`card-${item.issue.key}`}
    >
      <IconGripVertical size={16} className={classes.grip} />
      <TaskCardBody item={item} assigneeName={assigneeName} />
    </div>
  )
})

export function TaskCardBody({ item, assigneeName }: { item: PanelIssue; assigneeName?: string }) {
  const { issue, plan } = item
  return (
    <div className={classes.body}>
      <Group gap={6} wrap="nowrap" justify="space-between">
        <Text size="xs" fw={600}>
          <span className={classes.dot} style={{ background: epicColor(issue.epicKey) }} />
          {issue.key}
        </Text>
        <Badge size="xs" variant="light" color="gray">
          {formatHours(issue.originalEstimateSeconds)}
        </Badge>
      </Group>
      {issue.parentLinkLabel && (
        <Text size="xs" c="dimmed" className={classes.linkLabel}>
          ↳ {issue.parentLinkLabel}
        </Text>
      )}
      <div className={classes.summary}>{issue.summary}</div>
      <Text size="xs" c="dimmed" mt={2}>
        {plan.start ? `${formatIso(plan.start)} – ${formatIso(plan.end)}` : 'без дат'}
        {' · '}
        {assigneeName ??
          (!plan.assigneeId
            ? 'без исполнителя'
            : `${plan.assigneeId === issue.planAssigneeId ? issue.planAssigneeName : 'исполнитель'} (не участник)`)}
        {item.dirty && ' · изменено'}
      </Text>
    </div>
  )
}

export const panelOverlayClass = `${classes.card} ${classes.overlay}`
