import {
  Alert,
  Anchor,
  Button,
  Divider,
  Group,
  Modal,
  MultiSelect,
  NumberInput,
  PasswordInput,
  Select,
  Stack,
  TagsInput,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconAlertTriangle, IconCheck, IconPlugConnected } from '@tabler/icons-react'
import { useEffect, useRef, useState } from 'react'
import { ApiError, describeError } from '../../api/client'
import { useDiscoverJira, useSaveConnection } from '../../api/hooks'
import type { ConnectionDto, HierarchyLinkDto, JiraDiscoverResult, JiraLinkTypeDto } from '../../api/types'

type ConnectionFormProps = {
  /** null — закрыто, 'new' — новое подключение, иначе — редактирование. */
  editing: ConnectionDto | 'new' | null
  onClose(): void
}

export function ConnectionForm({ editing, onClose }: ConnectionFormProps) {
  const opened = editing !== null
  const existing = editing && editing !== 'new' ? editing : null
  return (
    <Modal opened={opened} onClose={onClose} title={existing ? 'Подключение к Jira' : 'Новое подключение к Jira'} size="lg">
      {opened && <ConnectionFormBody key={existing?.id ?? 'new'} existing={existing} onClose={onClose} />}
    </Modal>
  )
}

type Settings = {
  name: string
  projectKeys: string[]
  startDateFieldId: string | null
  endDateFieldId: string
  hoursPerDay: number
  /** Связи Jira, образующие иерархию задач: «родитель <связь> дочерняя». */
  hierarchyLinks: HierarchyLinkDto[]
}

/**
 * Шаг 1 — адрес и токен, «Подключиться»: Jira проверяется сразу, без сохранения.
 * Шаг 2 — проект и поля дат выбираются из того, что вернула Jira; название подставляется само.
 */
function ConnectionFormBody({ existing, onClose }: { existing: ConnectionDto | null; onClose(): void }) {
  const discover = useDiscoverJira()
  const save = useSaveConnection()

  const [baseUrl, setBaseUrl] = useState(existing?.baseUrl ?? '')
  const [token, setToken] = useState('')
  const [settings, setSettings] = useState<Settings>({
    name: existing?.name ?? '',
    projectKeys: existing?.projectKeys ?? [],
    startDateFieldId: existing?.startDateFieldId ?? null,
    endDateFieldId: existing?.endDateFieldId ?? 'duedate',
    hoursPerDay: existing?.hoursPerDay ?? 8,
    hierarchyLinks: existing?.hierarchyLinks ?? [],
  })
  const [hierarchyTouched, setHierarchyTouched] = useState(false)
  const [nameTouched, setNameTouched] = useState(!!existing)
  const [jira, setJira] = useState<JiraDiscoverResult | null>(null)
  const [connectedWith, setConnectedWith] = useState<string | null>(null)
  const [manual, setManual] = useState(false)
  const [errors, setErrors] = useState<Record<string, string[]>>({})

  const credentials = `${baseUrl.trim()}|${token.trim()}`
  const connected = jira?.ok === true
  const stale = connected && connectedWith !== credentials
  const showSettings = !!existing || connected || manual

  const set = <K extends keyof Settings>(key: K, value: Settings[K]) => setSettings((s) => ({ ...s, [key]: value }))

  const connect = async () => {
    setErrors({})
    try {
      const result = await discover.mutateAsync({
        baseUrl,
        token: token.trim() || null,
        connectionId: existing?.id ?? null,
      })
      setJira(result)
      if (!result.ok) return
      // Адрес приводится к адресу сервера (если вставили ссылку на задачу или доску).
      setBaseUrl(result.baseUrl)
      setConnectedWith(`${result.baseUrl}|${token.trim()}`)
      setSettings((s) => {
        const projectKeys =
          s.projectKeys.length > 0 ? s.projectKeys : result.suggestedProjectKey ? [result.suggestedProjectKey] : []
        return {
          ...s,
          projectKeys,
          // Новое подключение: подставляем предложенные поля; при редактировании — только если не были заданы.
          startDateFieldId: existing ? (s.startDateFieldId ?? result.suggestedStartFieldId) : result.suggestedStartFieldId,
          endDateFieldId: existing ? s.endDateFieldId : result.suggestedEndFieldId,
          // Иерархия: предложенные связи («включает», «tested by»), если ещё ничего не выбрано.
          hierarchyLinks:
            hierarchyTouched || s.hierarchyLinks.length > 0 ? s.hierarchyLinks : result.suggestedHierarchyLinks,
          name: nameTouched ? s.name : defaultName(result, projectKeys),
        }
      })
    } catch (e) {
      if (e instanceof ApiError && e.problem.errors) setErrors(e.problem.errors)
      else notifications.show({ color: 'red', message: describeError(e) })
    }
  }

  // При редактировании сразу подключаемся с сохранённым токеном, чтобы показать списки проектов и полей.
  const autoConnected = useRef(false)
  useEffect(() => {
    if (existing && !autoConnected.current) {
      autoConnected.current = true
      void connect()
    }
  })

  const chooseProjects = (keys: string[]) => {
    const projectKeys = [...new Set(keys.map((k) => k.trim().toUpperCase()).filter(Boolean))]
    setSettings((s) => ({ ...s, projectKeys, name: nameTouched || !jira ? s.name : defaultName(jira, projectKeys) }))
  }

  const submit = async () => {
    setErrors({})
    try {
      await save.mutateAsync({
        id: existing?.id,
        input: {
          name: settings.name.trim() || null,
          baseUrl,
          token: token.trim() || null,
          projectKeys: settings.projectKeys,
          startDateFieldId: settings.startDateFieldId,
          endDateFieldId: settings.endDateFieldId,
          hoursPerDay: settings.hoursPerDay,
          hierarchyLinks: settings.hierarchyLinks,
        },
      })
      notifications.show({ color: 'green', message: existing ? 'Подключение сохранено.' : 'Подключение создано.' })
      onClose()
    } catch (e) {
      if (e instanceof ApiError && e.problem.errors) setErrors(e.problem.errors)
      else notifications.show({ color: 'red', message: describeError(e) })
    }
  }

  const err = (key: string) => errors[key]?.join(' ')

  const projectOptions = (jira?.projects ?? []).map((p) => ({ value: p.key, label: `${p.key} — ${p.name}` }))
  const dateFieldOptions = (jira?.fields ?? [])
    .filter((f) => f.schemaType === 'date' || f.schemaType === 'datetime' || f.id === 'duedate')
    .map((f) => ({ value: f.id, label: `${f.name} (${f.id})` }))

  return (
    <Stack gap="sm">
      <TextInput
        label="Адрес Jira"
        description="Можно вставить ссылку на любую задачу, доску или проект — адрес сервера и проект определятся сами."
        placeholder="https://jira.company.local"
        required
        value={baseUrl}
        onChange={(e) => setBaseUrl(e.currentTarget.value)}
        error={err('baseUrl')}
        data-autofocus
      />
      <PasswordInput
        label="Personal Access Token"
        description={
          existing
            ? 'Оставьте пустым, чтобы использовать сохранённый токен.'
            : 'Jira → профиль → Personal Access Tokens → Create token (Jira 8.14+).'
        }
        required={!existing}
        value={token}
        onChange={(e) => setToken(e.currentTarget.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') void connect()
        }}
        error={err('token')}
        autoComplete="off"
      />
      <Group>
        <Button
          leftSection={<IconPlugConnected size={16} />}
          onClick={() => void connect()}
          loading={discover.isPending}
          disabled={!baseUrl.trim() || (!existing && !token.trim())}
          variant={connected && !stale ? 'light' : 'filled'}
        >
          {connected && !stale ? 'Подключиться заново' : 'Подключиться'}
        </Button>
        {!showSettings && (
          <Anchor component="button" size="sm" onClick={() => setManual(true)}>
            Заполнить вручную
          </Anchor>
        )}
      </Group>

      {jira && !jira.ok && (
        <Alert color="red" icon={<IconAlertTriangle size={18} />} p="xs">
          {jira.error}
        </Alert>
      )}
      {connected && (
        <Alert color={stale ? 'yellow' : 'green'} icon={<IconCheck size={18} />} p="xs">
          Подключено как <b>{jira.displayName}</b> ({jira.userName}) · {jira.serverTitle || 'Jira'} {jira.serverVersion} ·
          проектов: {jira.projects.length}
          {stale && '. Адрес или токен изменились — подключитесь заново.'}
        </Alert>
      )}
      {jira?.warnings.map((w) => (
        <Alert key={w} color="yellow" p="xs">
          {w}
        </Alert>
      ))}

      {showSettings && (
        <>
          <Divider mt="xs" />
          <Title order={5}>Проект и поля</Title>
          {projectOptions.length > 0 ? (
            <MultiSelect
              label="Проекты Jira"
              description="Один или несколько: эпики и пользователи берутся из всех выбранных проектов"
              placeholder={settings.projectKeys.length ? undefined : 'Выберите проекты'}
              required
              searchable
              clearable
              data={withSelected(projectOptions, settings.projectKeys)}
              value={settings.projectKeys}
              onChange={chooseProjects}
              nothingFoundMessage="Проект не найден"
              error={err('projectKeys')}
            />
          ) : (
            <TagsInput
              label="Ключи проектов Jira"
              description="Введите ключ и нажмите Enter; можно несколько"
              placeholder="PRJ"
              required
              value={settings.projectKeys}
              onChange={chooseProjects}
              splitChars={[',', ' ', ';']}
              error={err('projectKeys')}
            />
          )}
          <Group grow align="flex-start">
            {dateFieldOptions.length > 0 ? (
              <>
                <Select
                  label="Ожидаемое начало"
                  description="Стандартного поля нет: Start date, Target start или своё"
                  searchable
                  clearable
                  data={dateFieldOptions}
                  value={settings.startDateFieldId}
                  onChange={(v) => set('startDateFieldId', v)}
                />
                <Select
                  label="Ожидаемое окончание"
                  description="Обычно Due date или Target end"
                  searchable
                  allowDeselect={false}
                  data={dateFieldOptions}
                  value={settings.endDateFieldId}
                  onChange={(v) => set('endDateFieldId', v ?? 'duedate')}
                />
              </>
            ) : (
              <>
                <TextInput
                  label="Ожидаемое начало"
                  placeholder="customfield_10015"
                  value={settings.startDateFieldId ?? ''}
                  onChange={(e) => set('startDateFieldId', e.currentTarget.value || null)}
                />
                <TextInput
                  label="Ожидаемое окончание"
                  placeholder="duedate"
                  value={settings.endDateFieldId}
                  onChange={(e) => set('endDateFieldId', e.currentTarget.value || 'duedate')}
                />
              </>
            )}
          </Group>
          <Group grow align="flex-start">
            <TextInput
              label="Название"
              description="Как подключение будет называться в приложении"
              value={settings.name}
              onChange={(e) => {
                setNameTouched(true)
                set('name', e.currentTarget.value)
              }}
            />
            <NumberInput
              label="Часов в рабочем дне"
              description="Для длительности по оценке и нагрузки"
              min={1}
              max={24}
              value={settings.hoursPerDay}
              onChange={(v) => set('hoursPerDay', Number(v) || 8)}
              error={err('hoursPerDay')}
            />
          </Group>
          <MultiSelect
            label="Иерархия задач"
            description="Связи Jira, по которым задачи вкладываются друг в друга в списке задач. Дочерние задачи по этим связям загружаются вместе с эпиком."
            placeholder={settings.hierarchyLinks.length ? undefined : 'Без иерархии'}
            searchable
            clearable
            data={hierarchyOptions(jira?.linkTypes ?? [], settings.hierarchyLinks)}
            value={settings.hierarchyLinks.map(ruleValue)}
            onChange={(values) => {
              setHierarchyTouched(true)
              set('hierarchyLinks', values.map(parseRule))
            }}
            nothingFoundMessage="Тип связи не найден"
          />
          <Text size="xs" c="dimmed">
            Оценка берётся из Original Estimate. В Jira приложение пишет только сроки (и по желанию исполнителя) — по кнопке «Отправить в Jira» с предпросмотром.
          </Text>
        </>
      )}

      <Group justify="flex-end" mt="sm">
        <Button variant="default" onClick={onClose}>
          Отмена
        </Button>
        <Button onClick={() => void submit()} loading={save.isPending} disabled={!showSettings || settings.projectKeys.length === 0}>
          Сохранить
        </Button>
      </Group>
    </Stack>
  )
}

const ruleValue = (r: HierarchyLinkDto) => `${r.linkTypeName}|${r.parentSide}`

function parseRule(value: string): HierarchyLinkDto {
  const i = value.lastIndexOf('|')
  return { linkTypeName: value.slice(0, i), parentSide: value.slice(i + 1) === 'inward' ? 'inward' : 'outward' }
}

/** Для каждого типа связи — два варианта: родитель на стороне outward («includes») или inward («is tested by»). */
function hierarchyOptions(linkTypes: JiraLinkTypeDto[], selected: HierarchyLinkDto[]) {
  const options = linkTypes.flatMap((t) => [
    { value: `${t.name}|outward`, label: `Родитель «${t.outward}» дочернюю (${t.name})` },
    { value: `${t.name}|inward`, label: `Родитель «${t.inward}» дочернюю (${t.name})` },
  ])
  const missing = selected.map(ruleValue).filter((v) => !options.some((o) => o.value === v))
  return [...options, ...missing.map((value) => ({ value, label: value.replace('|', ' · ') }))]
}

/** Сохранённые ключи, которых нет в списке Jira (нет доступа или проект удалён), всё равно показываем. */
function withSelected(options: { value: string; label: string }[], selected: string[]) {
  const missing = selected.filter((key) => !options.some((o) => o.value === key))
  return [...options, ...missing.map((key) => ({ value: key, label: `${key} (нет в списке Jira)` }))]
}

function defaultName(jira: JiraDiscoverResult, projectKeys: string[]): string {
  if (projectKeys.length === 0) return jira.suggestedName
  const host = (() => {
    try {
      return new URL(jira.baseUrl).hostname
    } catch {
      return jira.baseUrl
    }
  })()
  const keys = projectKeys.slice(0, 3).join(', ')
  return `${host} · ${keys}${projectKeys.length > 3 ? ` +${projectKeys.length - 3}` : ''}`
}
