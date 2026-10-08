import { describe, expect, it } from 'vitest'
import { toDay } from '../../shared/dateMath'
import { hoursByDay, isOverloaded } from './workload'

const monday = toDay('2026-10-05')
const h = (hours: number) => hours * 3600

describe('hoursByDay', () => {
  it('spreads the estimate over working days only', () => {
    // чт–вт: 4 рабочих дня, 16 ч → по 4 ч.
    const hours = hoursByDay([{ start: monday + 3, end: monday + 8, estimateSeconds: h(16) }])
    expect(hours.get(monday + 3)).toBe(4)
    expect(hours.has(monday + 5)).toBe(false)
    expect([...hours.values()].reduce((a, b) => a + b, 0)).toBe(16)
  })

  it('sums overlapping tasks and detects overload', () => {
    const hours = hoursByDay([
      { start: monday, end: monday, estimateSeconds: h(6) },
      { start: monday, end: monday + 1, estimateSeconds: h(8) },
    ])
    expect(hours.get(monday)).toBe(10)
    expect(isOverloaded(10, 8, monday)).toBe(true)
    expect(isOverloaded(4, 8, monday + 1)).toBe(false)
    expect(isOverloaded(1, 8, monday + 5)).toBe(true) // работа в субботу
  })

  it('ignores tasks without estimate', () => {
    expect(hoursByDay([{ start: monday, end: monday + 2, estimateSeconds: null }]).size).toBe(0)
  })
})
