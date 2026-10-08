import {
  DndContext,
  DragOverlay,
  PointerSensor,
  pointerWithin,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragMoveEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { ActionIcon, Alert, Anchor, Badge, Button, Group, Loader, Modal, SegmentedControl, Text, Tooltip } from '@mantine/core'
import { useHotkeys } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { useQueryClient } from '@tanstack/react-query'
import {
  IconArrowBackUp,
  IconArrowForwardUp,
  IconCalendarEvent,
  IconDeviceFloppy,
  IconCloudUpload,
  IconFileExport,
  IconX,
} from '@tabler/icons-react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useBlocker, useParams } from 'react-router'
import { ApiError, describeError, http } from '../api/client'
import { fetchProjectExport, keys, useGantt, useRemoveMember, useReorderMembers, useSaveGantt } from '../api/hooks'
import type { GanttDto, IssueDto } from '../api/types'
import { ExportDialog } from '../features/export/ExportDialog'
import { JiraPushDialog } from '../features/jira-push/JiraPushDialog'
import { AddMemberButton } from '../features/gantt/AddMemberButton'
import { BarCard } from '../features/gantt/BarCard'
import { MemberHeader } from '../features/gantt/MemberHeader'
import { useGanttDraft, toSaveRequest, serverPlan, type Plan, type PlanChange } from '../features/gantt/store'
import { panelOverlayClass, TaskCardBody, TaskPanel, type PanelIssue } from '../features/gantt/TaskPanel'
import { LEFT_WIDTH, Timeline, type BarCommit, type BarModel, type Ghost, type TimelineRow } from '../features/gantt/Timeline'
import { hoursByDay } from '../features/gantt/workload'
import {
  durationFromEstimate,
  endForWorkingDays,
  fromDay,
  toDay,
  toDayOrNull,
  type Day,
} from '../shared/dateMath'
import { epicColor } from '../shared/epicPalette'
import { storage } from '../shared/format'
import classes from './ProjectGanttPage.module.css'

type Scale = 'day' | 'week' | 'month'
const DAY_WIDTH: Record<Scale, number> = { day: 40, week: 16, month: 4 }
/** Минимальная длина шкалы, чтобы на крупном масштабе она не обрывалась посреди экрана. */
const MIN_RANGE_DAYS: Record<Scale, number> = { day: 0, week: 140, month: 420 }

export function ProjectGanttPage() {
  const { id = '' } = useParams()
  const gantt = useGantt(id)

  if (gantt.isPending) return <Loader m="xl" />
  if (gantt.isError) return <Alert color="red" m="md">{describeError(gantt.error)}</Alert>
  return <GanttEditor projectId={id} data={gantt.data} />
}

function GanttEditor({ projectId, data }: { projectId: string; data: GanttDto }) {
  const qc = useQueryClient()
  const save = useSaveGantt(projectId)
  const reorderMembers = useReorderMembers(projectId).mutate
  const removeMember = useRemoveMember(projectId).mutate

  const server = useGanttDraft((s) => s.server)
  const changes = useGanttDraft((s) => s.changes)
  const canUndo = useGanttDraft((s) => s.history.length > 0)
  const canRedo = useGanttDraft((s) => s.future.length > 0)
  const { apply, undo, redo, reset, load } = useGanttDraft.getState()
  const loadedProject = useGanttDraft((s) => s.projectId)

  useEffect(() => {
    useGanttDraft.getState().load(projectId, data.issues)
  }, [projectId, data.issues])

  const [scale, setScale] = useState<Scale>(() => (storage.get('gantt.scale') as Scale) ?? 'day')
  const [panelCollapsed, setPanelCollapsed] = useState(() => storage.get('gantt.panelCollapsed') === '1')
  const [exportOpened, setExportOpened] = useState(false)
  const [pushOpened, setPushOpened] = useState(false)
  const [conflict, setConflict] = useState<string[] | null>(null)
  const scrollRef = useRef<HTMLDivElement>(null)
  const dayWidth = DAY_WIDTH[scale]
  const hoursPerDay = data.project.hoursPerDay
  const today = toDay(data.today)

  // ---------- действующие значения (сервер + черновик)

  const ready = loadedProject === projectId
  const issues = useMemo(() => (ready ? [...server.values()] : data.issues), [ready, server, data.issues])
  const memberIds = useMemo(() => new Set(data.members.map((m) => m.userId)), [data.members])
  const memberNames = useMemo(() => new Map(data.members.map((m) => [m.userId, m.displayName])), [data.members])

  const planOf = useCallback((issue: IssueDto): Plan => changes.get(issue.id) ?? serverPlan(issue), [changes])

  const commit = useCallback((change: PlanChange) => apply(change), [apply])

  const { rows, panelItems, range } = useMemo(() => {
    const barsByMember = new Map<string, BarModel[]>(data.members.map((m) => [m.userId, []]))
    const panelItems: PanelIssue[] = []
    let min = toDay(data.range.from)
    let max = toDay(data.range.to)

    for (const issue of issues) {
      const plan = planOf(issue)
      const start = toDayOrNull(plan.start)
      const end = toDayOrNull(plan.end)
      const onTimeline = start !== null && end !== null && plan.assigneeId !== null && memberIds.has(plan.assigneeId)
      const dirty = changes.has(issue.id)
      panelItems.push({ issue, plan, scheduled: onTimeline, dirty })
      if (!onTimeline) continue

      min = Math.min(min, start - 7)
      max = Math.max(max, end + 14)
      const differs =
        plan.start !== issue.jiraStartDate || plan.end !== issue.jiraEndDate || plan.assigneeId !== issue.jiraAssigneeId
      barsByMember.get(plan.assigneeId!)!.push({
        id: issue.id,
        issueKey: issue.key,
        rowId: plan.assigneeId!,
        start,
        end,
        label: `${issue.key} · ${issue.summary}`,
        color: epicColor(issue.epicKey),
        estimateSeconds: issue.originalEstimateSeconds,
        dirty,
        differs,
        done: issue.statusCategory === 'done',
        card: (
          <BarCard
            issue={issue}
            plan={plan}
            assigneeName={memberNames.get(plan.assigneeId!)}
            onResetToJira={() =>
              commit({ issueId: issue.id, start: issue.jiraStartDate, end: issue.jiraEndDate, assigneeId: issue.jiraAssigneeId })
            }
            onUnplan={() => commit({ issueId: issue.id, start: null, end: null, assigneeId: null })}
          />
        ),
      })
    }

    const rows: TimelineRow[] = data.members.map((member, index) => {
      const bars = barsByMember.get(member.userId)!
      const hours = [...hoursByDay(bars.map((b) => ({ start: b.start, end: b.end, estimateSeconds: b.estimateSeconds }))).values()]
        .reduce((a, b) => a + b, 0)
      return {
        id: member.userId,
        droppable: true,
        bars,
        header: (
          <MemberHeader
            member={member}
            taskCount={bars.length}
            hours={hours}
            isFirst={index === 0}
            isLast={index === data.members.length - 1}
            onMove={(direction) => {
              const ids = data.members.map((m) => m.userId)
              const target = index + direction
              ;[ids[index], ids[target]] = [ids[target], ids[index]]
              reorderMembers(ids, { onError: (e) => notifications.show({ color: 'red', message: describeError(e) }) })
            }}
            onRemove={() =>
              removeMember(member.userId, {
                onError: (e) => notifications.show({ color: 'red', title: 'Не удалось убрать участника', message: describeError(e) }),
              })
            }
          />
        ),
      }
    })

    return { rows, panelItems, range: { from: min, to: Math.max(max, min + MIN_RANGE_DAYS[scale]) } }
  }, [issues, planOf, changes, data.members, data.range, memberIds, memberNames, commit, reorderMembers, removeMember, scale])

  // ---------- правки мышью на шкале

  const onCommit = useCallback(
    (c: BarCommit) => {
      if (c.unplan) apply({ issueId: c.issueId, start: null, end: null, assigneeId: null })
      else apply({ issueId: c.issueId, start: fromDay(c.start), end: fromDay(c.end), assigneeId: c.rowId })
    },
    [apply],
  )

  // ---------- перенос из списка (dnd-kit)

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }))
  const [activeIssue, setActiveIssue] = useState<PanelIssue | null>(null)
  const [ghost, setGhost] = useState<Ghost | null>(null)

  // Позицию указателя отслеживаем сами: event.delta у dnd-kit может отставать,
  // если отпускание пришло в том же кадре, что и последнее движение.
  const pointerRef = useRef({ x: 0, y: 0 })
  const stopTrackingRef = useRef<(() => void) | null>(null)

  const trackPointer = (initial: PointerEvent) => {
    pointerRef.current = { x: initial.clientX, y: initial.clientY }
    const update = (e: PointerEvent) => {
      pointerRef.current = { x: e.clientX, y: e.clientY }
    }
    window.addEventListener('pointermove', update, true)
    window.addEventListener('pointerup', update, true)
    stopTrackingRef.current = () => {
      window.removeEventListener('pointermove', update, true)
      window.removeEventListener('pointerup', update, true)
    }
  }

  const stopTracking = () => {
    stopTrackingRef.current?.()
    stopTrackingRef.current = null
    setActiveIssue(null)
    setGhost(null)
  }

  const dropTarget = (event: DragMoveEvent | DragEndEvent): Ghost | null => {
    const scroller = scrollRef.current
    const issueId = event.active.data.current?.issueId as string | undefined
    const pointer = pointerRef.current
    const rowEl = document
      .elementsFromPoint(pointer.x, pointer.y)
      .map((el) => el.closest<HTMLElement>('[data-row-id]'))
      .find(Boolean)
    const rowId = rowEl?.dataset.rowId
    if (!rowId || !scroller || !issueId) return null
    const issue = server.get(issueId)
    if (!issue) return null

    const x = pointer.x - scroller.getBoundingClientRect().left - LEFT_WIDTH + scroller.scrollLeft
    const start = range.from + Math.floor(x / dayWidth)
    const plan = planOf(issue)
    let end: Day
    if (plan.start && plan.end) {
      // У задачи уже есть даты — длительность сохраняется.
      end = start + (toDay(plan.end) - toDay(plan.start))
    } else {
      end = endForWorkingDays(start, durationFromEstimate(issue.originalEstimateSeconds, hoursPerDay))
    }
    return { rowId, start, end }
  }

  const onDragStart = (event: DragStartEvent) => {
    const issueId = event.active.data.current?.issueId as string
    trackPointer(event.activatorEvent as PointerEvent)
    setActiveIssue(panelItems.find((p) => p.issue.id === issueId) ?? null)
  }

  const onDragMove = (event: DragMoveEvent) => {
    const next = dropTarget(event)
    setGhost((prev) =>
      prev?.rowId === next?.rowId && prev?.start === next?.start && prev?.end === next?.end ? prev : next,
    )
  }

  const onDragEnd = (event: DragEndEvent) => {
    const target = dropTarget(event)
    stopTracking()
    if (!target) return
    apply({
      issueId: event.active.data.current!.issueId as string,
      start: fromDay(target.start),
      end: fromDay(target.end),
      assigneeId: target.rowId,
    })
  }

  // ---------- сохранение

  const dirtyCount = changes.size

  const doSave = useCallback(async () => {
    const state = useGanttDraft.getState()
    if (state.changes.size === 0 || save.isPending) return
    try {
      const result = await save.mutateAsync(toSaveRequest(state))
      const fresh = qc.getQueryData<GanttDto>(keys.gantt(projectId))
      if (fresh) load(projectId, fresh.issues, false)
      notifications.show({ color: 'green', message: `Сохранено задач: ${result.saved}` })
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setConflict(e.problem.issueIds ?? [])
        return
      }
      if (e instanceof ApiError && e.status === 400 && e.problem.errors) {
        const lines = Object.entries(e.problem.errors).map(
          ([issueId, messages]) => `${server.get(issueId)?.key ?? issueId}: ${messages.join(' ')}`,
        )
        notifications.show({ color: 'red', title: e.message, message: lines.join('\n'), autoClose: 10_000 })
        return
      }
      notifications.show({ color: 'red', title: 'Не удалось сохранить', message: describeError(e) })
    }
  }, [save, qc, projectId, load, server])

  const resolveConflict = async (keepMine: boolean) => {
    setConflict(null)
    const fresh = await qc.fetchQuery({
      queryKey: keys.gantt(projectId),
      queryFn: () => http.get<GanttDto>(`/api/projects/${projectId}/gantt`),
      staleTime: 0,
    })
    load(projectId, fresh.issues, keepMine)
  }

  useHotkeys([
    ['mod+Z', () => undo()],
    ['mod+shift+Z', () => redo()],
    ['mod+Y', () => redo()],
    ['mod+S', () => void doSave()],
  ])

  // Несохранённые правки: предупреждение при закрытии вкладки и уходе со страницы.
  useEffect(() => {
    if (dirtyCount === 0) return
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault()
    }
    window.addEventListener('beforeunload', handler)
    return () => window.removeEventListener('beforeunload', handler)
  }, [dirtyCount])
  const blocker = useBlocker(({ currentLocation, nextLocation }) => dirtyCount > 0 && currentLocation.pathname !== nextLocation.pathname)

  // ---------- прокрутка к сегодняшнему дню

  const scrollToToday = useCallback(() => {
    const scroller = scrollRef.current
    if (!scroller) return
    const x = (today - range.from) * dayWidth
    scroller.scrollTo({ left: Math.max(0, x - (scroller.clientWidth - LEFT_WIDTH) / 3), behavior: 'smooth' })
  }, [today, range.from, dayWidth])

  const didInitialScroll = useRef(false)
  useEffect(() => {
    if (didInitialScroll.current || !scrollRef.current) return
    didInitialScroll.current = true
    const scroller = scrollRef.current
    scroller.scrollLeft = Math.max(0, (today - range.from) * dayWidth - (scroller.clientWidth - LEFT_WIDTH) / 3)
  }, [today, range.from, dayWidth])

  const changeScale = (value: string) => {
    const scroller = scrollRef.current
    // Сохраняем день в центре экрана при смене масштаба.
    const centerDay = scroller ? range.from + (scroller.scrollLeft + (scroller.clientWidth - LEFT_WIDTH) / 2) / dayWidth : today
    setScale(value as Scale)
    storage.set('gantt.scale', value)
    requestAnimationFrame(() => {
      if (scroller) scroller.scrollLeft = (centerDay - range.from) * DAY_WIDTH[value as Scale] - (scroller.clientWidth - LEFT_WIDTH) / 2
    })
  }

  const togglePanel = () => {
    setPanelCollapsed((c) => {
      storage.set('gantt.panelCollapsed', c ? '0' : '1')
      return !c
    })
  }

  return (
    <div className={classes.page}>
      <Group className={classes.toolbar} justify="space-between" wrap="nowrap">
        <Group gap="sm" wrap="nowrap">
          <Anchor component={Link} to={`/projects/${projectId}`} fw={600} size="lg" c="var(--mantine-color-text)">
            {data.project.name}
          </Anchor>
          <SegmentedControl
            size="xs"
            value={scale}
            onChange={changeScale}
            data={[
              { value: 'day', label: 'День' },
              { value: 'week', label: 'Неделя' },
              { value: 'month', label: 'Месяц' },
            ]}
          />
          <Button size="xs" variant="default" leftSection={<IconCalendarEvent size={14} />} onClick={scrollToToday}>
            Сегодня
          </Button>
          <Tooltip label="Отменить действие (Ctrl+Z)">
            <ActionIcon variant="default" onClick={undo} disabled={!canUndo} aria-label="Отменить действие">
              <IconArrowBackUp size={16} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Повторить (Ctrl+Shift+Z)">
            <ActionIcon variant="default" onClick={redo} disabled={!canRedo} aria-label="Повторить">
              <IconArrowForwardUp size={16} />
            </ActionIcon>
          </Tooltip>
        </Group>
        <Group gap="sm" wrap="nowrap">
          <Button size="xs" variant="default" leftSection={<IconFileExport size={14} />} onClick={() => setExportOpened(true)}>
            Экспорт
          </Button>
          <Button size="xs" variant="default" leftSection={<IconCloudUpload size={14} />} onClick={() => setPushOpened(true)}>
            В Jira
          </Button>
          <Button size="xs" variant="default" leftSection={<IconX size={14} />} onClick={reset} disabled={dirtyCount === 0}>
            Отменить
          </Button>
          <Button
            size="xs"
            leftSection={<IconDeviceFloppy size={14} />}
            onClick={() => void doSave()}
            disabled={dirtyCount === 0}
            loading={save.isPending}
            data-testid="save"
          >
            Сохранить{' '}
            {dirtyCount > 0 && (
              <Badge size="xs" color="white" c="blue" ml={6} circle>
                {dirtyCount}
              </Badge>
            )}
          </Button>
        </Group>
      </Group>

      <DndContext sensors={sensors} collisionDetection={pointerWithin} onDragStart={onDragStart} onDragMove={onDragMove} onDragEnd={onDragEnd} onDragCancel={stopTracking}>
        <div className={classes.workspace}>
          <div className={classes.timeline}>
            {data.members.length === 0 && (
              <Alert color="blue" m="sm" p="xs">
                Добавьте участников кнопкой «+ Участник» внизу и перетащите на их строки задачи из списка справа.
              </Alert>
            )}
            <Timeline
              rows={rows}
              from={range.from}
              to={range.to}
              today={today}
              dayWidth={dayWidth}
              hoursPerDay={hoursPerDay}
              onCommit={onCommit}
              ghost={ghost}
              scrollRef={scrollRef}
              footer={<AddMemberButton projectId={projectId} connectionId={data.project.connectionId} memberIds={memberIds} />}
            />
          </div>
          <TaskPanel
            items={panelItems}
            epics={data.epics}
            memberNames={memberNames}
            collapsed={panelCollapsed}
            onToggle={togglePanel}
          />
        </div>
        <DragOverlay dropAnimation={null}>
          {activeIssue && (
            <div className={panelOverlayClass}>
              <TaskCardBody item={activeIssue} />
            </div>
          )}
        </DragOverlay>
      </DndContext>

      <ExportDialog
        opened={exportOpened}
        onClose={() => setExportOpened(false)}
        cacheKey={`project:${projectId}`}
        load={(params) => fetchProjectExport(projectId, params)}
        withGroupBy
        dirtyCount={dirtyCount}
        onSave={() => void doSave()}
        saving={save.isPending}
      />

      <JiraPushDialog
        projectId={projectId}
        opened={pushOpened}
        onClose={() => setPushOpened(false)}
        dirtyCount={dirtyCount}
        onSave={() => void doSave()}
        saving={save.isPending}
      />

      <Modal opened={conflict !== null} onClose={() => setConflict(null)} title="Конфликт изменений">
        <Text size="sm" mb="md">
          Задачи ({conflict?.map((id) => server.get(id)?.key ?? id).join(', ')}) изменены в другой вкладке или при
          синхронизации. Ничего не сохранено.
        </Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={() => void resolveConflict(false)}>
            Перезагрузить без моих правок
          </Button>
          <Button onClick={() => void resolveConflict(true)}>Перезагрузить, оставив мои правки</Button>
        </Group>
      </Modal>

      <Modal opened={blocker.state === 'blocked'} onClose={() => blocker.reset?.()} title="Несохранённые изменения">
        <Text size="sm" mb="md">
          На диаграмме есть несохранённые правки ({dirtyCount}). Уйти со страницы без сохранения?
        </Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={() => blocker.reset?.()}>
            Остаться
          </Button>
          <Button color="red" onClick={() => blocker.proceed?.()}>
            Уйти без сохранения
          </Button>
        </Group>
      </Modal>
    </div>
  )
}
