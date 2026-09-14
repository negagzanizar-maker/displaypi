export class ApiError extends Error {
  readonly status: number
  constructor(message: string, status: number) { super(message); this.status = status }
}

export async function readProblem(response: Response): Promise<string> {
  if (response.status === 401) window.dispatchEvent(new Event('session-expired'))
  try {
    const problem = await response.json() as { title?: string }
    return problem.title ?? `Requête refusée (${response.status}).`
  } catch { return `Requête refusée (${response.status}).` }
}

export async function apiRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(path, { credentials: 'same-origin', ...init,
    headers: { Accept: 'application/json', ...init.headers } })
  if (!response.ok) throw new ApiError(await readProblem(response), response.status)
  return response.status === 204 ? undefined as T : await response.json() as T
}

// Lists retain their array contract; the server supplies an opaque continuation header.
export async function loadList<T>(path: string, signal?: AbortSignal): Promise<T[]> {
  const items: T[] = []
  const seen = new Set<string>()
  let cursor: string | null = null
  for (let page = 0; page < 1000; page++) {
    const url = new URL(path, window.location.origin)
    if (cursor) url.searchParams.set('cursor', cursor)
    const response = await fetch(url.pathname + url.search, { signal, credentials: 'same-origin', headers: { Accept: 'application/json' } })
    if (!response.ok) throw new ApiError(await readProblem(response), response.status)
    const batch: unknown = await response.json()
    if (!Array.isArray(batch)) throw new Error('Réponse de liste invalide.')
    items.push(...batch as T[])
    cursor = response.headers.get('X-Next-Cursor')
    if (!cursor) return items
    if (seen.has(cursor)) throw new Error('La pagination ne progresse pas.')
    seen.add(cursor)
  }
  throw new Error('Catalogue trop volumineux pour cette vue. Affinez la recherche.')
}
