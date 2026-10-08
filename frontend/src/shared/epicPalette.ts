/**
 * Цвет полосы закреплён за ключом эпика. Алгоритм и палитра совпадают с backend
 * (GanttDiagram.Application/Export/EpicPalette.cs), поэтому цвета в экспорте PlantUML те же.
 */
export const EPIC_COLORS = [
  '#9EC5FE', '#A3CFBB', '#FFDA6A', '#F1AEB5', '#C5B3E6',
  '#9EEAF9', '#FECBA1', '#A6E9D5', '#E2D9F3', '#D3D3D4',
] as const

export const NO_EPIC_COLOR = '#CED4DA'

/** FNV-1a по UTF-16 кодам символов. */
export function epicHash(text: string): number {
  let hash = 2166136261
  for (let i = 0; i < text.length; i++) {
    hash ^= text.charCodeAt(i)
    hash = Math.imul(hash, 16777619) >>> 0
  }
  return hash >>> 0
}

export function epicColor(epicKey: string | null | undefined): string {
  if (!epicKey) return NO_EPIC_COLOR
  return EPIC_COLORS[epicHash(epicKey) % EPIC_COLORS.length]
}
