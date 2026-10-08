import { Box, Group, Text } from '@mantine/core'
import { useState } from 'react'
import type { WorkloadDayDto } from '../../api/types'
import { formatDay, toDay } from '../../shared/dateMath'

const BAR_WIDTH = 16
const GAP = 2
const HEIGHT = 120

/** Гистограмма часов по дням против ёмкости (часов в дне); перегрузка — красным. */
export function WorkloadHistogram({ days, hoursPerDay, today }: { days: WorkloadDayDto[]; hoursPerDay: number; today: string }) {
  const [hover, setHover] = useState<WorkloadDayDto | null>(null)
  const max = Math.max(hoursPerDay * 1.5, ...days.map((d) => d.hours))
  const scale = (h: number) => (h / max) * HEIGHT
  const width = days.length * (BAR_WIDTH + GAP)
  const capacityY = HEIGHT - scale(hoursPerDay)

  return (
    <Box>
      <Box style={{ overflowX: 'auto' }}>
        <svg width={width} height={HEIGHT + 18} role="img" aria-label="Часы по дням">
          {days.map((d, i) => {
            const x = i * (BAR_WIDTH + GAP)
            const h = scale(d.hours)
            const weekend = d.capacity === 0
            const isToday = d.date === today
            return (
              <g key={d.date} onMouseEnter={() => setHover(d)} onMouseLeave={() => setHover(null)}>
                {weekend && <rect x={x} y={0} width={BAR_WIDTH} height={HEIGHT} fill="var(--mantine-color-gray-light)" />}
                <rect
                  x={x}
                  y={HEIGHT - h}
                  width={BAR_WIDTH}
                  height={h}
                  rx={2}
                  fill={d.overloaded ? 'var(--mantine-color-red-filled)' : 'var(--mantine-color-teal-filled)'}
                  opacity={hover && hover.date !== d.date ? 0.6 : 1}
                />
                <rect x={x} y={0} width={BAR_WIDTH} height={HEIGHT} fill="transparent" />
                {(i === 0 || new Date(d.date).getUTCDay() === 1) && (
                  <text x={x} y={HEIGHT + 13} fontSize={10} fill="var(--mantine-color-dimmed)">
                    {d.date.slice(8, 10)}.{d.date.slice(5, 7)}
                  </text>
                )}
                {isToday && <rect x={x} y={HEIGHT + 1} width={BAR_WIDTH} height={2} fill="var(--mantine-color-red-filled)" />}
              </g>
            )
          })}
          <line x1={0} x2={width} y1={capacityY} y2={capacityY} stroke="var(--mantine-color-dimmed)" strokeDasharray="4 3" />
        </svg>
      </Box>
      <Group gap="md" mt={4}>
        <Text size="xs" c="dimmed">
          Пунктир — {hoursPerDay} ч в день
        </Text>
        {hover && (
          <Text size="xs">
            {formatDay(toDay(hover.date))}: <b>{hover.hours} ч</b> из {hover.capacity}
            {hover.overloaded && ' — перегрузка'}
          </Text>
        )}
      </Group>
    </Box>
  )
}
