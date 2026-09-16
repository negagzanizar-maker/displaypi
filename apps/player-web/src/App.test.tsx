import { act, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('fail-closed player presentation', () => {
  it('keeps a slow state request singular and aborts it when the player unmounts', async () => {
    vi.useFakeTimers()
    const fetchMock = vi.fn().mockImplementation(() => new Promise<Response>(() => {}))
    vi.stubGlobal('fetch', fetchMock)
    const view = render(<App />)
    await act(async () => vi.advanceTimersByTime(10_000))
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const signal = fetchMock.mock.calls[0][1].signal as AbortSignal
    expect(signal.aborted).toBe(false)
    view.unmount()
    expect(signal.aborted).toBe(true)
  })

  it('shows the exact unlicensed message by default', () => {
    render(<App />)

    expect(screen.getByRole('heading', { name: 'Not licensed' })).toBeInTheDocument()
    expect(screen.queryByText('No content assigned')).not.toBeInTheDocument()
  })

  it('distinguishes a valid licence with no assigned content', () => {
    render(<App presentation={{ kind: 'noContent', deviceName: 'Lobby display' }} />)

    expect(screen.getByRole('heading', { name: 'No content assigned' })).toBeInTheDocument()
    expect(screen.queryByText('Not licensed')).not.toBeInTheDocument()
  })

  it('renders an approved cached image without exposing a filesystem path', () => {
    render(<App presentation={{
      kind: 'ready',
      manifest: {
        desiredStateId: '11111111-1111-1111-1111-111111111111',
        version: 3,
        assets: [{
          contentVersionId: '22222222-2222-2222-2222-222222222222',
          position: 0,
          mediaKind: 'png',
          durationMilliseconds: 5000,
          loopVideo: false,
          url: '/player/v1/assets/22222222-2222-2222-2222-222222222222',
        }],
      },
    }} />)

    const image = document.querySelector('img')
    expect(image).not.toBeNull()
    expect(image).toHaveAttribute(
      'src',
      '/player/v1/assets/22222222-2222-2222-2222-222222222222',
    )
  })

  it('renders an approved text caption over a looping video', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('Welcome to the lobby', { status: 200 })))
    render(<App presentation={{
      kind: 'ready',
      manifest: {
        desiredStateId: '11111111-1111-1111-1111-111111111111',
        version: 4,
        assets: [{
          contentVersionId: '22222222-2222-2222-2222-222222222222',
          position: 0,
          mediaKind: 'mp4',
          durationMilliseconds: 10_000,
          loopVideo: true,
          captionContentVersionId: '33333333-3333-3333-3333-333333333333',
          url: '/player/v1/assets/22222222-2222-2222-2222-222222222222',
        }, {
          contentVersionId: '33333333-3333-3333-3333-333333333333',
          position: 1,
          mediaKind: 'plainText',
          durationMilliseconds: 1_000,
          loopVideo: false,
          isCaption: true,
          url: '/player/v1/assets/33333333-3333-3333-3333-333333333333',
        }],
      },
    }} />)

    expect(document.querySelector('video')).toHaveAttribute('loop')
    expect((await screen.findByText('Welcome to the lobby')).closest('.caption-content')).not.toBeNull()
  })

  it('renders inline caption text over an image without downloading a second asset', () => {
    render(<App presentation={{
      kind: 'ready',
      manifest: {
        desiredStateId: '11111111-1111-1111-1111-111111111111',
        version: 5,
        assets: [{
          contentVersionId: '22222222-2222-2222-2222-222222222222',
          position: 0,
          mediaKind: 'jpeg',
          durationMilliseconds: 10_000,
          loopVideo: false,
          captionText: 'Bienvenue au bureau',
          url: '/player/v1/assets/22222222-2222-2222-2222-222222222222',
        }],
      },
    }} />)

    expect(document.querySelector('img')).not.toBeNull()
    expect(screen.getByText('Bienvenue au bureau').closest('.caption-content')).not.toBeNull()
  })

  it('stops an active presentation at the lease expiry without waiting for a heartbeat', async () => {
    vi.useFakeTimers()
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({
        status: 'ready',
        message: 'Playing',
        authorizationExpiresAtUtc: new Date(Date.now() + 1_000).toISOString(),
        authorizationRemainingMilliseconds: 1_000,
      }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        desiredStateId: '11111111-1111-1111-1111-111111111111',
        version: 3,
        assets: [{
          contentVersionId: '22222222-2222-2222-2222-222222222222',
          position: 0,
          mediaKind: 'png',
          durationMilliseconds: 5_000,
          loopVideo: false,
          url: '/player/v1/assets/22222222-2222-2222-2222-222222222222',
        }],
      }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)
    await act(async () => Promise.resolve())
    await act(async () => Promise.resolve())
    expect(document.querySelector('img')).not.toBeNull()

    act(() => vi.advanceTimersByTime(1_000))

    expect(screen.getByRole('heading', { name: 'Not licensed' })).toBeInTheDocument()
  })

  it('switches to newly synchronized local state without a browser refresh', async () => {
    vi.useFakeTimers()
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({
        status: 'noContent',
        message: 'No content assigned',
      }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        status: 'ready',
        message: 'Playing',
        authorizationExpiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
        authorizationRemainingMilliseconds: 60_000,
      }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        desiredStateId: '11111111-1111-1111-1111-111111111111',
        version: 4,
        assets: [{
          contentVersionId: '22222222-2222-2222-2222-222222222222',
          position: 0,
          mediaKind: 'png',
          durationMilliseconds: 5_000,
          loopVideo: false,
          url: '/player/v1/assets/22222222-2222-2222-2222-222222222222',
        }],
      }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    render(<App />)
    await act(async () => Promise.resolve())
    expect(screen.getByRole('heading', { name: 'No content assigned' })).toBeInTheDocument()

    await act(async () => vi.advanceTimersByTimeAsync(1_000))
    await act(async () => Promise.resolve())

    expect(document.querySelector('img')).not.toBeNull()
    expect(fetchMock).toHaveBeenCalledTimes(3)
  })
})
