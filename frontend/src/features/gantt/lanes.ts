import type { Day } from '../../shared/dateMath'

export type Span = { id: string; start: Day; end: Day }

/**
 * Раскладка перекрывающихся задач по дорожкам жадным алгоритмом:
 * сортировка по началу, задача кладётся в первую дорожку, где предыдущая уже закончилась.
 * Возвращает номер дорожки для каждой задачи и число дорожек (минимум 1).
 */
export function assignLanes(spans: readonly Span[]): { lanes: Map<string, number>; count: number } {
  const sorted = [...spans].sort((a, b) => a.start - b.start || a.end - b.end || a.id.localeCompare(b.id))
  const laneEnds: Day[] = []
  const lanes = new Map<string, number>()

  for (const span of sorted) {
    let lane = laneEnds.findIndex((end) => end < span.start)
    if (lane === -1) {
      lane = laneEnds.length
      laneEnds.push(span.end)
    } else {
      laneEnds[lane] = span.end
    }
    lanes.set(span.id, lane)
  }

  return { lanes, count: Math.max(1, laneEnds.length) }
}
