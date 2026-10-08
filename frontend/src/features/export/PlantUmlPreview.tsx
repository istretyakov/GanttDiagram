import { Box, Text } from '@mantine/core'
import { encode } from 'plantuml-encoder'
import { useMemo } from 'react'

export const PLANTUML_SERVER_KEY = 'plantuml.server'

/**
 * Предпросмотр PlantUML через внутренний сервер компании: {server}/svg/{encoded}.
 * Если адрес не задан — предпросмотра нет.
 */
export function PlantUmlPreview({ source, server }: { source: string; server: string }) {
  const url = useMemo(() => (server ? `${server.replace(/\/+$/, '')}/svg/${encode(source)}` : null), [source, server])

  if (!url) {
    return (
      <Text size="sm" c="dimmed">
        Укажите адрес PlantUML-сервера компании, чтобы видеть картинку. Текст можно вставить в Confluence или GitLab.
      </Text>
    )
  }
  return (
    <Box style={{ overflow: 'auto', maxHeight: 520 }}>
      <img src={url} alt="Диаграмма PlantUML" />
    </Box>
  )
}
