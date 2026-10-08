import { AppShell, Group, NavLink as MantineNavLink, Text } from '@mantine/core'
import { IconChartBar, IconFolders, IconPlug, IconTimeline } from '@tabler/icons-react'
import { NavLink, Outlet, useLocation } from 'react-router'

const nav = [
  { to: '/projects', label: 'Проекты', icon: IconFolders },
  { to: '/workload', label: 'Нагрузка', icon: IconChartBar },
  { to: '/connections', label: 'Подключения Jira', icon: IconPlug },
]

export function Layout() {
  const location = useLocation()
  return (
    <AppShell header={{ height: 52 }} padding={0}>
      <AppShell.Header>
        <Group h="100%" px="md" gap="lg" wrap="nowrap">
          <Group gap={6} wrap="nowrap">
            <IconTimeline size={22} color="var(--mantine-color-blue-filled)" />
            <Text fw={700}>Gantt по Jira</Text>
          </Group>
          <Group gap={4} wrap="nowrap">
            {nav.map((item) => (
              <MantineNavLink
                key={item.to}
                component={NavLink}
                to={item.to}
                label={item.label}
                leftSection={<item.icon size={16} />}
                active={location.pathname.startsWith(item.to)}
                style={{ borderRadius: 6, width: 'auto' }}
              />
            ))}
          </Group>
        </Group>
      </AppShell.Header>
      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
    </AppShell>
  )
}
