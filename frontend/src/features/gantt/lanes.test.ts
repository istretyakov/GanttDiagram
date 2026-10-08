import { describe, expect, it } from 'vitest'
import { assignLanes } from './lanes'

describe('assignLanes', () => {
  it('puts non-overlapping tasks in one lane', () => {
    const { lanes, count } = assignLanes([
      { id: 'a', start: 0, end: 2 },
      { id: 'b', start: 3, end: 5 },
    ])
    expect(count).toBe(1)
    expect(lanes.get('a')).toBe(0)
    expect(lanes.get('b')).toBe(0)
  })

  it('treats inclusive ends as overlapping', () => {
    const { lanes, count } = assignLanes([
      { id: 'a', start: 0, end: 2 },
      { id: 'b', start: 2, end: 4 },
    ])
    expect(count).toBe(2)
    expect(lanes.get('b')).toBe(1)
  })

  it('reuses the first free lane greedily', () => {
    const { lanes, count } = assignLanes([
      { id: 'c', start: 4, end: 6 },
      { id: 'a', start: 0, end: 5 },
      { id: 'b', start: 1, end: 2 },
      { id: 'd', start: 7, end: 8 },
    ])
    expect(count).toBe(2)
    expect(lanes.get('a')).toBe(0)
    expect(lanes.get('b')).toBe(1)
    expect(lanes.get('c')).toBe(1) // b закончилась во 2-й день
    expect(lanes.get('d')).toBe(0)
  })

  it('returns at least one lane for an empty row', () => {
    expect(assignLanes([]).count).toBe(1)
  })
})
