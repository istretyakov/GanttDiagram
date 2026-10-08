import { Alert, Button, Card, Group, Loader, Modal, Select, SimpleGrid, Stack, Text, Textarea, TextInput, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useQueryClient } from '@tanstack/react-query'
import { IconChartBar, IconCopy, IconPlus, IconSparkles } from '@tabler/icons-react'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { ApiError, describeError } from '../api/client'
import { seedDemo, useConnections, useCreateProject, useProjects } from '../api/hooks'
import type { ProjectSummaryDto } from '../api/types'
import { CopyProjectModal } from '../features/projects/CopyProjectModal'

export function ProjectsPage() {
  const projects = useProjects()
  const connections = useConnections()
  const [creating, setCreating] = useState(false)
  const [copySource, setCopySource] = useState<ProjectSummaryDto | null>(null)
  const [seeding, setSeeding] = useState(false)
  const qc = useQueryClient()
  const navigate = useNavigate()

  const runSeed = async () => {
    setSeeding(true)
    try {
      const { projectId } = await seedDemo()
      await qc.invalidateQueries()
      navigate(`/projects/${projectId}/gantt`)
    } catch (e) {
      notifications.show({ color: 'red', title: 'Не удалось создать демо-данные', message: describeError(e) })
    } finally {
      setSeeding(false)
    }
  }

  return (
    <Stack p="md" gap="md">
      <Group justify="space-between">
        <Title order={2}>Проекты</Title>
        <Group>
          {import.meta.env.DEV && (
            <Button variant="subtle" leftSection={<IconSparkles size={16} />} onClick={runSeed} loading={seeding}>
              Демо-данные
            </Button>
          )}
          <Button leftSection={<IconPlus size={16} />} onClick={() => setCreating(true)} disabled={!connections.data?.length}>
            Проект
          </Button>
        </Group>
      </Group>

      {connections.data?.length === 0 && (
        <Alert color="blue">
          Сначала <Link to="/connections">настройте подключение к Jira</Link>
          {import.meta.env.DEV && ' или создайте демо-данные, чтобы попробовать диаграмму без Jira'}.
        </Alert>
      )}
      {projects.isPending && <Loader />}
      {projects.isError && <Alert color="red">{describeError(projects.error)}</Alert>}
      {projects.data?.length === 0 && connections.data?.length !== 0 && <Text c="dimmed">Проектов пока нет.</Text>}

      <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}>
        {projects.data?.map((p) => (
          <Card key={p.id} withBorder padding="md">
            <Stack gap={6}>
              <Text fw={600} component={Link} to={`/projects/${p.id}`}>
                {p.name}
              </Text>
              {p.description && (
                <Text size="sm" c="dimmed" lineClamp={2}>
                  {p.description}
                </Text>
              )}
              <Text size="xs" c="dimmed">
                {p.connectionName} · эпиков {p.epicCount} · задач {p.issueCount} · участников {p.memberCount}
              </Text>
              <Group gap="xs" mt={4}>
                <Button size="xs" component={Link} to={`/projects/${p.id}/gantt`} leftSection={<IconChartBar size={14} />}>
                  Диаграмма
                </Button>
                <Button size="xs" variant="default" component={Link} to={`/projects/${p.id}`}>
                  Задачи и эпики
                </Button>
                <Button size="xs" variant="subtle" leftSection={<IconCopy size={14} />} onClick={() => setCopySource(p)}>
                  Копировать
                </Button>
              </Group>
            </Stack>
          </Card>
        ))}
      </SimpleGrid>

      <CreateProjectModal
        opened={creating}
        onClose={() => setCreating(false)}
        onCopyFrom={(project) => {
          setCreating(false)
          setCopySource(project)
        }}
      />
      <CopyProjectModal source={copySource} onClose={() => setCopySource(null)} />
    </Stack>
  )
}

function CreateProjectModal({
  opened,
  onClose,
  onCopyFrom,
}: {
  opened: boolean
  onClose(): void
  onCopyFrom(project: ProjectSummaryDto): void
}) {
  const connections = useConnections()
  const projects = useProjects()
  const create = useCreateProject()
  const navigate = useNavigate()
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [connectionId, setConnectionId] = useState<string | null>(null)
  const [errors, setErrors] = useState<Record<string, string[]>>({})

  const connectionValue = connectionId ?? connections.data?.[0]?.id ?? null

  const submit = async () => {
    if (!connectionValue) return
    try {
      const project = await create.mutateAsync({ name, description, connectionId: connectionValue })
      onClose()
      setName('')
      setDescription('')
      navigate(`/projects/${project.id}`)
    } catch (e) {
      if (e instanceof ApiError && e.problem.errors) setErrors(e.problem.errors)
      else notifications.show({ color: 'red', message: describeError(e) })
    }
  }

  return (
    <Modal opened={opened} onClose={onClose} title="Новый проект">
      <Stack gap="sm">
        {(projects.data?.length ?? 0) > 0 && (
          <Select
            label="На основе проекта"
            description="Скопировать эпики, задачи и участников существующего проекта"
            placeholder="Пустой проект"
            clearable
            data={(projects.data ?? []).map((p) => ({ value: p.id, label: p.name }))}
            value={null}
            onChange={(id) => {
              const source = projects.data?.find((p) => p.id === id)
              if (source) onCopyFrom(source)
            }}
          />
        )}
        <TextInput label="Название" required value={name} onChange={(e) => setName(e.currentTarget.value)} error={errors.name?.join(' ')} data-autofocus />
        <Textarea label="Описание" autosize minRows={2} value={description} onChange={(e) => setDescription(e.currentTarget.value)} />
        <Select
          label="Подключение Jira"
          data={(connections.data ?? []).map((c) => ({ value: c.id, label: `${c.name} (${c.projectKeys.join(', ')})` }))}
          value={connectionValue}
          onChange={setConnectionId}
          allowDeselect={false}
        />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Отмена
          </Button>
          <Button onClick={submit} loading={create.isPending} disabled={!name.trim()}>
            Создать
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
