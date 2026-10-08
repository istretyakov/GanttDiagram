import { countWorkingDays, isWeekend, type Day } from '../../shared/dateMath'

export type LoadItem = { start: Day; end: Day; estimateSeconds: number | null | undefined }

/**
 * Часы по дням: оценка задачи равномерно делится на её рабочие дни.
 * Совпадает с WorkloadService.CalculateDays на backend.
 */
export function hoursByDay(items: readonly LoadItem[]): Map<Day, number> {
  const result = new Map<Day, number>()
  for (const item of items) {
    if (!item.estimateSeconds || item.estimateSeconds <= 0) continue
    const workingDays = countWorkingDays(item.start, item.end)
    const perDay = item.estimateSeconds / 3600 / Math.max(1, workingDays)
    for (let day = item.start; day <= item.end; day++) {
      // Задача целиком на выходных — считаем все её дни, иначе только рабочие.
      if (workingDays > 0 && isWeekend(day)) continue
      result.set(day, (result.get(day) ?? 0) + perDay)
    }
  }
  return result
}

export const isOverloaded = (hours: number, hoursPerDay: number, day: Day) =>
  hours > (isWeekend(day) ? 0 : hoursPerDay) + 0.001
