import { useCallback, useEffect, useState } from 'react'
import './App.css'

interface PlayerManifestAsset {
  contentVersionId: string
  position: number
  mediaKind: 'plainText' | 'jpeg' | 'png' | 'webP' | 'mp4'
  durationMilliseconds: number | null
  loopVideo: boolean
  captionContentVersionId?: string | null
  isCaption?: boolean
  url: string
}

export interface PlayerManifest {
  desiredStateId: string
  version: number
  assets: PlayerManifestAsset[]
}

export type PlayerPresentation =
  | { kind: 'notLicensed' }
  | { kind: 'noContent'; deviceName?: string }
  | { kind: 'synchronizing'; progress?: number }
  | { kind: 'ready'; manifest: PlayerManifest; authorizationRemainingMilliseconds?: number }

interface PlayerStateResponse {
  status: 'notLicensed' | 'noContent' | 'synchronizing' | 'ready'
  message: string
  deviceId?: string | null
  desiredStateVersion?: number | null
  authorizationExpiresAtUtc?: string | null
  authorizationRemainingMilliseconds?: number | null
}

const safeDefault: PlayerPresentation = { kind: 'notLicensed' }

export interface AppProps {
  presentation?: PlayerPresentation
}

async function loadPresentation(signal: AbortSignal): Promise<PlayerPresentation> {
  const started = performance.now()
  const response = await fetch('/player/v1/state', {
    signal,
    cache: 'no-store',
    credentials: 'same-origin',
  })
  if (!response.ok) throw new Error('player-state-unavailable')
  const state = (await response.json()) as PlayerStateResponse
  if (state.status === 'noContent') return { kind: 'noContent' }
  if (state.status === 'synchronizing') return { kind: 'synchronizing' }
  if (state.status !== 'ready') return safeDefault

  const manifestResponse = await fetch('/player/v1/manifest', {
    signal,
    cache: 'no-store',
    credentials: 'same-origin',
  })
  if (!manifestResponse.ok) throw new Error('player-manifest-unavailable')
  const manifest = (await manifestResponse.json()) as PlayerManifest
  if (!Array.isArray(manifest.assets) || manifest.assets.length === 0) {
    throw new Error('player-manifest-invalid')
  }

  const remaining = (state.authorizationRemainingMilliseconds ?? 0) - (performance.now() - started)
  if (!state.authorizationExpiresAtUtc || !Number.isFinite(remaining) || remaining <= 0 ||
      !state.authorizationRemainingMilliseconds ||
      state.authorizationRemainingMilliseconds <= 0) {
    return safeDefault
  }

  return {
    kind: 'ready',
    manifest,
    authorizationRemainingMilliseconds: remaining,
  }
}

function PlainTextAsset({ className = 'text-content', url, onFailure, onPlaying }: { className?: string; url: string; onFailure: () => void; onPlaying: () => void }) {
  const [text, setText] = useState('')

  useEffect(() => {
    const abort = new AbortController()
    fetch(url, { cache: 'no-store', credentials: 'same-origin', signal: abort.signal })
      .then((response) => {
        if (!response.ok) throw new Error('text-asset-unavailable')
        return response.text()
      })
      .then((value) => { if (!abort.signal.aborted) { setText(value); onPlaying() } })
      .catch(() => { if (!abort.signal.aborted) onFailure() })
    return () => abort.abort()
  }, [url, onFailure, onPlaying])

  return <pre className={className}>{text}</pre>
}

function PlaylistPlayer({ manifest }: { manifest: PlayerManifest }) {
  const [position, setPosition] = useState(0)
  const [cycle, setCycle] = useState(0)
  const [failed, setFailed] = useState(false)
  const allAssets = [...manifest.assets].sort((left, right) => left.position - right.position)
  const assets = allAssets.filter((item) => !item.isCaption)
  const asset = assets[position % assets.length]
  const advance = useCallback(() => {
    setFailed(false)
    setPosition((current) => (current + 1) % assets.length)
    setCycle((current) => current + 1)
  }, [assets.length])
  const report = useCallback((status: 'playing' | 'error', errorCode: string | null) => {
    void fetch('/player/v1/playback-report', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ desiredStateId: manifest.desiredStateId, version: manifest.version,
        contentVersionId: asset.contentVersionId, status, errorCode }),
    }).catch(() => { /* Heartbeat connectivity is independent of playback. */ })
  }, [manifest.desiredStateId, manifest.version, asset.contentVersionId])
  const fail = useCallback(() => { setFailed(true); report('error', 'media_error') }, [report])
  const playing = useCallback(() => report('playing', null), [report])

  useEffect(() => {
    if (!failed) return
    const retry = window.setTimeout(advance, 5_000)
    return () => window.clearTimeout(retry)
  }, [failed, advance])

  useEffect(() => {
    setPosition(0)
    setCycle(0)
  }, [manifest.desiredStateId, manifest.version])

  useEffect(() => {
    if (!asset.durationMilliseconds) return
    const timeout = window.setTimeout(advance, asset.durationMilliseconds)
    return () => window.clearTimeout(timeout)
  }, [advance, asset.contentVersionId, asset.durationMilliseconds, cycle])

  let media
  if (asset.mediaKind === 'plainText') {
    media = <PlainTextAsset key={`${asset.contentVersionId}-${cycle}`} url={asset.url} onFailure={fail} onPlaying={playing} />
  } else if (asset.mediaKind === 'mp4') {
    media = (
      <video
        key={`${asset.contentVersionId}-${cycle}`}
        className="visual-content"
        src={asset.url}
        autoPlay
        muted
        playsInline
        loop={asset.loopVideo}
        onEnded={asset.loopVideo ? undefined : advance}
        onError={fail}
        onStalled={fail}
        onPlaying={playing}
      />
    )
  } else {
    media = <img key={`${asset.contentVersionId}-${cycle}`} className="visual-content" src={asset.url} alt="" onError={fail} onLoad={playing} />
  }

  const caption = asset.captionContentVersionId
    ? allAssets.find((item) => item.isCaption && item.contentVersionId === asset.captionContentVersionId)
    : undefined
  if (asset.mediaKind === 'mp4' && caption) {
    media = <div className="composite-content">{media}<PlainTextAsset className="caption-content" key={`${caption.contentVersionId}-${cycle}`} url={caption.url} onFailure={fail} onPlaying={playing} /></div>
  }

  return <main className="playback-screen">{failed ? <p role="status">Media unavailable. Retrying…</p> : media}</main>
}

function App({ presentation }: AppProps) {
  const [agentPresentation, setAgentPresentation] = useState<PlayerPresentation>(safeDefault)

  useEffect(() => {
    if (presentation) return

    let active = true
    let poll: number | undefined
    let abort = new AbortController()
    const load = async () => {
      abort = new AbortController()
      const timeout = window.setTimeout(() => abort.abort(), 15_000)
      try {
        const next = await loadPresentation(abort.signal)
        if (active) setAgentPresentation(next)
      } catch {
        if (active) setAgentPresentation(safeDefault)
      } finally {
        window.clearTimeout(timeout)
        // The agent's local state is authoritative; poll it frequently so a completed
        // push-triggered synchronization switches the kiosk without a browser refresh.
        if (active) poll = window.setTimeout(() => void load(), 1_000)
      }
    }

    void load()
    return () => {
      active = false
      abort.abort()
      window.clearTimeout(poll)
    }
  }, [presentation])

  const current = presentation ?? agentPresentation
  const activeAuthorizationRemaining = presentation === undefined && agentPresentation.kind === 'ready'
    ? agentPresentation.authorizationRemainingMilliseconds
    : undefined

  useEffect(() => {
    if (!activeAuthorizationRemaining) return

    const expiryTimer = window.setTimeout(
      () => setAgentPresentation(safeDefault),
      activeAuthorizationRemaining,
    )
    return () => window.clearTimeout(expiryTimer)
  }, [activeAuthorizationRemaining, agentPresentation])

  if (current.kind === 'ready') {
    return <PlaylistPlayer manifest={current.manifest} />
  }

  if (current.kind === 'noContent') {
    return (
      <main className="player-screen neutral" aria-live="polite">
        <div className="state-mark" aria-hidden="true">—</div>
        <h1>No content assigned</h1>
        {current.deviceName && <p>{current.deviceName}</p>}
      </main>
    )
  }

  if (current.kind === 'synchronizing') {
    const progress = Math.max(0, Math.min(100, current.progress ?? 0))
    return (
      <main className="player-screen neutral" aria-live="polite">
        <div className="sync-ring" aria-hidden="true" />
        <h1>Synchronizing</h1>
        <p>{progress}%</p>
      </main>
    )
  }

  return (
    <main className="player-screen denied" aria-live="assertive">
      <div className="state-mark" aria-hidden="true">×</div>
      <h1>Not licensed</h1>
    </main>
  )
}

export default App
