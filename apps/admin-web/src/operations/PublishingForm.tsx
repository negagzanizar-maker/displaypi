import { useEffect, useState, type FormEvent } from 'react'
import type { ContentItem, Device, DeviceGroup, Playlist, PostJson } from './types'

export default function PublishingForm({ contents, devices, groups, tenantId, post, onPublished }: {
  contents: ContentItem[]
  devices: Device[]
  groups: DeviceGroup[]
  tenantId: string
  post: PostJson
  onPublished: () => Promise<void>
}) {
  const [selectedVersionId, setSelectedVersionId] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const selectedContent = contents.find((content) => content.latestVersion?.id === selectedVersionId) ?? null

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = event.currentTarget
    const values = new FormData(form)
    setBusy(true); setError(null); setNotice(null)
    try {
      if (!selectedContent?.latestVersion) throw new Error('Sélectionnez un contenu utilisable.')
      const created = await post<Playlist>(`/api/v1/tenants/${tenantId}/playlists`, {
        name: `Publication – ${selectedContent.title}`,
        description: 'Publication directe depuis l’administration',
        publishImmediately: true,
        items: [{
          contentVersionId: selectedContent.latestVersion.id,
          durationMilliseconds: 10_000,
          loopVideo: selectedContent.mediaKind.toLowerCase() === 'mp4',
          captionContentVersionId: null,
        }],
      })
      if (created.latestVersion?.publicationState !== 'published') {
        throw new Error('Le contenu n’a pas pu être publié directement.')
      }

      const [targetKind, targetId] = String(values.get('target')).split(':', 2)
      if (!targetId || !['device', 'group'].includes(targetKind)) throw new Error('Sélectionnez une destination.')
      const targetPath = targetKind === 'device' ? `devices/${targetId}` : `device-groups/${targetId}`
      await post(`/api/v1/tenants/${tenantId}/${targetPath}/assignments`, {
        playlistVersionId: created.latestVersion.id,
        priority: 0,
        startsAtUtc: null,
        endsAtUtc: null,
        presentationTimeZone: 'UTC',
      })
      form.reset(); setSelectedVersionId('')
      setNotice(targetKind === 'device' ? 'Contenu envoyé à l’écran.' : 'Contenu envoyé au groupe.')
      await onPublished()
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Publication impossible.')
    } finally {
      setBusy(false)
    }
  }

  return <form className="stack-form publishing-form" onSubmit={(event) => void submit(event)}>
    <h3>Afficher maintenant</h3>
    <p>Choisissez le contenu et l’écran. Le nouvel affichage est envoyé directement.</p>
    {error && <p className="form-error" role="alert">{error}</p>}{notice && <p className="form-notice" role="status">{notice}</p>}
    <label>Contenu<select name="content" required value={selectedVersionId} onChange={(event) => setSelectedVersionId(event.target.value)}><option value="">Sélectionner</option>{contents.map((content) => <option key={content.id} value={content.latestVersion?.id}>{content.title}</option>)}</select></label>
    <CompactContentPreview content={selectedContent} tenantId={tenantId} />
    <label>Destination<select name="target" required><option value="">Sélectionner</option>{devices.length > 0 && <optgroup label="Écrans">{devices.map((device) => <option key={device.id} value={`device:${device.id}`}>{device.displayName}</option>)}</optgroup>}{groups.length > 0 && <optgroup label="Groupes">{groups.map((group) => <option key={group.id} value={`group:${group.id}`}>{group.name}</option>)}</optgroup>}</select></label>
    <button className="primary-button" disabled={busy || contents.length === 0 || devices.length + groups.length === 0} type="submit">{busy ? 'Envoi…' : 'Afficher maintenant'}</button>
  </form>
}

function CompactContentPreview({ content, tenantId }: { content: ContentItem | null; tenantId: string }) {
  const [text, setText] = useState('')
  const [error, setError] = useState<string | null>(null)
  const mediaKind = content?.mediaKind.toLowerCase() ?? ''
  const previewUrl = content?.latestVersion
    ? `/api/v1/tenants/${tenantId}/contents/${content.id}/versions/${content.latestVersion.id}/preview`
    : ''

  useEffect(() => {
    setText(''); setError(null)
    if (!previewUrl || mediaKind !== 'plaintext') return
    const abort = new AbortController()
    void fetch(previewUrl, { credentials: 'same-origin', cache: 'no-store', signal: abort.signal })
      .then((response) => { if (!response.ok) throw new Error('Aperçu indisponible.'); return response.text() })
      .then(setText)
      .catch((reason: unknown) => { if (!abort.signal.aborted) setError(reason instanceof Error ? reason.message : 'Aperçu indisponible.') })
    return () => abort.abort()
  }, [mediaKind, previewUrl])

  return <div className={`publishing-preview ${content ? '' : 'publishing-preview-empty'}`} aria-live="polite">
    {!content && <span>Le contenu sélectionné apparaîtra ici.</span>}
    {content && <>
      <strong>{content.title}</strong>
      <div className="publishing-preview-stage">
        {error && <span>{error}</span>}
        {!error && mediaKind === 'mp4' && <video src={previewUrl} controls muted playsInline />}
        {!error && ['jpeg', 'png', 'webp'].includes(mediaKind) && <img src={previewUrl} alt={`Aperçu de ${content.title}`} />}
        {!error && mediaKind === 'plaintext' && <p>{text || 'Chargement de l’aperçu…'}</p>}
      </div>
    </>}
  </div>
}
