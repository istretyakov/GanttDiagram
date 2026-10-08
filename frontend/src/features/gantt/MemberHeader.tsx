import { ActionIcon, Avatar, Group, Menu, Stack, Text, Tooltip } from '@mantine/core'
import { IconArrowDown, IconArrowUp, IconChartBar, IconDots, IconInfoCircle, IconTrash } from '@tabler/icons-react'
import { Link } from 'react-router'
import type { MemberDto } from '../../api/types'
import { initials } from '../../shared/format'

type MemberHeaderProps = {
  member: MemberDto
  taskCount: number
  hours: number
  isFirst: boolean
  isLast: boolean
  onMove(direction: -1 | 1): void
  onRemove(): void
}

export function MemberHeader({ member, taskCount, hours, isFirst, isLast, onMove, onRemove }: MemberHeaderProps) {
  return (
    <Group gap={8} wrap="nowrap" w="100%" align="flex-start">
      <Avatar src={member.avatarUrl} size={28} radius="xl" color="blue">
        {initials(member.displayName)}
      </Avatar>
      <Stack gap={0} style={{ minWidth: 0, flex: 1 }}>
        <Text size="sm" fw={500} truncate title={member.displayName}>
          {member.displayName}
        </Text>
        <Text size="xs" c="dimmed" truncate>
          {taskCount} зад. · {Math.round(hours)} ч
        </Text>
      </Stack>
      <Tooltip label="Нагрузка по всем проектам">
        <ActionIcon component={Link} to={`/workload/${member.userId}`} variant="subtle" size="sm" aria-label="Нагрузка">
          <IconInfoCircle size={16} />
        </ActionIcon>
      </Tooltip>
      <Menu position="bottom-end" withinPortal>
        <Menu.Target>
          <ActionIcon variant="subtle" size="sm" color="gray" aria-label="Действия с участником">
            <IconDots size={16} />
          </ActionIcon>
        </Menu.Target>
        <Menu.Dropdown>
          <Menu.Item leftSection={<IconChartBar size={14} />} component={Link} to={`/workload/${member.userId}`}>
            Нагрузка
          </Menu.Item>
          <Menu.Item leftSection={<IconArrowUp size={14} />} disabled={isFirst} onClick={() => onMove(-1)}>
            Выше
          </Menu.Item>
          <Menu.Item leftSection={<IconArrowDown size={14} />} disabled={isLast} onClick={() => onMove(1)}>
            Ниже
          </Menu.Item>
          <Menu.Divider />
          <Menu.Item
            leftSection={<IconTrash size={14} />}
            color="red"
            disabled={taskCount > 0}
            onClick={onRemove}
          >
            {taskCount > 0 ? 'Убрать (сначала переназначьте задачи)' : 'Убрать из проекта'}
          </Menu.Item>
        </Menu.Dropdown>
      </Menu>
    </Group>
  )
}
