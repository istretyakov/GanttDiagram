import { describe, expect, it } from 'vitest'
import { EPIC_COLORS, epicColor, epicHash, NO_EPIC_COLOR } from './epicPalette'

describe('epicPalette', () => {
  it('matches the backend FNV-1a hash (EpicPalette.cs)', () => {
    // То же значение зафиксировано в ExporterTests.EpicColors_AreStable.
    expect(epicHash('PRJ-1')).toBe(1_665_203_501)
  })

  it('assigns stable colors from the palette', () => {
    expect(epicColor('PRJ-1')).toBe(EPIC_COLORS[1_665_203_501 % EPIC_COLORS.length])
    expect(epicColor('PRJ-1')).toBe(epicColor('PRJ-1'))
    expect(epicColor(null)).toBe(NO_EPIC_COLOR)
  })
})
