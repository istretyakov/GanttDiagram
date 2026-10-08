import { Button, Group, Popover, Select, Stack, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconPlus, IconRefresh } from '@tabler/icons-react'
import { useState } from 'react'
import { describeError } from '../../api/client'
import { useAddMember, useSyncUsers, useUsers } from '../../api/hooks'

type AddMemberButtonProps = {
  projectId: string
  connectionId: string
  memberIds: ReadonlySet<string>
}

/** Кнопка «+»: участник из уже синхронизированных пользователей Jira. */
export function AddMemberButton({ projectId, connectionId, memberIds }: AddMemberButtonProps) {
  const [opened, setOpened] = useState(false)
  const users = useUsers(connectionId)
  const addMember = useAddMember(projectId)
  const syncUsers = useSyncUsers()

  const options = (users.data ?? [])
    .filter((u) => !memberIds.has(u.id))
    .map((u) => ({ value: u.id, label: `${u.displayName} (${u.name})` }))

  const add = async (userId: string | null) => {
    if (!userId) return
    try {
      await addMember.mutateAsync(userId)
      setOpened(false)
    } catch (e) {
      notifications.show({ color: 'red', title: 'Не удалось добавить участника', message: describeError(e) })
    }
  }

  const sync = async () => {
    try {
      const result = await syncUsers.mutateAsync(connectionId)
      notifications.show({ message: `Пользователи обновлены: новых ${result.created}, всего ${result.total}.` })
    } catch (e) {
      notifications.show({ color: 'red', title: 'Не удалось обновить пользователей', message: describeError(e) })
    }
  }

  return (
    <Popover opened={opened} onChange={setOpened} position="right-start" width={320} withinPortal trapFocus>
      <Popover.Target>
        <Button
          variant="light"
          size="xs"
          leftSection={<IconPlus size={14} />}
          onClick={() => setOpened((o) => !o)}
          data-testid="add-member"
        >
          Участник
        </Button>
      </Popover.Target>
      <Popover.Dropdown>
        <Stack gap="xs">
          <Select
            label="Добавить участника"
            placeholder="Поиск по имени или логину"
            searchable
            data={options}
            nothingFoundMessage="Никого не найдено"
            onChange={add}
            disabled={addMember.isPending}
            comboboxProps={{ withinPortal: false }}
            data-autofocus
          />
          <Group justify="space-between" wrap="nowrap">
            <Text size="xs" c="dimmed">
              Нет нужного человека?
            </Text>
            <Button size="compact-xs" variant="subtle" leftSection={<IconRefresh size={12} />} onClick={sync} loading={syncUsers.isPending}>
              Обновить пользователей
            </Button>
          </Group>
        </Stack>
      </Popover.Dropdown>
    </Popover>
  )
}
