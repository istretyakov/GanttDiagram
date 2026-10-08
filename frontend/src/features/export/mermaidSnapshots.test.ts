import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import mermaid from 'mermaid'
import { describe, expect, it } from 'vitest'

/**
 * Эталоны экспорта Mermaid из backend-тестов (Verify) должны разбираться настоящим парсером mermaid:
 * так проверяется экранирование и формат строк задач.
 */
const snapshotsDir = join(__dirname, '../../../../backend/tests/GanttDiagram.Tests/Export/Snapshots')
const files = readdirSync(snapshotsDir).filter((f) => f.endsWith('.verified.mmd'))

describe('Mermaid export snapshots', () => {
  it('exist', () => {
    expect(files.length).toBeGreaterThan(0)
  })

  it.each(files)('%s parses', async (file) => {
    const source = readFileSync(join(snapshotsDir, file), 'utf8').replace(/^﻿/, '')
    mermaid.initialize({ startOnLoad: false })
    await expect(mermaid.parse(source)).resolves.toBeTruthy()
  })

  it('rejects broken syntax (sanity check)', async () => {
    // Задача без названия — синтаксическая ошибка.
    await expect(mermaid.parse('gantt\n    section A\n    :t1, 2026-10-05, 1d')).rejects.toThrow()
  })
})
