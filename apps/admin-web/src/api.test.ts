import { afterEach, describe, expect, it, vi } from 'vitest'
import { loadList } from './api'
import { optionalUtc } from './operations/formatters'

afterEach(() => vi.unstubAllGlobals())

describe('catalogue continuation', () => {
  it('includes later pages and preserves existing query filters', async () => {
    const request = vi.fn()
      .mockResolvedValueOnce(new Response('[1]', { headers: { 'X-Next-Cursor': 'a+b=' } }))
      .mockResolvedValueOnce(new Response('[2]'))
    vi.stubGlobal('fetch', request)
    expect(await loadList('/api/items?limit=1')).toEqual([1, 2])
    expect(request.mock.calls[1][0]).toBe('/api/items?limit=1&cursor=a%2Bb%3D')
  })

  it('rejects a looping continuation instead of displaying partial totals', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(new Response('[]', { headers: { 'X-Next-Cursor': 'same' } }))))
    await expect(loadList('/api/items')).rejects.toThrow('La pagination ne progresse pas.')
  })

  it('signals session expiry on unauthorized catalogue requests', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 401 })))
    const expired = vi.fn()
    window.addEventListener('session-expired', expired)
    try { await expect(loadList('/api/items')).rejects.toThrow(); expect(expired).toHaveBeenCalledOnce() }
    finally { window.removeEventListener('session-expired', expired) }
  })
})

describe('explicit UTC scheduling fields', () => {
  it('preserves the entered hour and rejects calendar normalization', () => {
    expect(optionalUtc('2026-09-06T10:00')).toBe('2026-09-06T10:00:00.000Z')
    expect(() => optionalUtc('2026-02-30T10:00')).toThrow('Date UTC invalide.')
  })
})
