/**
 * Даты на диаграмме — «номер дня»: число дней с 1970-01-01 (UTC).
 * Так арифметика (сдвиг, длительность, позиция в пикселях) — целочисленная и без часовых поясов.
 * Концы периодов включительные, как в Jira.
 */
export type Day = number

const MS_PER_DAY = 86_400_000

/** "2026-10-05" → номер дня. */
export function toDay(iso: string): Day {
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number)
  return Math.round(Date.UTC(y, m - 1, d) / MS_PER_DAY)
}

/** Номер дня → "2026-10-05". */
export function fromDay(day: Day): string {
  return new Date(day * MS_PER_DAY).toISOString().slice(0, 10)
}

export const toDayOrNull = (iso: string | null | undefined): Day | null => (iso ? toDay(iso) : null)

export function todayDay(now = new Date()): Day {
  return Math.round(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()) / MS_PER_DAY)
}

/** 0 — воскресенье … 6 — суббота (1970-01-01 был четвергом). */
export function dayOfWeek(day: Day): number {
  return (((day + 4) % 7) + 7) % 7
}

export const isWeekend = (day: Day): boolean => {
  const dow = dayOfWeek(day)
  return dow === 0 || dow === 6
}

/** Понедельник недели, в которую входит день. */
export function startOfWeek(day: Day): Day {
  return day - ((dayOfWeek(day) + 6) % 7)
}

/** Число рабочих дней (Пн–Пт) в [start, end]. */
export function countWorkingDays(start: Day, end: Day): number {
  if (end < start) return 0
  const total = end - start + 1
  const fullWeeks = Math.floor(total / 7)
  let count = fullWeeks * 5
  for (let d = start + fullWeeks * 7; d <= end; d++) if (!isWeekend(d)) count++
  return count
}

/** Длительность в рабочих днях по оценке: ceil(часы / часов в дне), минимум 1. */
export function durationFromEstimate(estimateSeconds: number | null | undefined, hoursPerDay: number): number {
  if (!estimateSeconds || estimateSeconds <= 0 || hoursPerDay <= 0) return 1
  return Math.max(1, Math.ceil(estimateSeconds / 3600 / hoursPerDay - 1e-9))
}

/** Конец (включительно) задачи длительностью workingDays рабочих дней с началом в start. */
export function endForWorkingDays(start: Day, workingDays: number): Day {
  if (workingDays <= 1) return start
  let end = start
  let counted = isWeekend(start) ? 0 : 1
  while (counted < workingDays) {
    end++
    if (!isWeekend(end)) counted++
  }
  return end
}

export function dayToDate(day: Day): Date {
  const d = new Date(day * MS_PER_DAY)
  return new Date(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate())
}

const shortFormatter = new Intl.DateTimeFormat('ru-RU', { day: '2-digit', month: '2-digit' })
const longFormatter = new Intl.DateTimeFormat('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric' })

export const formatDayShort = (day: Day) => shortFormatter.format(dayToDate(day))
export const formatDay = (day: Day) => longFormatter.format(dayToDate(day))

export function formatIso(iso: string | null | undefined): string {
  return iso ? formatDay(toDay(iso)) : '—'
}

export function formatRange(start: Day, end: Day): string {
  return `${formatDayShort(start)} – ${formatDayShort(end)}`
}
