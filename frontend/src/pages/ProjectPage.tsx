import {
  ActionIcon,
  Alert,
  Anchor,
  Badge,
  Button,
  Checkbox,
  Group,
  Loader,
  Modal,
  ScrollArea,
  Select,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Textarea,
  Title,
  Tooltip,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import {
  IconArrowBackUp,
  IconChartBar,
  IconChevronDown,
  IconChevronRight,
  IconCloudUpload,
  IconCopy,
  IconLink,
  IconPencil,
  IconPlus,
  IconRefresh,
  IconTrash,
  IconUnlink,
} from '@tabler/icons-react'
import { useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { describeError } from '../api/client'
import {
  useAttachEpics,
  useAttachIssues,
  useDeleteProject,
  useDetachIssue,
  useConnections,
  useEpicSearch,
  useProject,
  useResetToJira,
  useSyncProject,
  useUpdateProject,
} from '../api/hooks'
import type { ImportSummary, ProjectDto } from '../api/types'
import { JiraPushDialog } from '../features/jira-push/JiraPushDialog'
import { CopyProjectModal } from '../features/projects/CopyProjectModal'
import { formatIso } from '../shared/dateMath'
import { epicColor } from '../shared/epicPalette'
import { compareIssueKeys, formatHours } from '../shared/format'
import { buildTreeRows } from '../shared/issueTree'

export function ProjectPage() {
  const { id = '' } = useParams()
  const project = useProject(id)
  if (project.isPending) return <Loader m="xl" />
  if (project.isError) return <Alert color="red" m="md">{describeError(project.error)}</Alert>
  return <ProjectView project={project.data} />
}

function summaryText(s: ImportSummary): string {
  const parts = [`эпиков ${s.epics}`, `создано задач ${s.created}`, `обновлено ${s.updated}`, `без изменений ${s.skipped}`]
  if (s.differsFromJira) parts.push(`план отличается от Jira у ${s.differsFromJira}`)
  if (s.notFoundEpics.length) parts.push(`не найдены: ${s.notFoundEpics.join(', ')}`)
  return parts.join(', ')
}

function ProjectView({ project }: { project: ProjectDto }) {
  const navigate = useNavigate()
  const sync = useSyncProject(project.id)
  const detach = useDetachIssue(project.id)
  const reset = useResetToJira(project.id)
  const remove = useDeleteProject()
  const [onlyDiffers, setOnlyDiffers] = useState(false)
  const [search, setSearch] = useState('')
  const [addEpics, setAddEpics] = useState(false)
  const [attachKeys, setAttachKeys] = useState(false)
  const [editing, setEditing] = useState(false)
  const [lastImport, setLastImport] = useState<ImportSummary | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [copying, setCopying] = useState(false)
  const [pushing, setPushing] = useState(false)

  const [collapsedNodes, setCollapsedNodes] = useState<ReadonlySet<string>>(new Set())
  // Иерархия из Jira: подходящие под фильтр задачи показываются вместе с предками.
  const issues = useMemo(() => {
    const text = search.trim().toLowerCase()
    const filtering = onlyDiffers || text.length > 0
    return buildTreeRows(project.issues, {
      id: (i) => i.id,
      parentId: (i) => i.parentIssueId,
      compare: (a, b) => compareIssueKeys(a.key, b.key),
      matches: filtering
        ? (i) =>
            (!onlyDiffers || i.differsFromJira) &&
            (!text || i.key.toLowerCase().includes(text) || i.summary.toLowerCase().includes(text))
        : undefined,
      collapsed: collapsedNodes,
    })
  }, [project.issues, onlyDiffers, search, collapsedNodes])
  const toggleNode = (id: string) =>
    setCollapsedNodes((s) => {
      const next = new Set(s)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  const differsCount = project.issues.filter((i) => i.differsFromJira).length

  const runSync = async () => {
    try {
      setLastImport(await sync.mutateAsync())
    } catch (e) {
      notifications.show({ color: 'red', title: 'Синхронизация не удалась', message: describeError(e) })
    }
  }

  const onError = (e: unknown) => notifications.show({ color: 'red', message: describeError(e) })

  return (
    <Stack p="md" gap="md">
      <Group justify="space-between" align="flex-start">
        <Stack gap={2}>
          <Group gap="xs">
            <Title order={2}>{project.name}</Title>
            <ActionIcon variant="subtle" onClick={() => setEditing(true)} aria-label="Изменить проект">
              <IconPencil size={16} />
            </ActionIcon>
          </Group>
          {project.description && <Text c="dimmed">{project.description}</Text>}
          <Text size="sm" c="dimmed">
            {project.connectionName} · участников {project.members.length}
          </Text>
        </Stack>
        <Group>
          <Button component={Link} to={`/projects/${project.id}/gantt`} leftSection={<IconChartBar size={16} />}>
            Диаграмма
          </Button>
          <Button variant="default" leftSection={<IconRefresh size={16} />} onClick={runSync} loading={sync.isPending} disabled={project.epics.length === 0}>
            Пересинхронизировать
          </Button>
          <Button variant="default" leftSection={<IconCloudUpload size={16} />} onClick={() => setPushing(true)}>
            Отправить в Jira
          </Button>
          <Button variant="default" leftSection={<IconCopy size={16} />} onClick={() => setCopying(true)}>
            Копировать
          </Button>
          <Button variant="subtle" color="red" leftSection={<IconTrash size={16} />} onClick={() => setConfirmDelete(true)}>
            Удалить
          </Button>
        </Group>
      </Group>

      {lastImport && (
        <Alert color="green" withCloseButton onClose={() => setLastImport(null)} title="Импорт завершён">
          {summaryText(lastImport)}. Локальные правки плана сохранены.
        </Alert>
      )}

      <Stack gap="xs">
        <Group justify="space-between">
          <Title order={4}>Эпики</Title>
          <Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => setAddEpics(true)}>
            Добавить эпики
          </Button>
        </Group>
        {project.epics.length === 0 ? (
          <Text c="dimmed" size="sm">
            Эпиков нет. Добавьте эпики из Jira — их задачи загрузятся в проект.
          </Text>
        ) : (
          <Group gap="xs">
            {project.epics.map((e) => (
              <Badge key={e.id} size="lg" variant="outline" color="dark" leftSection={<span style={{ display: 'inline-block', width: 10, height: 10, borderRadius: 3, background: epicColor(e.key) }} />}>
                {e.key} · {e.summary}
              </Badge>
            ))}
          </Group>
        )}
      </Stack>

      <Stack gap="xs">
        <Group justify="space-between">
          <Title order={4}>Задачи ({project.issues.length})</Title>
          <Group>
            <TextInput size="xs" placeholder="Поиск" value={search} onChange={(e) => setSearch(e.currentTarget.value)} />
            <Switch label={`Отличается от Jira (${differsCount})`} checked={onlyDiffers} onChange={(e) => setOnlyDiffers(e.currentTarget.checked)} />
            <Button size="xs" variant="default" leftSection={<IconLink size={14} />} onClick={() => setAttachKeys(true)}>
              Привязать задачи
            </Button>
          </Group>
        </Group>
        <ScrollArea>
          <Table striped highlightOnHover verticalSpacing={4} fz="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Ключ</Table.Th>
                <Table.Th>Название</Table.Th>
                <Table.Th>Эпик</Table.Th>
                <Table.Th>Статус</Table.Th>
                <Table.Th>Оценка</Table.Th>
                <Table.Th>План</Table.Th>
                <Table.Th>Исполнитель</Table.Th>
                <Table.Th>Jira</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {issues.map(({ item: i, depth, hasChildren, collapsed, context }) => (
                <Table.Tr key={i.id} style={context ? { opacity: 0.55 } : undefined}>
                  <Table.Td style={{ whiteSpace: 'nowrap' }}>
                    <Group gap={2} wrap="nowrap" pl={depth * 18}>
                      {hasChildren ? (
                        <ActionIcon
                          variant="subtle"
                          color="gray"
                          size="sm"
                          onClick={() => toggleNode(i.id)}
                          aria-label={collapsed ? 'Развернуть' : 'Свернуть'}
                        >
                          {collapsed ? <IconChevronRight size={14} /> : <IconChevronDown size={14} />}
                        </ActionIcon>
                      ) : (
                        <span style={{ display: 'inline-block', width: 22 }} />
                      )}
                      <Anchor href={i.jiraUrl} target="_blank" rel="noreferrer">
                        {i.key}
                      </Anchor>
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    {i.parentLinkLabel && (
                      <Text span size="xs" c="dimmed" fs="italic" mr={6}>
                        ↳ {i.parentLinkLabel}
                      </Text>
                    )}
                    {i.summary}
                  </Table.Td>
                  <Table.Td>{i.epicKey ?? '—'}</Table.Td>
                  <Table.Td>{i.status}</Table.Td>
                  <Table.Td>{formatHours(i.originalEstimateSeconds)}</Table.Td>
                  <Table.Td style={{ whiteSpace: 'nowrap' }}>
                    {i.planStartDate ? `${formatIso(i.planStartDate)} – ${formatIso(i.planEndDate)}` : '—'}
                  </Table.Td>
                  <Table.Td>{i.planAssigneeName ?? '—'}</Table.Td>
                  <Table.Td style={{ whiteSpace: 'nowrap' }}>
                    {i.differsFromJira ? (
                      <Text size="xs" c="orange.8">
                        ≠ {i.jiraStartDate ? `${formatIso(i.jiraStartDate)} – ${formatIso(i.jiraEndDate)}` : 'без дат'} ·{' '}
                        {i.jiraAssigneeName ?? 'без исполнителя'}
                      </Text>
                    ) : (
                      <Text size="xs" c="dimmed">
                        совпадает
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4} wrap="nowrap">
                      {i.differsFromJira && (
                        <Tooltip label="Сбросить к Jira">
                          <ActionIcon variant="subtle" color="orange" onClick={() => reset.mutate(i.id, { onError })} aria-label="Сбросить к Jira">
                            <IconArrowBackUp size={16} />
                          </ActionIcon>
                        </Tooltip>
                      )}
                      <Tooltip label="Отвязать от проекта">
                        <ActionIcon variant="subtle" color="gray" onClick={() => detach.mutate(i.id, { onError })} aria-label="Отвязать">
                          <IconUnlink size={16} />
                        </ActionIcon>
                      </Tooltip>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
        {issues.length === 0 && (
          <Text c="dimmed" size="sm">
            Нет задач.
          </Text>
        )}
      </Stack>

      <AddEpicsModal opened={addEpics} onClose={() => setAddEpics(false)} project={project} onImported={setLastImport} />
      <AttachIssuesModal opened={attachKeys} onClose={() => setAttachKeys(false)} projectId={project.id} />
      <CopyProjectModal source={copying ? project : null} onClose={() => setCopying(false)} />
      <JiraPushDialog projectId={project.id} opened={pushing} onClose={() => setPushing(false)} />
      <EditProjectModal opened={editing} onClose={() => setEditing(false)} project={project} />
      <Modal opened={confirmDelete} onClose={() => setConfirmDelete(false)} title="Удалить проект?">
        <Text size="sm" mb="md">
          Удалится только локальный проект (участники и привязки). Задачи останутся в базе и в Jira.
        </Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={() => setConfirmDelete(false)}>
            Отмена
          </Button>
          <Button color="red" loading={remove.isPending} onClick={() => remove.mutate(project.id, { onSuccess: () => navigate('/projects'), onError })}>
            Удалить
          </Button>
        </Group>
      </Modal>
    </Stack>
  )
}

function AddEpicsModal({ opened, onClose, project, onImported }: { opened: boolean; onClose(): void; project: ProjectDto; onImported(s: ImportSummary): void }) {
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search, 350)
  const [selected, setSelected] = useState<string[]>([])
  const [jiraProject, setJiraProject] = useState<string | null>(null)
  const connection = useConnections().data?.find((c) => c.id === project.connectionId)
  const jiraProjects = connection?.projectKeys ?? []
  const epics = useEpicSearch(project.connectionId, debounced, jiraProject, opened)
  const attach = useAttachEpics(project.id)
  const linked = new Set(project.epics.map((e) => e.key))

  const toggle = (key: string) => setSelected((s) => (s.includes(key) ? s.filter((k) => k !== key) : [...s, key]))

  const run = async () => {
    try {
      const summary = await attach.mutateAsync(selected)
      onImported(summary)
      setSelected([])
      onClose()
    } catch (e) {
      notifications.show({ color: 'red', title: 'Импорт не удался', message: describeError(e) })
    }
  }

  return (
    <Modal opened={opened} onClose={onClose} title="Добавить эпики из Jira" size="lg">
      <Stack gap="sm">
        <Group gap="xs" grow={jiraProjects.length <= 1}>
          <TextInput placeholder="Поиск по названию эпика" value={search} onChange={(e) => setSearch(e.currentTarget.value)} data-autofocus />
          {jiraProjects.length > 1 && (
            <Select
              w={200}
              placeholder="Все проекты Jira"
              clearable
              data={jiraProjects}
              value={jiraProject}
              onChange={setJiraProject}
            />
          )}
        </Group>
        {epics.isFetching && <Loader size="sm" />}
        {epics.isError && <Alert color="red">{describeError(epics.error)}</Alert>}
        <ScrollArea.Autosize mah={360}>
          <Stack gap={6}>
            {epics.data?.map((e) => (
              <Checkbox
                key={e.key}
                checked={selected.includes(e.key) || linked.has(e.key)}
                disabled={linked.has(e.key)}
                onChange={() => toggle(e.key)}
                label={
                  <Text size="sm">
                    <b>{e.key}</b> · {e.summary}{' '}
                    <Text span size="xs" c="dimmed">
                      {e.status}
                      {linked.has(e.key) ? ' · уже в проекте' : e.imported ? ' · уже импортирован' : ''}
                    </Text>
                  </Text>
                }
              />
            ))}
            {epics.data?.length === 0 && <Text c="dimmed" size="sm">Эпики не найдены.</Text>}
          </Stack>
        </ScrollArea.Autosize>
        <Group justify="space-between">
          <Text size="sm" c="dimmed">
            Выбрано: {selected.length}
          </Text>
          <Group>
            <Button variant="default" onClick={onClose}>
              Отмена
            </Button>
            <Button onClick={run} loading={attach.isPending} disabled={selected.length === 0}>
              Импортировать
            </Button>
          </Group>
        </Group>
        {attach.isPending && <Text size="sm" c="dimmed">Загружаем задачи эпиков из Jira…</Text>}
      </Stack>
    </Modal>
  )
}

function AttachIssuesModal({ opened, onClose, projectId }: { opened: boolean; onClose(): void; projectId: string }) {
  const [text, setText] = useState('')
  const attach = useAttachIssues(projectId)
  const run = async () => {
    const keys = text.split(/[\s,;]+/).filter(Boolean)
    try {
      const r = await attach.mutateAsync(keys)
      notifications.show({
        color: r.notFound.length ? 'yellow' : 'green',
        message: `Привязано: ${r.attached}.${r.notFound.length ? ` Не найдены среди импортированных: ${r.notFound.join(', ')}` : ''}`,
      })
      setText('')
      onClose()
    } catch (e) {
      notifications.show({ color: 'red', message: describeError(e) })
    }
  }
  return (
    <Modal opened={opened} onClose={onClose} title="Привязать задачи по ключам">
      <Stack gap="sm">
        <Textarea
          label="Ключи задач"
          description="Через пробел, запятую или с новой строки. Задачи должны быть уже импортированы (из любого эпика)."
          placeholder="PRJ-12, PRJ-15"
          autosize
          minRows={3}
          value={text}
          onChange={(e) => setText(e.currentTarget.value)}
        />
        <Group justify="flex-end">
          <Button onClick={run} loading={attach.isPending} disabled={!text.trim()}>
            Привязать
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}

function EditProjectModal({ opened, onClose, project }: { opened: boolean; onClose(): void; project: ProjectDto }) {
  const update = useUpdateProject(project.id)
  const [name, setName] = useState(project.name)
  const [description, setDescription] = useState(project.description ?? '')
  const run = () =>
    update.mutate(
      { name, description, connectionId: project.connectionId },
      { onSuccess: onClose, onError: (e) => notifications.show({ color: 'red', message: describeError(e) }) },
    )
  return (
    <Modal opened={opened} onClose={onClose} title="Проект">
      <Stack gap="sm">
        <TextInput label="Название" value={name} onChange={(e) => setName(e.currentTarget.value)} />
        <Textarea label="Описание" autosize minRows={2} value={description} onChange={(e) => setDescription(e.currentTarget.value)} />
        <Group justify="flex-end">
          <Button onClick={run} loading={update.isPending} disabled={!name.trim()}>
            Сохранить
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
