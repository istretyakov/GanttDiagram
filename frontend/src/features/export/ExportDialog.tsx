import {
  Alert,
  Button,
  Checkbox,
  CopyButton,
  Group,
  Modal,
  SegmentedControl,
  Select,
  Stack,
  Tabs,
  Text,
  TextInput,
  Textarea,
} from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { IconAlertTriangle, IconCopy, IconDownload } from '@tabler/icons-react'
import { useState } from 'react'
import { describeError } from '../../api/client'
import type { ExportFormat, ExportGroupBy, ExportLabel, ExportParams, ExportResult } from '../../api/types'
import { storage } from '../../shared/format'
import { MermaidPreview } from './MermaidPreview'
import { PlantUmlPreview, PLANTUML_SERVER_KEY } from './PlantUmlPreview'

type ExportDialogProps = {
  opened: boolean
  onClose(): void
  /** Ключ для кеша запроса: проект или исполнитель. */
  cacheKey: string
  load(params: ExportParams): Promise<ExportResult>
  /** Группировка по участникам/эпикам — только для проекта (нагрузка группируется по проектам). */
  withGroupBy?: boolean
  /** Есть несохранённые правки: экспорт идёт из сохранённых данных. */
  dirtyCount?: number
  onSave?(): void
  saving?: boolean
}

export function ExportDialog(props: ExportDialogProps) {
  const { opened, onClose, cacheKey, load, withGroupBy, dirtyCount = 0, onSave, saving } = props
  const [format, setFormat] = useState<ExportFormat>(() => (storage.get('export.format') as ExportFormat) ?? 'plantuml')
  const [groupBy, setGroupBy] = useState<ExportGroupBy>('member')
  const [label, setLabel] = useState<ExportLabel>('keySummary')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [excludeWeekends, setExcludeWeekends] = useState(true)
  const [markToday, setMarkToday] = useState(true)
  const [plantUmlServer, setPlantUmlServer] = useState(() => storage.get(PLANTUML_SERVER_KEY) ?? '')

  const params: ExportParams = {
    format,
    groupBy: withGroupBy ? groupBy : undefined,
    label,
    from: from || undefined,
    to: to || undefined,
    excludeWeekends,
    markToday,
  }

  const query = useQuery({
    queryKey: ['export', cacheKey, params],
    queryFn: () => load(params),
    enabled: opened,
    staleTime: 0,
  })

  const changeFormat = (value: string) => {
    setFormat(value as ExportFormat)
    storage.set('export.format', value)
  }

  const download = () => {
    if (!query.data) return
    const blob = new Blob([query.data.content], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = query.data.fileName
    a.click()
    URL.revokeObjectURL(url)
  }

  return (
    <Modal opened={opened} onClose={onClose} title="Экспорт диаграммы" size="xl">
      <Stack gap="sm">
        {dirtyCount > 0 && (
          <Alert color="yellow" icon={<IconAlertTriangle size={18} />} p="xs">
            <Group justify="space-between" wrap="nowrap">
              <Text size="sm">
                Есть несохранённые правки ({dirtyCount}). Экспорт строится из сохранённых данных.
              </Text>
              {onSave && (
                <Button size="xs" onClick={onSave} loading={saving}>
                  Сохранить
                </Button>
              )}
            </Group>
          </Alert>
        )}

        <Group align="flex-end" gap="sm">
          <SegmentedControl
            value={format}
            onChange={changeFormat}
            data={[
              { value: 'plantuml', label: 'PlantUML' },
              { value: 'mermaid', label: 'Mermaid' },
            ]}
          />
          {withGroupBy && (
            <Select
              label="Группировка"
              w={170}
              value={groupBy}
              onChange={(v) => v && setGroupBy(v as ExportGroupBy)}
              allowDeselect={false}
              data={[
                { value: 'member', label: 'По участникам' },
                { value: 'epic', label: 'По эпикам' },
              ]}
            />
          )}
          <Select
            label="Подпись задачи"
            w={250}
            value={label}
            onChange={(v) => v && setLabel(v as ExportLabel)}
            allowDeselect={false}
            data={[
              { value: 'key', label: 'KEY' },
              { value: 'keySummary', label: 'KEY · название' },
              { value: 'keySummaryAssignee', label: 'KEY · название (исполнитель)' },
            ]}
          />
          <TextInput label="С" type="date" w={150} value={from} onChange={(e) => setFrom(e.currentTarget.value)} />
          <TextInput label="По" type="date" w={150} value={to} onChange={(e) => setTo(e.currentTarget.value)} />
        </Group>
        <Group gap="lg">
          <Checkbox label="Исключать выходные" checked={excludeWeekends} onChange={(e) => setExcludeWeekends(e.currentTarget.checked)} />
          <Checkbox label="Отмечать сегодня" checked={markToday} onChange={(e) => setMarkToday(e.currentTarget.checked)} />
        </Group>

        {query.isError && <Alert color="red">{describeError(query.error)}</Alert>}

        <Tabs defaultValue="text" keepMounted={false}>
          <Tabs.List>
            <Tabs.Tab value="text">Текст</Tabs.Tab>
            <Tabs.Tab value="preview">Предпросмотр</Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="text" pt="xs">
            <Textarea
              readOnly
              autosize
              minRows={12}
              maxRows={22}
              value={query.data?.content ?? (query.isFetching ? 'Загрузка…' : '')}
              styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)', fontSize: 12 } }}
              data-testid="export-text"
            />
          </Tabs.Panel>
          <Tabs.Panel value="preview" pt="xs">
            {query.data &&
              (format === 'mermaid' ? (
                <MermaidPreview source={query.data.content} />
              ) : (
                <Stack gap="xs">
                  <TextInput
                    size="xs"
                    label="PlantUML-сервер компании"
                    description="Публичный plantuml.com не используется: данные задач не должны уходить наружу."
                    placeholder="https://plantuml.company.local"
                    value={plantUmlServer}
                    onChange={(e) => {
                      setPlantUmlServer(e.currentTarget.value)
                      storage.set(PLANTUML_SERVER_KEY, e.currentTarget.value.trim())
                    }}
                  />
                  <PlantUmlPreview source={query.data.content} server={plantUmlServer.trim()} />
                </Stack>
              ))}
          </Tabs.Panel>
        </Tabs>

        <Group justify="flex-end">
          <CopyButton value={query.data?.content ?? ''}>
            {({ copied, copy }) => (
              <Button variant="default" leftSection={<IconCopy size={16} />} onClick={copy} disabled={!query.data}>
                {copied ? 'Скопировано' : 'Копировать'}
              </Button>
            )}
          </CopyButton>
          <Button leftSection={<IconDownload size={16} />} onClick={download} disabled={!query.data}>
            Скачать {query.data ? `(${query.data.fileName})` : ''}
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
