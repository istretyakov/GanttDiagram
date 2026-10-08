import { useDroppable } from '@dnd-kit/core'
import { HoverCard } from '@mantine/core'
import { memo, useMemo, useRef, useState, type CSSProperties, type ReactNode, type RefObject } from 'react'
import {
  countWorkingDays,
  dayOfWeek,
  dayToDate,
  formatRange,
  isWeekend,
  type Day,
} from '../../shared/dateMath'
import { pluralDays } from '../../shared/format'
import { assignLanes } from './lanes'
import classes from './Timeline.module.css'
import { hoursByDay, isOverloaded } from './workload'

export const LEFT_WIDTH = 240
export const LANE_HEIGHT = 28
const ROW_PADDING_TOP = 8
const ROW_PADDING_BOTTOM = 4
const EDGE_SCROLL_ZONE = 48
const DRAG_THRESHOLD_PX = 3

export type BarModel = {
  /** Идентификатор задачи. */
  id: string
  /** Ключ задачи Jira. */
  issueKey: string
  rowId: string
  start: Day
  end: Day
  label: string
  color: string
  estimateSeconds?: number | null
  dirty?: boolean
  differs?: boolean
  done?: boolean
  /** Содержимое всплывающей карточки. */
  card?: ReactNode
}

export type TimelineRow = {
  id: string
  header: ReactNode
  bars: BarModel[]
  /** Можно ли бросить сюда задачу (перенос полосы или из списка). */
  droppable?: boolean
}

export type BarCommit =
  | { issueId: string; start: Day; end: Day; rowId: string; unplan?: false }
  | { issueId: string; unplan: true }

export type Ghost = { rowId: string; start: Day; end: Day }

type TimelineProps = {
  rows: TimelineRow[]
  from: Day
  to: Day
  today: Day
  dayWidth: number
  hoursPerDay: number
  readOnly?: boolean
  onCommit?: (commit: BarCommit) => void
  footer?: ReactNode
  ghost?: Ghost | null
  scrollRef: RefObject<HTMLDivElement | null>
  cornerTitle?: string
}

export function Timeline(props: TimelineProps) {
  const { rows, from, to, today, dayWidth, scrollRef, footer, cornerTitle = 'Участники' } = props
  const days = to - from + 1
  const width = days * dayWidth

  // Выходные затенены: плитка в неделю, начинается с понедельника.
  const mondayIndex = (1 - dayOfWeek(from) + 7) % 7
  const gridStyle: CSSProperties = {
    left: LEFT_WIDTH,
    width,
    backgroundImage: [
      `linear-gradient(90deg, transparent ${5 * dayWidth}px, var(--mantine-color-gray-light) ${5 * dayWidth}px)`,
      dayWidth >= 16
        ? `linear-gradient(90deg, var(--mantine-color-default-border) 1px, transparent 1px)`
        : 'none',
    ].join(','),
    backgroundSize: `${7 * dayWidth}px 100%, ${dayWidth}px 100%`,
    backgroundPosition: `${mondayIndex * dayWidth}px 0, 0 0`,
  }

  return (
    <div className={classes.scroller} ref={scrollRef} data-testid="timeline">
      <div className={classes.inner} style={{ width: LEFT_WIDTH + width }}>
        <div className={classes.header}>
          <div className={classes.corner} style={{ width: LEFT_WIDTH }}>
            {cornerTitle}
          </div>
          <TimelineScale from={from} to={to} today={today} dayWidth={dayWidth} />
        </div>

        <div className={classes.body}>
          <div className={classes.grid} style={gridStyle} />
          {today >= from && today <= to && (
            <div className={classes.todayLine} style={{ left: LEFT_WIDTH + (today - from) * dayWidth + dayWidth / 2 }} />
          )}
          {rows.map((row) => (
            <Row
              key={row.id}
              row={row}
              from={from}
              width={width}
              dayWidth={dayWidth}
              hoursPerDay={props.hoursPerDay}
              readOnly={props.readOnly}
              onCommit={props.onCommit}
              scrollRef={scrollRef}
              ghost={props.ghost?.rowId === row.id ? props.ghost : null}
            />
          ))}
          {footer && (
            <div className={classes.footer} style={{ width: LEFT_WIDTH }}>
              {footer}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// ---------- шкала

const monthFormatter = new Intl.DateTimeFormat('ru-RU', { month: 'short', year: 'numeric' })
const monthOnlyFormatter = new Intl.DateTimeFormat('ru-RU', { month: 'short' })

type Segment = { start: Day; end: Day; label: string }

/** Группирует подряд идущие дни с одинаковым ключом (месяц, год) в сегменты шкалы. */
function segmentsBy(from: Day, to: Day, keyOf: (d: Date) => string, labelOf: (d: Date) => string): Segment[] {
  const result: (Segment & { key: string })[] = []
  for (let day = from; day <= to; day++) {
    const date = dayToDate(day)
    const key = keyOf(date)
    const last = result[result.length - 1]
    if (last?.key === key) last.end = day
    else result.push({ start: day, end: day, label: labelOf(date), key })
  }
  return result
}

const TimelineScale = memo(function TimelineScale(props: { from: Day; to: Day; today: Day; dayWidth: number }) {
  const { from, to, today, dayWidth } = props

  const { top, bottom } = useMemo(() => {
    if (dayWidth <= 4) {
      return {
        top: segmentsBy(from, to, (d) => `${d.getFullYear()}`, (d) => `${d.getFullYear()}`),
        bottom: segmentsBy(from, to, (d) => `${d.getFullYear()}-${d.getMonth()}`, (d) => monthOnlyFormatter.format(d)),
      }
    }
    const months = segmentsBy(from, to, (d) => `${d.getFullYear()}-${d.getMonth()}`, (d) => monthFormatter.format(d))
    if (dayWidth < 30) {
      // Неделя: подпись у каждого понедельника.
      const weeks: Segment[] = []
      for (let day = from; day <= to; day++) {
        if (day === from || dayOfWeek(day) === 1) {
          const date = dayToDate(day)
          weeks.push({ start: day, end: Math.min(to, day + ((8 - dayOfWeek(day)) % 7 || 7) - 1), label: `${date.getDate()}.${String(date.getMonth() + 1).padStart(2, '0')}` })
        }
      }
      return { top: months, bottom: weeks }
    }
    const days: Segment[] = []
    for (let day = from; day <= to; day++) days.push({ start: day, end: day, label: String(dayToDate(day).getDate()) })
    return { top: months, bottom: days }
  }, [from, to, dayWidth])

  const cell = (s: Segment, extra = '') => (
    <div
      key={s.start}
      className={`${classes.scaleCell} ${extra}`}
      style={{ left: (s.start - from) * dayWidth, width: (s.end - s.start + 1) * dayWidth }}
      title={s.label}
    >
      {s.label}
    </div>
  )

  return (
    <div className={classes.scale} style={{ width: (to - from + 1) * dayWidth }}>
      <div className={classes.scaleRow}>{top.map((s) => cell(s, classes.scaleTop))}</div>
      <div className={classes.scaleRow}>
        {bottom.map((s) =>
          cell(
            s,
            [
              s.start === s.end && isWeekend(s.start) ? classes.scaleWeekend : '',
              s.start <= today && today <= s.end && dayWidth >= 30 ? classes.scaleToday : '',
            ].join(' '),
          ),
        )}
      </div>
    </div>
  )
})

// ---------- строка

type RowProps = {
  row: TimelineRow
  from: Day
  width: number
  dayWidth: number
  hoursPerDay: number
  readOnly?: boolean
  onCommit?: (commit: BarCommit) => void
  scrollRef: RefObject<HTMLDivElement | null>
  ghost: Ghost | null
}

const Row = memo(function Row({ row, from, width, dayWidth, hoursPerDay, readOnly, onCommit, scrollRef, ghost }: RowProps) {
  const { setNodeRef, isOver } = useDroppable({
    id: `row:${row.id}`,
    data: { rowId: row.id },
    disabled: readOnly || !row.droppable,
  })

  const { lanes, count } = useMemo(() => {
    const spans = row.bars.map((b) => ({ id: b.id, start: b.start, end: b.end }))
    if (ghost) spans.push({ id: '__ghost__', start: ghost.start, end: ghost.end })
    return assignLanes(spans)
  }, [row.bars, ghost])

  const load = useMemo(
    () => [...hoursByDay(row.bars.map((b) => ({ start: b.start, end: b.end, estimateSeconds: b.estimateSeconds })))],
    [row.bars],
  )

  const height = ROW_PADDING_TOP + count * LANE_HEIGHT + ROW_PADDING_BOTTOM

  return (
    <div
      className={`${classes.row} ${isOver ? classes.dndOver : ''}`}
      data-row-id={row.droppable ? row.id : undefined}
      data-testid={`row-${row.id}`}
    >
      <div className={classes.rowHead} style={{ width: LEFT_WIDTH, minHeight: height }}>
        {row.header}
      </div>
      <div className={classes.track} style={{ width, height }} ref={setNodeRef}>
        <div className={classes.loadStrip}>
          {load.map(([day, hours]) => {
            const over = isOverloaded(hours, hoursPerDay, day)
            const ratio = Math.min(1, hours / hoursPerDay)
            return (
              <div
                key={day}
                className={classes.loadCell}
                title={`${Math.round(hours * 10) / 10} ч из ${isWeekend(day) ? 0 : hoursPerDay}`}
                style={{
                  left: (day - from) * dayWidth,
                  width: dayWidth,
                  background: over
                    ? 'var(--mantine-color-red-filled)'
                    : `rgb(64 192 87 / ${0.25 + ratio * 0.6})`,
                }}
              />
            )
          })}
        </div>
        {row.bars.map((bar) => (
          <Bar
            key={bar.id}
            bar={bar}
            top={ROW_PADDING_TOP + (lanes.get(bar.id) ?? 0) * LANE_HEIGHT}
            from={from}
            dayWidth={dayWidth}
            readOnly={readOnly}
            onCommit={onCommit}
            scrollRef={scrollRef}
          />
        ))}
        {ghost && (
          <div
            className={classes.ghost}
            data-testid="drop-ghost"
            style={{
              top: ROW_PADDING_TOP + (lanes.get('__ghost__') ?? 0) * LANE_HEIGHT,
              left: (ghost.start - from) * dayWidth,
              width: (ghost.end - ghost.start + 1) * dayWidth,
            }}
          />
        )}
      </div>
    </div>
  )
})

// ---------- полоса

type DragMode = 'move' | 'left' | 'right'
type DragState = { start: Day; end: Day; dy: number; rowId: string; unplan: boolean }

type BarProps = {
  bar: BarModel
  top: number
  from: Day
  dayWidth: number
  readOnly?: boolean
  onCommit?: (commit: BarCommit) => void
  scrollRef: RefObject<HTMLDivElement | null>
}

const Bar = memo(function Bar({ bar, top, from, dayWidth, readOnly, onCommit, scrollRef }: BarProps) {
  const [drag, setDrag] = useState<DragState | null>(null)
  const barRef = useRef<HTMLDivElement>(null)

  function startDrag(event: React.PointerEvent, mode: DragMode) {
    if (readOnly || event.button !== 0 || !scrollRef.current || !barRef.current) return
    event.preventDefault()
    event.stopPropagation()

    const scroller = scrollRef.current
    const barEl = barRef.current
    try {
      barEl.setPointerCapture(event.pointerId)
    } catch {
      // Указатель уже не активен (например, синтетическое событие) — работаем через window-слушатели.
    }

    const origin = { x: event.clientX, y: event.clientY, scroll: scroller.scrollLeft }
    const pointer = { x: event.clientX, y: event.clientY }
    let state: DragState = { start: bar.start, end: bar.end, dy: 0, rowId: bar.rowId, unplan: false }
    let moved = false
    let frame = 0
    let highlighted: Element | null = null

    const highlight = (el: Element | null) => {
      if (highlighted === el) return
      highlighted?.classList.remove(classes.dropTarget)
      el?.classList.add(classes.dropTarget)
      highlighted = el
    }

    const compute = () => {
      const dx = pointer.x - origin.x + (scroller.scrollLeft - origin.scroll)
      const delta = Math.round(dx / dayWidth)
      let start = bar.start
      let end = bar.end
      if (mode === 'move') {
        start += delta
        end += delta
      } else if (mode === 'left') {
        start = Math.min(bar.start + delta, bar.end)
      } else {
        end = Math.max(bar.end + delta, bar.start)
      }

      let rowId = bar.rowId
      let unplan = false
      if (mode === 'move') {
        // Целевая строка — под указателем, без учёта самой полосы.
        const hits = document.elementsFromPoint(pointer.x, pointer.y).filter((el) => !barEl.contains(el))
        unplan = hits.some((el) => el.closest('[data-unplan-zone]'))
        const rowEl = unplan ? null : (hits.map((el) => el.closest('[data-row-id]')).find(Boolean) ?? null)
        if (rowEl) rowId = (rowEl as HTMLElement).dataset.rowId!
        highlight(rowEl && rowId !== bar.rowId ? rowEl : null)
      }

      state = { start, end, dy: mode === 'move' ? pointer.y - origin.y : 0, rowId, unplan }
      setDrag(state)
    }

    // Автопрокрутка, когда указатель у края видимой области.
    const tick = () => {
      if (moved) {
        const rect = scroller.getBoundingClientRect()
        let velocity = 0
        if (pointer.x < rect.left + LEFT_WIDTH + EDGE_SCROLL_ZONE) velocity = -Math.ceil((rect.left + LEFT_WIDTH + EDGE_SCROLL_ZONE - pointer.x) / 4)
        else if (pointer.x > rect.right - EDGE_SCROLL_ZONE) velocity = Math.ceil((pointer.x - rect.right + EDGE_SCROLL_ZONE) / 4)
        if (velocity !== 0) {
          const before = scroller.scrollLeft
          scroller.scrollLeft += velocity
          if (scroller.scrollLeft !== before) compute()
        }
      }
      frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)

    const onMove = (e: PointerEvent) => {
      pointer.x = e.clientX
      pointer.y = e.clientY
      if (!moved && Math.abs(e.clientX - origin.x) < DRAG_THRESHOLD_PX && Math.abs(e.clientY - origin.y) < DRAG_THRESHOLD_PX) return
      moved = true
      compute()
    }

    const finish = (commit: boolean) => {
      cancelAnimationFrame(frame)
      window.removeEventListener('pointermove', onMove)
      window.removeEventListener('pointerup', onUp)
      window.removeEventListener('pointercancel', onCancel)
      window.removeEventListener('keydown', onKey, true)
      highlight(null)
      setDrag(null)
      if (barEl.hasPointerCapture?.(event.pointerId)) barEl.releasePointerCapture(event.pointerId)
      if (!commit || !moved || !onCommit) return

      if (state.unplan) onCommit({ issueId: bar.id, unplan: true })
      else if (state.start !== bar.start || state.end !== bar.end || state.rowId !== bar.rowId)
        onCommit({ issueId: bar.id, start: state.start, end: state.end, rowId: state.rowId })
    }
    const onUp = () => finish(true)
    const onCancel = () => finish(false)
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault()
        e.stopPropagation()
        finish(false)
      }
    }

    window.addEventListener('pointermove', onMove)
    window.addEventListener('pointerup', onUp)
    window.addEventListener('pointercancel', onCancel)
    window.addEventListener('keydown', onKey, true)
  }

  const start = drag?.start ?? bar.start
  const end = drag?.end ?? bar.end
  const style: CSSProperties = {
    top,
    left: (start - from) * dayWidth,
    width: Math.max((end - start + 1) * dayWidth, 4),
    background: bar.color,
    transform: drag?.dy ? `translateY(${drag.dy}px)` : undefined,
  }
  const showLabel = dayWidth * (end - start + 1) >= 24

  const className = [
    classes.bar,
    readOnly ? classes.readOnly : '',
    drag ? classes.dragging : '',
    bar.dirty ? classes.dirty : '',
    bar.done ? classes.done : '',
  ].join(' ')

  const element = (
    <div
      ref={barRef}
      className={className}
      style={style}
      data-testid={`bar-${bar.issueKey}`}
      data-start={start}
      data-end={end}
      onPointerDown={(e) => startDrag(e, 'move')}
    >
      {!readOnly && <div className={classes.handle} data-handle="left" onPointerDown={(e) => startDrag(e, 'left')} />}
      <span className={classes.label}>{showLabel ? bar.label : ''}</span>
      {bar.differs && showLabel && (
        <span className={classes.differs} title="План отличается от Jira">
          ≠
        </span>
      )}
      {!readOnly && <div className={classes.handle} data-handle="right" onPointerDown={(e) => startDrag(e, 'right')} />}
      {drag && (
        <div className={classes.dragTip}>
          {drag.unplan
            ? 'Снять с плана'
            : `${formatRange(drag.start, drag.end)} · ${pluralDays(countWorkingDays(drag.start, drag.end))} раб.`}
        </div>
      )}
    </div>
  )

  if (!bar.card) return element
  return (
    <HoverCard openDelay={450} closeDelay={120} width={320} shadow="md" withinPortal disabled={!!drag} position="top-start">
      <HoverCard.Target>{element}</HoverCard.Target>
      <HoverCard.Dropdown>{bar.card}</HoverCard.Dropdown>
    </HoverCard>
  )
})
