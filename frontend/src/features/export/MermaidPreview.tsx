import { Alert, Box, Loader } from '@mantine/core'
import { useEffect, useId, useState } from 'react'

type Rendered = { source: string; svg?: string; error?: string }

/** Предпросмотр Mermaid. Библиотека тяжёлая — подгружается только при открытии (import()). */
export function MermaidPreview({ source }: { source: string }) {
  const id = useId().replace(/[^a-zA-Z0-9]/g, '')
  const [rendered, setRendered] = useState<Rendered | null>(null)

  useEffect(() => {
    let cancelled = false
    import('mermaid')
      .then(async ({ default: mermaid }) => {
        mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: 'default' })
        const { svg } = await mermaid.render(`mermaid-${id}`, source)
        if (!cancelled) setRendered({ source, svg })
      })
      .catch((e: unknown) => {
        if (!cancelled) setRendered({ source, error: e instanceof Error ? e.message : String(e) })
      })
    return () => {
      cancelled = true
    }
  }, [source, id])

  // Результат для предыдущего текста не показываем.
  if (!rendered || rendered.source !== source) return <Loader size="sm" />
  if (rendered.error) return <Alert color="red">Mermaid не смог построить диаграмму: {rendered.error}</Alert>
  return <Box style={{ overflow: 'auto', maxHeight: 520 }} dangerouslySetInnerHTML={{ __html: rendered.svg! }} />
}
