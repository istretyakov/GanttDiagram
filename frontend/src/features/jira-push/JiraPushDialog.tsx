import {
  Alert,
  Anchor,
  Badge,
  Button,
  Checkbox,
  Group,
  Loader,
  Modal,
  ScrollArea,
  Stack,
  Switch,
  Table,
  Text,
} from '@mantine/core'
import { IconAlertTriangle, IconCloudUpload } from '@tabler/icons-react'
import { useState } from 'react'
import { describeError } from '../../api/client'
import { useJiraPush, useJiraPushPreview } from '../../api/hooks'
import type { JiraPushItem, JiraPushResult } from '../../api/types'
import { formatIso } from '../../shared/dateMath'

type JiraPushDialogProps = {
  projectId: string
  opened: boolean
  onClose(): void
  /** Несохранённые правки диаграммы: в Jira уходят только сохранённые значения. */
  dirtyCount?: number
  onSave?(): void
  saving?: boolean
}

const FIELD_LABEL: Record<string, string> = { start: 'Начало', end: 'Окончание', assignee: 'Исполнитель' }

/**
 * Отправка плановых сроков в Jira: предпросмотр изменений, выбор задач, результат по каждой задаче.
 * Задачи, изменённые в Jira после последней синхронизации, не записываются (конфликт).
 */
export function JiraPushDialog({ projectId, opened, onClose, dirtyCount = 0, onSave, saving }: JiraPushDialogProps) {
  return (
    <Modal opened={opened} onClose={onClose} title="Отправить сроки в Jira" size="xl">
      {opened && (
        <JiraPushBody projectId={projectId} onClose={onClose} dirtyCount={dirtyCount} onSave={onSave} saving={saving} />
      )}
    </Modal>
  )
}

function JiraPushBody({ projectId, onClose, dirtyCount, onSave, saving }: Omit<JiraPushDialogProps, 'opened'>) {
  const [includeAssignee, setIncludeAssignee] = useState(false)
  const [excluded, setExcluded] = useState<Set<string>>(new Set())
  const [result, setResult] = useState<JiraPushResult | null>(null)
  const [confirming, setConfirming] = useState(false)
  const preview = useJiraPushPreview(projectId, includeAssignee, !result)
  const push = useJiraPush(projectId)

  const items = preview.data?.items ?? []
  const selected = items.filter((i) => !excluded.has(i.issueId))

  const toggle = (id: string) =>
    setExcluded((s) => {
      const next = new Set(s)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const run = async (issueIds: string[]) => {
    setConfirming(false)
    try {
      setResult(await push.mutateAsync({ issueIds, includeAssignee }))
    } catch {
      // Ошибка показывается из push.error; предпросмотр мог устареть.
      void preview.refetch()
    }
  }

  if (result) {
    const failedIds = result.items.filter((i) => i.status !== 'synced').map((i) => i.issueId)
    return (
      <Stack gap="sm">
        <Group gap="xs">
          <Badge color="green" size="lg" variant="light">Записано: {result.synced}</Badge>
          <Badge color="orange" size="lg" variant="light">Конфликтов: {result.conflicts}</Badge>
          <Badge color="red" size="lg" variant="light">Ошибок: {result.failed}</Badge>
        </Group>
        <ScrollArea.Autosize mah={420}>
          <Table verticalSpacing={4} fz="sm">
            <Table.Tbody>
              {result.items.map((i) => (
                <Table.Tr key={i.issueId}>
                  <Table.Td w={110}>{i.key}</Table.Td>
                  <Table.Td w={130}>
                    <Badge color={i.status === 'synced' ? 'green' : i.status === 'conflict' ? 'orange' : 'red'} variant="light">
                      {i.status === 'synced' ? 'записано' : i.status === 'conflict' ? 'конфликт' : 'ошибка'}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Text size="xs">{i.message}</Text>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea.Autosize>
        {result.conflicts > 0 && (
          <Text size="xs" c="dimmed">
            Конфликт: задачу изменили в Jira. Снимок Jira обновлён, план сохранён — проверьте значок ≠ на диаграмме и
            отправьте снова.
          </Text>
        )}
        <Group justify="flex-end">
          {failedIds.length > 0 && (
            <Button
              variant="default"
              onClick={() => {
                setResult(null)
                setExcluded(new Set(items.filter((i) => !failedIds.includes(i.issueId)).map((i) => i.issueId)))
              }}
            >
              Повторить для неотправленных
            </Button>
          )}
          <Button onClick={onClose}>Готово</Button>
        </Group>
      </Stack>
    )
  }

  return (
    <Stack gap="sm">
      {dirtyCount! > 0 && (
        <Alert color="yellow" icon={<IconAlertTriangle size={18} />} p="xs">
          <Group justify="space-between" wrap="nowrap">
            <Text size="sm">
              Есть несохранённые правки ({dirtyCount}). В Jira отправляются только сохранённые значения.
            </Text>
            {onSave && (
              <Button size="xs" onClick={onSave} loading={saving}>
                Сохранить
              </Button>
            )}
          </Group>
        </Alert>
      )}
      <Text size="sm" c="dimmed">
        Плановые даты, которые отличаются от Jira, будут записаны в задачи. Перед записью каждая задача перечитывается:
        если её изменили в Jira после последней синхронизации, она пропускается.
      </Text>
      <Switch
        label="Также переназначить исполнителя"
        checked={includeAssignee}
        onChange={(e) => setIncludeAssignee(e.currentTarget.checked)}
      />
      {preview.data?.warnings.map((w) => (
        <Alert key={w} color="yellow" p="xs">
          {w}
        </Alert>
      ))}
      {preview.isPending && <Loader size="sm" />}
      {preview.isError && <Alert color="red">{describeError(preview.error)}</Alert>}
      {push.isError && <Alert color="red">{describeError(push.error)}</Alert>}
      {preview.data && items.length === 0 && (
        <Alert color="green" p="xs">
          Все плановые значения совпадают с Jira — отправлять нечего.
        </Alert>
      )}

      {items.length > 0 && (
        <ScrollArea.Autosize mah={400}>
          <Table verticalSpacing={4} fz="sm" data-testid="jira-push-preview">
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={36}>
                  <Checkbox
                    checked={selected.length === items.length}
                    indeterminate={selected.length > 0 && selected.length < items.length}
                    onChange={() => setExcluded(selected.length === items.length ? new Set(items.map((i) => i.issueId)) : new Set())}
                    aria-label="Выбрать все"
                  />
                </Table.Th>
                <Table.Th>Задача</Table.Th>
                <Table.Th>Изменения: Jira → план</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {items.map((item) => (
                <PreviewRow key={item.issueId} item={item} checked={!excluded.has(item.issueId)} onToggle={() => toggle(item.issueId)} />
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea.Autosize>
      )}

      <Group justify="flex-end">
        <Button variant="default" onClick={onClose}>
          Отмена
        </Button>
        {confirming ? (
          <Group gap="xs">
            <Text size="sm">Записать изменения в {selected.length} задач(и) Jira?</Text>
            <Button variant="default" onClick={() => setConfirming(false)}>
              Нет
            </Button>
            <Button color="orange" onClick={() => void run(selected.map((i) => i.issueId))} loading={push.isPending}>
              Да, записать
            </Button>
          </Group>
        ) : (
          <Button
            leftSection={<IconCloudUpload size={16} />}
            disabled={selected.length === 0 || dirtyCount! > 0}
            onClick={() => setConfirming(true)}
            loading={push.isPending}
          >
            Отправить в Jira ({selected.length})
          </Button>
        )}
      </Group>
    </Stack>
  )
}

function PreviewRow({ item, checked, onToggle }: { item: JiraPushItem; checked: boolean; onToggle(): void }) {
  return (
    <Table.Tr>
      <Table.Td>
        <Checkbox checked={checked} onChange={onToggle} aria-label={`Отправить ${item.key}`} />
      </Table.Td>
      <Table.Td>
        <Anchor href={item.jiraUrl} target="_blank" rel="noreferrer" size="sm" fw={600}>
          {item.key}
        </Anchor>
        <Text size="xs" c="dimmed" lineClamp={1}>
          {item.summary}
        </Text>
      </Table.Td>
      <Table.Td>
        {item.changes.map((c) => (
          <Text key={c.field} size="xs">
            {FIELD_LABEL[c.field] ?? c.field}: {show(c.field, c.from)} → <b>{show(c.field, c.to)}</b>
          </Text>
        ))}
      </Table.Td>
    </Table.Tr>
  )
}

function show(field: string, value: string | null) {
  if (!value) return field === 'assignee' ? 'без исполнителя' : '—'
  return field === 'assignee' ? value : formatIso(value)
}
