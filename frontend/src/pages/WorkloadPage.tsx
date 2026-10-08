import { Alert, Anchor, Badge, Button, Card, Group, Loader, Paper, Select, SimpleGrid, Stack, Table, Text, TextInput, Title } from '@mantine/core'
import { IconFileExport } from '@tabler/icons-react'
import { useMemo, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { describeError } from '../api/client'
import { fetchWorkloadExport, useUsers, useWorkload } from '../api/hooks'
import type { WorkloadDto } from '../api/types'
import { ExportDialog } from '../features/export/ExportDialog'
import { BarCard } from '../features/gantt/BarCard'
import { Timeline, type BarModel, type TimelineRow } from '../features/gantt/Timeline'
import { WorkloadHistogram } from '../features/workload/WorkloadHistogram'
import { toDay } from '../shared/dateMath'
import { epicColor } from '../shared/epicPalette'
import { formatHours } from '../shared/format'

const NO_PROJECT = '__none__'

export function WorkloadPage() {
  const { userId } = useParams()
  const navigate = useNavigate()
  const users = useUsers()
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const workload = useWorkload(userId, from || undefined, to || undefined)

  const userOptions = (users.data ?? []).map((u) => ({ value: u.id, label: `${u.displayName} (${u.name})` }))

  return (
    <Stack p="md" gap="md">
      <Group justify="space-between" align="flex-end">
        <Title order={2}>Нагрузка исполнителя</Title>
      </Group>
      <Group align="flex-end">
        <Select
          label="Исполнитель"
          placeholder="Выберите исполнителя"
          searchable
          w={340}
          data={userOptions}
          value={userId ?? null}
          onChange={(value) => value && navigate(`/workload/${value}`)}
          nothingFoundMessage="Никого не найдено. Синхронизируйте пользователей в подключении."
        />
        <TextInput label="С" type="date" w={160} value={from || workload.data?.range.from || ''} onChange={(e) => setFrom(e.currentTarget.value)} />
        <TextInput label="По" type="date" w={160} value={to || workload.data?.range.to || ''} onChange={(e) => setTo(e.currentTarget.value)} />
        {(from || to) && (
          <Button variant="subtle" onClick={() => { setFrom(''); setTo('') }}>
            Сбросить период
          </Button>
        )}
      </Group>

      {!userId && <Text c="dimmed">Выберите исполнителя, чтобы увидеть его задачи по всем проектам.</Text>}
      {workload.isPending && userId && <Loader />}
      {workload.isError && <Alert color="red">{describeError(workload.error)}</Alert>}
      {workload.data && <WorkloadView data={workload.data} />}
    </Stack>
  )
}

function WorkloadView({ data }: { data: WorkloadDto }) {
  const [exportOpened, setExportOpened] = useState(false)
  const scrollRef = useRef<HTMLDivElement>(null)
  const from = toDay(data.range.from)
  const to = toDay(data.range.to)

  // Строки — проекты; задача из нескольких проектов показывается в первом (как в экспорте).
  const rows = useMemo<TimelineRow[]>(() => {
    const groups = new Map<string, { name: string; bars: BarModel[] }>()
    for (const { issue, projects } of data.tasks) {
      const project = projects[0]
      const rowId = project?.id ?? NO_PROJECT
      if (!groups.has(rowId)) groups.set(rowId, { name: project?.name ?? 'Без проекта', bars: [] })
      groups.get(rowId)!.bars.push({
        id: issue.id,
        issueKey: issue.key,
        rowId,
        start: toDay(issue.planStartDate!),
        end: toDay(issue.planEndDate!),
        label: `${issue.key} · ${issue.summary}`,
        color: epicColor(issue.epicKey),
        estimateSeconds: issue.originalEstimateSeconds,
        differs: issue.differsFromJira,
        done: issue.statusCategory === 'done',
        card: (
          <BarCard
            issue={issue}
            readOnly
            plan={{ start: issue.planStartDate, end: issue.planEndDate, assigneeId: issue.planAssigneeId }}
            assigneeName={issue.planAssigneeName ?? undefined}
          />
        ),
      })
    }
    return [...groups.entries()]
      .sort(([a, x], [b, y]) => (a === NO_PROJECT ? 1 : b === NO_PROJECT ? -1 : x.name.localeCompare(y.name)))
      .map(([id, g]) => ({
        id,
        bars: g.bars,
        header:
          id === NO_PROJECT ? (
            <Text size="sm" c="dimmed">Без проекта</Text>
          ) : (
            <Anchor component={Link} to={`/projects/${id}/gantt`} size="sm" fw={500}>
              {g.name}
            </Anchor>
          ),
      }))
  }, [data.tasks])

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Title order={3}>{data.user.displayName}</Title>
        <Button variant="default" leftSection={<IconFileExport size={16} />} onClick={() => setExportOpened(true)}>
          Экспорт
        </Button>
      </Group>

      <SimpleGrid cols={{ base: 2, md: 4 }}>
        <Stat label="Часов за период" value={`${data.totalHours} ч`} />
        <Stat label="Загрузка" value={`${data.loadPercent}%`} hint={`ёмкость ${data.capacityHours} ч при ${data.hoursPerDay} ч/день`} color={data.loadPercent > 100 ? 'red' : undefined} />
        <Stat label="Задач с датами" value={String(data.tasks.length)} hint={data.withoutEstimate ? `без оценки: ${data.withoutEstimate}` : undefined} />
        <Stat label="Задач без дат" value={String(data.unscheduled.length)} />
      </SimpleGrid>

      <Paper withBorder p="sm">
        <Text fw={600} size="sm" mb="xs">
          Часы по дням
        </Text>
        <WorkloadHistogram days={data.days} hoursPerDay={data.hoursPerDay} today={data.today} />
      </Paper>

      <Paper withBorder style={{ height: Math.min(560, 90 + rows.length * 80), display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
        {rows.length > 0 ? (
          <Timeline
            rows={rows}
            from={from}
            to={to}
            today={toDay(data.today)}
            dayWidth={24}
            hoursPerDay={data.hoursPerDay}
            readOnly
            scrollRef={scrollRef}
            cornerTitle="Проекты"
          />
        ) : (
          <Text c="dimmed" p="md">
            За период нет задач с датами.
          </Text>
        )}
      </Paper>

      {data.unscheduled.length > 0 && (
        <Card withBorder>
          <Text fw={600} size="sm" mb="xs">
            Задачи без дат
          </Text>
          <Table>
            <Table.Tbody>
              {data.unscheduled.map(({ issue, projects }) => (
                <Table.Tr key={issue.id}>
                  <Table.Td>
                    <Anchor href={issue.jiraUrl} target="_blank" rel="noreferrer" size="sm">
                      {issue.key}
                    </Anchor>
                  </Table.Td>
                  <Table.Td>{issue.summary}</Table.Td>
                  <Table.Td>{projects.map((p) => p.name).join(', ') || '—'}</Table.Td>
                  <Table.Td>
                    <Badge variant="light" color="gray">
                      {formatHours(issue.originalEstimateSeconds)}
                    </Badge>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Card>
      )}

      <ExportDialog
        opened={exportOpened}
        onClose={() => setExportOpened(false)}
        cacheKey={`workload:${data.user.id}`}
        load={(params) => fetchWorkloadExport(data.user.id, { ...params, from: params.from ?? data.range.from, to: params.to ?? data.range.to })}
      />
    </Stack>
  )
}

function Stat({ label, value, hint, color }: { label: string; value: string; hint?: string; color?: string }) {
  return (
    <Paper withBorder p="sm">
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text size="xl" fw={700} c={color}>
        {value}
      </Text>
      {hint && (
        <Text size="xs" c="dimmed">
          {hint}
        </Text>
      )}
    </Paper>
  )
}
