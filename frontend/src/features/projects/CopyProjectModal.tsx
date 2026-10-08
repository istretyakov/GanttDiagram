import { Alert, Button, Checkbox, Group, Modal, Stack, Textarea, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconInfoCircle } from '@tabler/icons-react'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { describeError } from '../../api/client'
import { useCopyProject } from '../../api/hooks'

type Source = { id: string; name: string; description: string | null }

/** Новый проект на основе существующего: эпики, задачи и участники (с порядком строк) — по выбору. */
export function CopyProjectModal({ source, onClose }: { source: Source | null; onClose(): void }) {
  return (
    <Modal opened={source !== null} onClose={onClose} title="Копировать проект">
      {source && <CopyProjectForm key={source.id} source={source} onClose={onClose} />}
    </Modal>
  )
}

function CopyProjectForm({ source, onClose }: { source: Source; onClose(): void }) {
  const copy = useCopyProject()
  const navigate = useNavigate()
  const [name, setName] = useState(`${source.name} (копия)`)
  const [description, setDescription] = useState(source.description ?? '')
  const [includeEpics, setIncludeEpics] = useState(true)
  const [includeIssues, setIncludeIssues] = useState(true)
  const [includeMembers, setIncludeMembers] = useState(true)

  const run = async () => {
    try {
      const project = await copy.mutateAsync({
        sourceId: source.id,
        request: { name, description, includeEpics, includeIssues, includeMembers },
      })
      notifications.show({ color: 'green', message: `Создан проект «${project.name}».` })
      onClose()
      navigate(`/projects/${project.id}`)
    } catch (e) {
      notifications.show({ color: 'red', title: 'Не удалось скопировать проект', message: describeError(e) })
    }
  }

  return (
    <Stack gap="sm">
      <TextInput label="Название" value={name} onChange={(e) => setName(e.currentTarget.value)} data-autofocus />
      <Textarea label="Описание" autosize minRows={2} value={description} onChange={(e) => setDescription(e.currentTarget.value)} />
      <Checkbox label="Эпики (для пересинхронизации)" checked={includeEpics} onChange={(e) => setIncludeEpics(e.currentTarget.checked)} />
      <Checkbox label="Задачи" checked={includeIssues} onChange={(e) => setIncludeIssues(e.currentTarget.checked)} />
      <Checkbox label="Участники и порядок строк" checked={includeMembers} onChange={(e) => setIncludeMembers(e.currentTarget.checked)} />
      {includeIssues && (
        <Alert color="blue" p="xs" icon={<IconInfoCircle size={16} />}>
          Задачи Jira общие для всех проектов: плановые даты и исполнитель задачи одни и те же в оригинале и в копии.
        </Alert>
      )}
      <Group justify="flex-end">
        <Button variant="default" onClick={onClose}>
          Отмена
        </Button>
        <Button onClick={() => void run()} loading={copy.isPending} disabled={!name.trim()}>
          Копировать
        </Button>
      </Group>
    </Stack>
  )
}
