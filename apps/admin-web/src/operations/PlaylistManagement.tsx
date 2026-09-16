import { useState } from 'react'
import { apiRequest } from '../api'
import { Badge } from './presentation'
import type { Playlist, PlaylistItem, PostJson } from './types'

export default function PlaylistManagement({ playlists, tenantId, post, onChanged }: {
  playlists: Playlist[]
  tenantId: string
  post: PostJson
  onChanged: () => Promise<void>
}) {
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [items, setItems] = useState<PlaylistItem[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const toggleItems = async (playlist: Playlist) => {
    if (expandedId === playlist.id) {
      setExpandedId(null)
      return
    }
    setBusy(true); setError(null)
    try {
      const loaded = await apiRequest<PlaylistItem[]>(`/api/v1/tenants/${tenantId}/playlists/${playlist.id}/items`)
      setItems(loaded); setExpandedId(playlist.id)
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Chargement impossible.')
    } finally {
      setBusy(false)
    }
  }

  const removeItem = async (playlist: Playlist, item: PlaylistItem) => {
    if (!window.confirm(`Retirer « ${item.title} » de la playlist « ${playlist.name} » ?`)) return
    setBusy(true); setError(null)
    try {
      await post(`/api/v1/tenants/${tenantId}/playlists/${playlist.id}/items/${item.id}/remove`, {
        concurrencyToken: playlist.concurrencyToken,
        reason: 'Retrait depuis le tableau de bord',
      })
      setItems((current) => current.filter((value) => value.id !== item.id))
      await onChanged()
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Retrait impossible.')
    } finally {
      setBusy(false)
    }
  }

  const archive = async (playlist: Playlist) => {
    if (!window.confirm(`Supprimer la playlist « ${playlist.name} » ?`)) return
    setBusy(true); setError(null)
    try {
      await post(`/api/v1/tenants/${tenantId}/playlists/${playlist.id}/archive`, {
        concurrencyToken: playlist.concurrencyToken,
        reason: 'Suppression depuis le tableau de bord',
      })
      if (expandedId === playlist.id) setExpandedId(null)
      await onChanged()
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Suppression impossible.')
    } finally {
      setBusy(false)
    }
  }

  const publish = async (playlist: Playlist) => {
    if (!playlist.latestVersion) return
    setBusy(true); setError(null)
    try {
      await post(`/api/v1/tenants/${tenantId}/playlists/${playlist.id}/versions/${playlist.latestVersion.id}/publish`, {})
      await onChanged()
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Publication impossible.')
    } finally {
      setBusy(false)
    }
  }

  return <section className="collection-surface" aria-labelledby="saved-playlists-title">
    <div className="collection-heading"><div><p className="eyebrow">Catalogue</p><h3 id="saved-playlists-title">Playlists enregistrées</h3></div><span>{playlists.length}</span></div>
    {error && <p className="form-error" role="alert">{error}</p>}
    <div className="card-list playlist-card-list">{playlists.map((playlist) => <article key={playlist.id}>
      <div><strong>{playlist.name}</strong><small>{playlist.latestVersion?.itemCount ?? 0} élément(s)</small></div>
      <Badge value={playlist.latestVersion?.publicationState ?? 'vide'} />
      <div className="playlist-actions">
        {playlist.latestVersion?.publicationState === 'draft' && <button className="text-button" type="button" disabled={busy} onClick={() => void publish(playlist)}>Publier</button>}
        <button className="text-button" type="button" disabled={busy} aria-expanded={expandedId === playlist.id} onClick={() => void toggleItems(playlist)}>{expandedId === playlist.id ? 'Fermer' : 'Gérer les éléments'}</button>
        <button className="text-button danger-button" type="button" disabled={busy} onClick={() => void archive(playlist)}>Supprimer</button>
      </div>
      {expandedId === playlist.id && <div className="playlist-items">
        {items.map((item, index) => <div className="playlist-item-row" key={item.id}>
          <span><strong>{index + 1}. {item.title}</strong><small>{item.mediaKind}{item.durationMilliseconds ? ` · ${item.durationMilliseconds} ms` : ''}</small></span>
          <button className="text-button danger-button" type="button" disabled={busy || items.length === 1} title={items.length === 1 ? 'Supprimez la playlist pour retirer son dernier élément.' : undefined} onClick={() => void removeItem(playlist, item)}>Retirer</button>
        </div>)}
      </div>}
    </article>)}</div>
    {playlists.length === 0 && <p className="empty-state">Aucune playlist enregistrée.</p>}
  </section>
}
