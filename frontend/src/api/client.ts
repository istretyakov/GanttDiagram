/** Ответ сервера в формате ProblemDetails. */
export type Problem = {
  title?: string
  detail?: string
  status?: number
  errors?: Record<string, string[]>
  issueIds?: string[]
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: Problem

  constructor(status: number, problem: Problem) {
    super(problem.detail ?? problem.title ?? `Ошибка ${status}`)
    this.status = status
    this.problem = problem
  }
}

type Query = Record<string, string | number | boolean | null | undefined>

export function withQuery(url: string, query?: Query): string {
  if (!query) return url
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value))
  }
  const qs = params.toString()
  return qs ? `${url}?${qs}` : url
}

export async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, {
      method,
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiError(0, { title: 'Сервер недоступен', detail: 'Не удалось связаться с сервером приложения.' })
  }

  if (!response.ok) {
    let problem: Problem = { status: response.status, title: response.statusText }
    try {
      problem = { ...problem, ...(await response.json()) }
    } catch {
      // тело не JSON
    }
    throw new ApiError(response.status, problem)
  }

  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const http = {
  get: <T>(url: string, query?: Query) => request<T>('GET', withQuery(url, query)),
  post: <T>(url: string, body: unknown = {}) => request<T>('POST', url, body),
  put: <T>(url: string, body: unknown) => request<T>('PUT', url, body),
  delete: <T = void>(url: string) => request<T>('DELETE', url),
}

/** Текст ошибки для уведомления: detail + ошибки по полям. */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    const fieldErrors = Object.values(error.problem.errors ?? {}).flat()
    return [error.message, ...fieldErrors].filter(Boolean).join('\n')
  }
  return error instanceof Error ? error.message : String(error)
}
