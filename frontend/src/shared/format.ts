export function formatHours(seconds: number | null | undefined): string {
  if (!seconds) return '—'
  const hours = seconds / 3600
  return `${Number.isInteger(hours) ? hours : hours.toFixed(1)} ч`
}

export const pluralDays = (n: number) => {
  const mod10 = n % 10
  const mod100 = n % 100
  if (mod10 === 1 && mod100 !== 11) return `${n} день`
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return `${n} дня`
  return `${n} дней`
}

export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]!.toUpperCase())
    .join('')
}

/** Сортировка ключей Jira по номеру: PRJ-2 < PRJ-10. */
export function compareIssueKeys(a: string, b: string): number {
  const [pa, na] = splitKey(a)
  const [pb, nb] = splitKey(b)
  return pa === pb ? na - nb : pa.localeCompare(pb)
}

function splitKey(key: string): [string, number] {
  const i = key.lastIndexOf('-')
  return i < 0 ? [key, 0] : [key.slice(0, i), Number(key.slice(i + 1)) || 0]
}

/** Сохранение небольших настроек интерфейса; хранилище может быть недоступно. */
export const storage = {
  get(key: string): string | null {
    try {
      return localStorage.getItem(key)
    } catch {
      return null
    }
  },
  set(key: string, value: string) {
    try {
      localStorage.setItem(key, value)
    } catch {
      // приватный режим и т. п.
    }
  },
}
