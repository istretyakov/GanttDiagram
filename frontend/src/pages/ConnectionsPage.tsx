import { Alert, Badge, Button, Card, Group, Loader, Modal, Stack, Text, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconPencil, IconPlugConnected, IconPlus, IconRefresh, IconTrash } from '@tabler/icons-react'
import { useState } from 'react'
import { describeError } from '../api/client'
import {
  useConnections,
  useDeleteConnection,
  useSyncUsers,
  useTestConnection,
  useUsers,
} from '../api/hooks'
import type { ConnectionDto, ConnectionTestResult } from '../api/types'
import { ConnectionForm } from '../features/connections/ConnectionForm'

export function ConnectionsPage() {
  const connections = useConnections()
  const [editing, setEditing] = useState<ConnectionDto | 'new' | null>(null)

  return (
    <Stack p="md" gap="md" maw={980}>
      <Group justify="space-between">
        <Title order={2}>Подключения к Jira</Title>
        <Button leftSection={<IconPlus size={16} />} onClick={() => setEditing('new')}>
          Подключение
        </Button>
      </Group>
      <Text c="dimmed" size="sm">
        Jira Server / Data Center, только чтение. Для подключения достаточно адреса и Personal Access Token — проект и
        поля дат выбираются из списков Jira. Токен хранится в зашифрованном виде и не возвращается в интерфейс.
      </Text>

      {connections.isPending && <Loader />}
      {connections.isError && <Alert color="red">{describeError(connections.error)}</Alert>}
      {connections.data?.length === 0 && <Text c="dimmed">Подключений пока нет.</Text>}
      {connections.data?.map((c) => <ConnectionCard key={c.id} connection={c} onEdit={() => setEditing(c)} />)}

      <ConnectionForm editing={editing} onClose={() => setEditing(null)} />
    </Stack>
  )
}

function ConnectionCard({ connection, onEdit }: { connection: ConnectionDto; onEdit(): void }) {
  const test = useTestConnection()
  const syncUsers = useSyncUsers()
  const remove = useDeleteConnection()
  const users = useUsers(connection.id)
  const [result, setResult] = useState<ConnectionTestResult | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)

  const runTest = async () => {
    try {
      setResult(await test.mutateAsync(connection.id))
    } catch (e) {
      setResult({ ok: false, error: describeError(e), userName: null, displayName: null, serverVersion: null, serverTitle: null })
    }
  }

  const runSync = async () => {
    try {
      const r = await syncUsers.mutateAsync(connection.id)
      notifications.show({ color: 'green', message: `Пользователи: новых ${r.created}, обновлено ${r.updated}, всего ${r.total}.` })
    } catch (e) {
      notifications.show({ color: 'red', title: 'Не удалось обновить пользователей', message: describeError(e) })
    }
  }

  return (
    <Card withBorder>
      <Group justify="space-between" align="flex-start">
        <Stack gap={4}>
          <Group gap="xs">
            <Text fw={600}>{connection.name}</Text>
            {connection.projectKeys.map((key) => (
              <Badge key={key} variant="light">
                {key}
              </Badge>
            ))}
          </Group>
          <Text size="sm" c="dimmed">
            {connection.baseUrl}
          </Text>
          <Text size="xs" c="dimmed">
            Начало: {connection.startDateFieldId ?? 'не задано'} · Окончание: {connection.endDateFieldId} ·{' '}
            {connection.hoursPerDay} ч в дне · пользователей: {users.data?.length ?? '…'}
          </Text>
        </Stack>
        <Group gap="xs">
          <Button size="xs" variant="default" leftSection={<IconPlugConnected size={14} />} onClick={runTest} loading={test.isPending}>
            Проверить
          </Button>
          <Button size="xs" variant="default" leftSection={<IconRefresh size={14} />} onClick={runSync} loading={syncUsers.isPending}>
            Обновить пользователей
          </Button>
          <Button size="xs" variant="default" leftSection={<IconPencil size={14} />} onClick={onEdit}>
            Изменить
          </Button>
          <Button size="xs" variant="subtle" color="red" leftSection={<IconTrash size={14} />} onClick={() => setConfirmDelete(true)}>
            Удалить
          </Button>
        </Group>
      </Group>
      {!connection.startDateFieldId && (
        <Alert color="yellow" mt="sm" p="xs">
          Не выбрано поле «ожидаемое начало» — в Jira нет стандартного поля. Откройте «Изменить» и сопоставьте поля.
        </Alert>
      )}
      {result && (
        <Alert mt="sm" p="xs" color={result.ok ? 'green' : 'red'} withCloseButton onClose={() => setResult(null)}>
          {result.ok
            ? `Подключено как ${result.displayName} (${result.userName}). Jira ${result.serverVersion}${result.serverTitle ? ` · ${result.serverTitle}` : ''}.`
            : result.error}
        </Alert>
      )}

      <Modal opened={confirmDelete} onClose={() => setConfirmDelete(false)} title="Удалить подключение?">
        <Text size="sm" mb="md">
          Будут удалены все импортированные задачи, пользователи и проекты этого подключения. Jira не изменится.
        </Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={() => setConfirmDelete(false)}>
            Отмена
          </Button>
          <Button color="red" loading={remove.isPending} onClick={() => remove.mutate(connection.id, { onSuccess: () => setConfirmDelete(false) })}>
            Удалить
          </Button>
        </Group>
      </Modal>
    </Card>
  )
}
