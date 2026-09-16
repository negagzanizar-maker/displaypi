import { useEffect, useState } from 'react'
import type { ContentItem } from './types'
import { readLinkedContent } from './linkedContent'

export default function ContentPreview({ content, tenantId, onClose }: { content: ContentItem; tenantId: string; onClose: () => void }) {
  const [text, setText] = useState('')
  const [error, setError] = useState<string | null>(null)
  const version = content.latestVersion
  const previewUrl = version ? `/api/v1/tenants/${tenantId}/contents/${content.id}/versions/${version.id}/preview` : ''
  const mediaKind = content.mediaKind.toLowerCase()
  const linkedUrl = readLinkedContent(text)

  useEffect(() => {
    if (!previewUrl || mediaKind !== 'plaintext') return
    const abort = new AbortController()
    void fetch(previewUrl, { credentials: 'same-origin', cache: 'no-store', signal: abort.signal })
      .then((response) => { if (!response.ok) throw new Error('Aperçu indisponible.'); return response.text() })
      .then(setText)
      .catch((reason: unknown) => { if (!abort.signal.aborted) setError(reason instanceof Error ? reason.message : 'Aperçu indisponible.') })
    return () => abort.abort()
  }, [mediaKind, previewUrl])

  return <div className="content-preview-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose() }}>
    <section className="content-preview" role="dialog" aria-modal="true" aria-labelledby="content-preview-title">
      <div className="device-detail-heading"><div><p className="eyebrow">Aperçu sans publication</p><h2 id="content-preview-title">{content.title}</h2></div><button className="text-button" onClick={onClose} type="button">Fermer</button></div>
      <div className="content-preview-stage">
        {error && <p className="form-error" role="alert">{error}</p>}
        {!error && mediaKind === 'mp4' && <video src={previewUrl} controls playsInline />}
        {!error && ['jpeg', 'png', 'webp'].includes(mediaKind) && <img src={previewUrl} alt={`Aperçu de ${content.title}`} />}
        {!error && mediaKind === 'plaintext' && (linkedUrl
          ? <iframe src={linkedUrl} title={`Aperçu de ${content.title}`} sandbox="allow-forms allow-scripts allow-same-origin" referrerPolicy="no-referrer" />
          : <pre>{text}</pre>)}
      </div>
    </section>
  </div>
}
