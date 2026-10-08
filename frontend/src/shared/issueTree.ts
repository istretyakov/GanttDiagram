/** Строка дерева задач для плоского рендера (с отступом по глубине). */
export type TreeRow<T> = {
  item: T
  depth: number
  hasChildren: boolean
  collapsed: boolean
  /** Задача не подходит под фильтр, но показана как предок подходящей. */
  context: boolean
}

type TreeOptions<T> = {
  id(item: T): string
  parentId(item: T): string | null | undefined
  compare(a: T, b: T): number
  /** Фильтр: показываются подходящие задачи и их предки. */
  matches?(item: T): boolean
  collapsed?: ReadonlySet<string>
}

/**
 * Иерархия из Jira (includes, tested by…) → плоский список строк в порядке обхода в глубину.
 * Родитель вне набора — задача становится корнем.
 */
export function buildTreeRows<T>(items: readonly T[], options: TreeOptions<T>): TreeRow<T>[] {
  const { id, parentId, compare, matches, collapsed = new Set<string>() } = options
  const byId = new Map(items.map((item) => [id(item), item]))
  const parentOf = (item: T) => {
    const parent = parentId(item)
    return parent && parent !== id(item) && byId.has(parent) ? parent : null
  }

  // Что показываем: всё или подходящие под фильтр + их предки.
  let visible: Set<string>
  if (matches) {
    visible = new Set()
    for (const item of items) {
      if (!matches(item)) continue
      for (let current: T | undefined = item; current && !visible.has(id(current)); ) {
        visible.add(id(current))
        const parent = parentOf(current)
        current = parent ? byId.get(parent) : undefined
      }
    }
  } else {
    visible = new Set(byId.keys())
  }

  const children = new Map<string | null, T[]>()
  for (const item of items) {
    if (!visible.has(id(item))) continue
    const parent = parentOf(item)
    const list = children.get(parent) ?? []
    list.push(item)
    children.set(parent, list)
  }
  for (const list of children.values()) list.sort(compare)

  const rows: TreeRow<T>[] = []
  const seen = new Set<string>()
  const walk = (parent: string | null, depth: number) => {
    for (const item of children.get(parent) ?? []) {
      const key = id(item)
      if (seen.has(key)) continue // защита от циклов
      seen.add(key)
      const hasChildren = (children.get(key)?.length ?? 0) > 0
      const isCollapsed = hasChildren && collapsed.has(key)
      rows.push({ item, depth, hasChildren, collapsed: isCollapsed, context: !!matches && !matches(item) })
      if (!isCollapsed) walk(key, depth + 1)
    }
  }
  walk(null, 0)
  return rows
}
