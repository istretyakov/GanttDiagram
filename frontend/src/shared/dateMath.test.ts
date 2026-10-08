import { describe, expect, it } from 'vitest'
import {
  countWorkingDays,
  dayOfWeek,
  durationFromEstimate,
  endForWorkingDays,
  fromDay,
  isWeekend,
  startOfWeek,
  toDay,
} from './dateMath'

// 2026-10-05 — понедельник.
const monday = toDay('2026-10-05')

describe('dateMath', () => {
  it('converts ISO dates to day numbers and back', () => {
    expect(toDay('1970-01-01')).toBe(0)
    expect(fromDay(monday)).toBe('2026-10-05')
    expect(fromDay(toDay('2024-02-29'))).toBe('2024-02-29')
    expect(toDay('2026-10-05T10:00:00.000+0300')).toBe(monday)
  })

  it('detects weekdays and weekends', () => {
    expect(dayOfWeek(monday)).toBe(1)
    expect(isWeekend(monday + 5)).toBe(true) // суббота
    expect(isWeekend(monday + 6)).toBe(true) // воскресенье
    expect(isWeekend(monday + 4)).toBe(false) // пятница
    expect(startOfWeek(monday + 3)).toBe(monday)
    expect(startOfWeek(monday + 6)).toBe(monday)
  })

  it.each([
    [0, 0, 1],
    [0, 4, 5],
    [0, 6, 5],
    [5, 6, 0],
    [4, 7, 2],
    [0, 13, 10],
    [2, 22, 15],
  ])('counts working days %i..%i inclusive = %i', (from, to, expected) => {
    expect(countWorkingDays(monday + from, monday + to)).toBe(expected)
  })

  it.each([
    [null, 1],
    [0, 1],
    [3600, 1],
    [8 * 3600, 1],
    [9 * 3600, 2],
    [40 * 3600, 5],
  ])('duration from estimate %s s = %i days', (seconds, days) => {
    expect(durationFromEstimate(seconds, 8)).toBe(days)
  })

  it.each([
    [0, 1, 0],
    [0, 5, 4],
    [3, 3, 7],
    [4, 2, 7],
    [5, 2, 8],
    [0, 10, 11],
  ])('end for start +%i and %i working days = +%i', (start, days, end) => {
    expect(endForWorkingDays(monday + start, days)).toBe(monday + end)
  })
})
