import { useState } from 'react'
import type { ContentItem, Playlist, PostJson } from './types'

type Item = { key: number; contentVersionId: string; captionContentVersionId: string; durationMilliseconds: number; loopVideo: boolean }

export default function PlaylistEditor({ contents, tenantId, post, onCreated }: {
  contents: ContentItem[]; tenantId: string; post: PostJson; onCreated: (playlist: Playlist) => void
}) {
  const [items, setItems] = useState<Item[]>([{ key: 0, contentVersionId: '', captionContentVersionId: '', durationMilliseconds: 10000, loopVideo: false }])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const change = (key: number, patch: Partial<Item>) => setItems((current) => current.map((item) => item.key === key ? { ...item, ...patch } : item))
  return <form className="stack-form" onSubmit={(event) => {
    event.preventDefault()
    const form = event.currentTarget
    setBusy(true); setError(null)
    void post<Playlist>(`/api/v1/tenants/${tenantId}/playlists`, {
      name: String(new FormData(form).get('name')), description: null,
      publishImmediately: true,
      items: items.map(({ contentVersionId, captionContentVersionId, durationMilliseconds, loopVideo }) => ({ contentVersionId, captionContentVersionId: captionContentVersionId || null, durationMilliseconds, loopVideo })),
    }).then((created) => {
      onCreated(created); form.reset()
      setItems([{ key: 0, contentVersionId: '', captionContentVersionId: '', durationMilliseconds: 10000, loopVideo: false }])
    }).catch((reason: unknown) => setError(reason instanceof Error ? reason.message : 'Création impossible.'))
      .finally(() => setBusy(false))
  }}><h3>Créer une playlist</h3>
    <p>Ajoutez les contenus dans l’ordre voulu. La playlist sera directement prête à afficher.</p>
    {error && <p role="alert">{error}</p>}
    <label>Nom<input name="name" maxLength={200} required disabled={busy} /></label>
    {items.map((item, index) => <fieldset key={item.key} disabled={busy}><legend>Élément {index + 1}</legend>
      <label>Contenu<select required value={item.contentVersionId} onChange={(event) => change(item.key, { contentVersionId: event.target.value, captionContentVersionId: '', loopVideo: false })}>
        <option value="">Sélectionner</option>{contents.filter((content) => content.latestVersion?.id === item.contentVersionId || !items.some((other) => other.contentVersionId === content.latestVersion?.id)).map((content) => <option key={content.id} value={content.latestVersion?.id}>{content.title}</option>)}
      </select></label>
      {contents.find((content) => content.latestVersion?.id === item.contentVersionId)?.mediaKind.toLowerCase() === 'mp4' && <label>Texte sous la vidéo (facultatif)<select value={item.captionContentVersionId} onChange={(event) => change(item.key, { captionContentVersionId: event.target.value })}><option value="">Aucun</option>{contents.filter((content) => content.mediaKind.toLowerCase() === 'plaintext' && content.latestVersion?.id !== item.contentVersionId).map((content) => <option key={content.id} value={content.latestVersion?.id}>{content.title}</option>)}</select></label>}
      <label>Durée (ms)<input type="number" min={1000} max={86400000} value={item.durationMilliseconds} required onChange={(event) => change(item.key, { durationMilliseconds: Number(event.target.value) })} /></label>
      <label><input type="checkbox" checked={item.loopVideo} disabled={contents.find((content) => content.latestVersion?.id === item.contentVersionId)?.mediaKind !== 'mp4'} onChange={(event) => change(item.key, { loopVideo: event.target.checked })} />Répéter la vidéo pendant cette durée</label>
      <button type="button" disabled={index === 0} onClick={() => setItems((current) => { const next = [...current]; [next[index - 1], next[index]] = [next[index], next[index - 1]]; return next })}>Monter</button>
      <button type="button" disabled={items.length === 1} onClick={() => setItems((current) => current.filter((other) => other.key !== item.key))}>Retirer</button>
    </fieldset>)}
    <button type="button" disabled={busy || items.length >= contents.length || items.length >= 100} onClick={() => setItems((current) => [...current, { key: Math.max(...current.map((item) => item.key)) + 1, contentVersionId: '', captionContentVersionId: '', durationMilliseconds: 10000, loopVideo: false }])}>Ajouter un élément</button>
    <button className="primary-button" disabled={busy || contents.length === 0} type="submit">Enregistrer et publier</button>
  </form>
}
