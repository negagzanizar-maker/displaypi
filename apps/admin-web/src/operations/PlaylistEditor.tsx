import { useState } from 'react'
import type { ContentItem, Device, DeviceGroup, Playlist, PostJson } from './types'

type Item = { key: number; contentVersionId: string; captionText: string; durationMilliseconds: number; loopVideo: boolean }
const emptyItem = (key: number): Item => ({ key, contentVersionId: '', captionText: '', durationMilliseconds: 10000, loopVideo: false })

export default function PlaylistEditor({ contents, devices, groups, tenantId, post, onCreated }: {
  contents: ContentItem[]; devices: Device[]; groups: DeviceGroup[]; tenantId: string; post: PostJson; onCreated: (playlist: Playlist) => void
}) {
  const [items, setItems] = useState<Item[]>([emptyItem(0)])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [createdPlaylist, setCreatedPlaylist] = useState<Playlist | null>(null)
  const [target, setTarget] = useState('')
  const [notice, setNotice] = useState<string | null>(null)
  const change = (key: number, patch: Partial<Item>) => setItems((current) => current.map((item) => item.key === key ? { ...item, ...patch } : item))
  const move = (index: number, offset: -1 | 1) => setItems((current) => { const next = [...current]; [next[index], next[index + offset]] = [next[index + offset], next[index]]; return next })

  const displayPlaylist = async () => {
    const versionId = createdPlaylist?.latestVersion?.id
    const [targetKind, targetId] = target.split(':', 2)
    if (!versionId || !targetId || !['device', 'group'].includes(targetKind)) return
    setBusy(true); setError(null); setNotice(null)
    try {
      const targetPath = targetKind === 'device' ? `devices/${targetId}` : `device-groups/${targetId}`
      await post(`/api/v1/tenants/${tenantId}/${targetPath}/assignments`, { playlistVersionId: versionId, priority: 0, overrideEqualPriority: true, startsAtUtc: null, endsAtUtc: null, presentationTimeZone: 'UTC' })
      setNotice(targetKind === 'device' ? 'Playlist envoyée à l’écran.' : 'Playlist envoyée au groupe.')
      setCreatedPlaylist(null); setTarget('')
    } catch (reason: unknown) { setError(reason instanceof Error ? reason.message : 'Publication impossible.') } finally { setBusy(false) }
  }

  return <form className="stack-form playlist-editor" onSubmit={(event) => {
    event.preventDefault(); const form = event.currentTarget; setBusy(true); setError(null); setNotice(null)
    void post<Playlist>(`/api/v1/tenants/${tenantId}/playlists`, {
      name: String(new FormData(form).get('name')), description: null, publishImmediately: true,
      items: items.map(({ contentVersionId, captionText, durationMilliseconds, loopVideo }) => ({ contentVersionId, captionContentVersionId: null, captionText: captionText.trim() || null, durationMilliseconds, loopVideo })),
    }).then((created) => { setCreatedPlaylist(created); setTarget(''); onCreated(created); form.reset(); setItems([emptyItem(0)]) })
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : 'Création impossible.')).finally(() => setBusy(false))
  }}><h3>Créer une playlist</h3>
    <p>Ajoutez les contenus dans l’ordre voulu. La playlist sera directement prête à afficher.</p>
    {error && <p className="form-error" role="alert">{error}</p>}{notice && <p className="form-notice" role="status">{notice}</p>}
    <label>Nom<input name="name" maxLength={200} required disabled={busy} /></label>
    <div className="playlist-editor-items">{items.map((item, index) => {
      const content = contents.find((value) => value.latestVersion?.id === item.contentVersionId)
      const mediaKind = content?.mediaKind.toLowerCase() ?? ''
      const previewUrl = content?.latestVersion ? `/api/v1/tenants/${tenantId}/contents/${content.id}/versions/${content.latestVersion.id}/preview` : ''
      return <fieldset className="playlist-editor-item" key={item.key} disabled={busy}><legend>#{index + 1}</legend>
        <div className="playlist-item-preview" aria-hidden="true">{content && ['png', 'jpeg', 'webp'].includes(mediaKind) && <img src={previewUrl} alt="" />}{content && mediaKind === 'mp4' && <video src={previewUrl} muted />}{(!content || !['png', 'jpeg', 'webp', 'mp4'].includes(mediaKind)) && <span>{content ? 'TXT' : '—'}</span>}</div>
        <div className="playlist-item-fields"><label>Contenu<select aria-label={`Contenu de l’élément ${index + 1}`} required value={item.contentVersionId} onChange={(event) => change(item.key, { contentVersionId: event.target.value, captionText: '', loopVideo: false })}>
          <option value="">Sélectionner</option>{contents.filter((value) => value.latestVersion?.id === item.contentVersionId || !items.some((other) => other.contentVersionId === value.latestVersion?.id)).map((value) => <option key={value.id} value={value.latestVersion?.id}>{value.title}</option>)}
        </select></label><span className="playlist-item-meta">{content ? `${content.title} · ${content.mediaKind} · ${(item.durationMilliseconds / 1000).toLocaleString('fr-FR')} s` : 'Choisissez un contenu'}</span>
        <label>Durée (ms)<input type="number" min={1000} max={86400000} value={item.durationMilliseconds} required onChange={(event) => change(item.key, { durationMilliseconds: Number(event.target.value) })} /></label>
        {item.contentVersionId && mediaKind !== 'plaintext' && <label className="playlist-caption">Texte sous le contenu (facultatif)<textarea maxLength={1000} rows={2} value={item.captionText} onChange={(event) => change(item.key, { captionText: event.target.value })} /></label>}
        {mediaKind === 'mp4' && <label className="playlist-loop"><input type="checkbox" checked={item.loopVideo} onChange={(event) => change(item.key, { loopVideo: event.target.checked })} />Répéter la vidéo</label>}</div>
        <div className="playlist-item-controls"><button type="button" aria-label={`Monter l’élément ${index + 1}`} disabled={index === 0} onClick={() => move(index, -1)}>↑</button><button type="button" aria-label={`Descendre l’élément ${index + 1}`} disabled={index === items.length - 1} onClick={() => move(index, 1)}>↓</button><button type="button" className="danger-button" aria-label={`Retirer l’élément ${index + 1}`} disabled={items.length === 1} onClick={() => setItems((current) => current.filter((other) => other.key !== item.key))}>×</button></div>
      </fieldset>
    })}</div>
    <div className="playlist-editor-actions"><button type="button" disabled={busy || items.length >= contents.length || items.length >= 100} onClick={() => setItems((current) => [...current, emptyItem(Math.max(...current.map((item) => item.key)) + 1)])}>Ajouter un élément</button><button className="primary-button" disabled={busy || contents.length === 0} type="submit">Enregistrer et publier</button></div>
    {createdPlaylist?.latestVersion && <section className="playlist-display-action" aria-labelledby="display-playlist-title"><div><strong id="display-playlist-title">Playlist enregistrée</strong><small>{createdPlaylist.name} est prête à être affichée.</small></div><label>Destination<select aria-label="Destination de la playlist" value={target} onChange={(event) => setTarget(event.target.value)}><option value="">Sélectionner</option>{devices.length > 0 && <optgroup label="Écrans">{devices.map((device) => <option key={device.id} value={`device:${device.id}`}>{device.displayName}</option>)}</optgroup>}{groups.length > 0 && <optgroup label="Groupes">{groups.map((group) => <option key={group.id} value={`group:${group.id}`}>{group.name}</option>)}</optgroup>}</select></label><button className="primary-button" type="button" disabled={busy || !target} onClick={() => void displayPlaylist()}>Afficher cette playlist</button></section>}
  </form>
}
