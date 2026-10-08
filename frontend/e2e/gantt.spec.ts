import { expect, test, type Locator, type Page } from '@playwright/test'
import { IVANOV, mockApi, PETROV, PROJECT_ID } from './fixtures'

const DAY = 40 // масштаб «День»

async function open(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('gantt.scale', 'day')
    localStorage.setItem('gantt.panelCollapsed', '0')
  })
  const saved = await mockApi(page)
  await page.goto(`/projects/${PROJECT_ID}/gantt`)
  await expect(page.getByTestId('bar-PRJ-10')).toBeVisible()
  return saved
}

async function center(locator: Locator) {
  const box = (await locator.boundingBox())!
  return { x: box.x + box.width / 2, y: box.y + box.height / 2, box }
}

async function drag(page: Page, from: { x: number; y: number }, to: { x: number; y: number }) {
  await page.mouse.move(from.x, from.y)
  await page.mouse.down()
  await page.mouse.move(to.x, to.y, { steps: 12 })
  await page.mouse.up()
}

async function save(page: Page) {
  await page.getByTestId('save').click()
  await expect(page.getByText(/Сохранено задач/)).toBeVisible()
}

test('moving a bar shifts both dates and saves them', async ({ page }) => {
  const saved = await open(page)
  const bar = page.getByTestId('bar-PRJ-10')
  const c = await center(bar)

  await drag(page, c, { x: c.x + 2 * DAY, y: c.y })

  await expect(bar).toHaveAttribute('data-start', String(Date.UTC(2026, 9, 7) / 86_400_000))
  await expect(page.getByTestId('save')).toContainText('1')
  await save(page)
  expect(saved).toEqual([[{ issueId: 'i1', startDate: '2026-10-07', endDate: '2026-10-08', assigneeId: IVANOV, version: 1 }]])
})

test('dragging the right edge changes duration only', async ({ page }) => {
  const saved = await open(page)
  const { box } = await center(page.getByTestId('bar-PRJ-10'))

  await drag(page, { x: box.x + box.width - 2, y: box.y + box.height / 2 }, { x: box.x + box.width - 2 + 3 * DAY, y: box.y + box.height / 2 })
  await save(page)

  expect(saved[0]).toEqual([{ issueId: 'i1', startDate: '2026-10-05', endDate: '2026-10-09', assigneeId: IVANOV, version: 1 }])
})

test('dragging a bar to another row reassigns the task', async ({ page }) => {
  const saved = await open(page)
  const c = await center(page.getByTestId('bar-PRJ-10'))
  const petrovRow = await center(page.getByTestId(`row-${PETROV}`))

  await drag(page, c, { x: c.x, y: petrovRow.y })
  await save(page)

  expect(saved[0]).toEqual([{ issueId: 'i1', startDate: '2026-10-05', endDate: '2026-10-06', assigneeId: PETROV, version: 1 }])
})

test('Escape cancels a drag', async ({ page }) => {
  await open(page)
  const bar = page.getByTestId('bar-PRJ-10')
  const c = await center(bar)
  const startBefore = await bar.getAttribute('data-start')

  await page.mouse.move(c.x, c.y)
  await page.mouse.down()
  await page.mouse.move(c.x + 3 * DAY, c.y, { steps: 8 })
  await page.keyboard.press('Escape')
  await page.mouse.up()

  await expect(bar).toHaveAttribute('data-start', startBefore!)
  await expect(page.getByTestId('save')).toBeDisabled()
})

test('dropping a task from the list onto a member schedules it by estimate', async ({ page }) => {
  const saved = await open(page)
  const card = await center(page.getByTestId('card-PRJ-11'))
  const ivanovRow = await center(page.getByTestId(`row-${IVANOV}`))
  // Целимся в понедельник 12.10: полоса PRJ-10 начинается 05.10 → +7 дней.
  const ref = await center(page.getByTestId('bar-PRJ-10'))
  const targetX = ref.box.x + 7 * DAY + DAY / 2

  await drag(page, card, { x: targetX, y: ivanovRow.y })

  await expect(page.getByTestId('bar-PRJ-11')).toBeVisible()
  await save(page)
  // 24 ч при 8 ч в дне = 3 рабочих дня: 12–14.10.
  expect(saved[0]).toEqual([{ issueId: 'i2', startDate: '2026-10-12', endDate: '2026-10-14', assigneeId: IVANOV, version: 1 }])
})

test('undo and redo via keyboard', async ({ page }) => {
  await open(page)
  const bar = page.getByTestId('bar-PRJ-10')
  const c = await center(bar)
  const before = await bar.getAttribute('data-start')

  await drag(page, c, { x: c.x + DAY, y: c.y })
  await expect(bar).not.toHaveAttribute('data-start', before!)

  await page.keyboard.press('Control+z')
  await expect(bar).toHaveAttribute('data-start', before!)
  await page.keyboard.press('Control+Shift+z')
  await expect(bar).not.toHaveAttribute('data-start', before!)
})
