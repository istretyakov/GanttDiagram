import { describe, expect, it } from 'vitest'
import { compareIssueKeys } from './format'
import { buildTreeRows } from './issueTree'

type Item = { key: string; parent?: string; done?: boolean }

const items: Item[] = [
  { key: 'PRJ-10' },
  { key: 'QA-5', parent: 'PRJ-2' },
  { key: 'PRJ-3', parent: 'PRJ-2', done: true },
  { key: 'PRJ-2' },
  { key: 'QA-7', parent: 'PRJ-3' },
  { key: 'PRJ-4', parent: 'OUT-1' }, // родитель вне проекта → корень
]

const options = {
  id: (i: Item) => i.key,
  parentId: (i: Item) => i.parent,
  compare: (a: Item, b: Item) => compareIssueKeys(a.key, b.key),
}

const view = (rows: ReturnType<typeof buildTreeRows<Item>>) =>
  rows.map((r) => `${'  '.repeat(r.depth)}${r.item.key}${r.hasChildren ? (r.collapsed ? ' +' : ' -') : ''}${r.context ? ' (ctx)' : ''}`)

describe('buildTreeRows', () => {
  it('builds a depth-first tree sorted by key', () => {
    expect(view(buildTreeRows(items, options))).toEqual([
      'PRJ-2 -',
      '  PRJ-3 -',
      '    QA-7',
      '  QA-5',
      'PRJ-4',
      'PRJ-10',
    ])
  })

  it('hides children of collapsed nodes', () => {
    expect(view(buildTreeRows(items, { ...options, collapsed: new Set(['PRJ-3']) }))).toEqual([
      'PRJ-2 -',
      '  PRJ-3 +',
      '  QA-5',
      'PRJ-4',
      'PRJ-10',
    ])
  })

  it('keeps ancestors of matching items as context', () => {
    expect(view(buildTreeRows(items, { ...options, matches: (i) => i.key === 'QA-7' }))).toEqual([
      'PRJ-2 - (ctx)',
      '  PRJ-3 - (ctx)',
      '    QA-7',
    ])
  })

  it('survives cycles', () => {
    const cyclic: Item[] = [
      { key: 'A-1', parent: 'A-2' },
      { key: 'A-2', parent: 'A-1' },
      { key: 'A-3' },
    ]
    expect(view(buildTreeRows(cyclic, options))).toEqual(['A-3'])
  })
})
