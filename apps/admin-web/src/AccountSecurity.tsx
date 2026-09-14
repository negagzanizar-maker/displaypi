import { useState } from 'react'
import { loadList } from './api'
import type { PostJson } from './operations/types'

type AccountSession = { id: string; createdAtUtc: string; lastSeenAtUtc: string; absoluteExpiresAtUtc: string; isCurrent: boolean }

export default function AccountSecurity({ post, onCodes }: { post: PostJson; onCodes: (codes: string[]) => void }) {
  const [sessions, setSessions] = useState<AccountSession[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const run = async (action: () => Promise<void>) => {
    setBusy(true); setError(null); setNotice(null)
    try { await action() } catch (reason) { setError(reason instanceof Error ? reason.message : 'Opération impossible.') }
    finally { setBusy(false) }
  }
  return <section className="data-section" id="account-security"><h2>Sécurité du compte</h2>
    <p>Les modifications exigent une preuve MFA récente. Régénérer les codes invalide les anciens codes.</p>
    {error && <p role="alert" className="form-error">{error}</p>}{notice && <p role="status">{notice}</p>}
    <form className="inline-form" onSubmit={(event) => {
      event.preventDefault(); const form = event.currentTarget; const code = String(new FormData(form).get('code'))
      void run(async () => { await post('/api/v1/auth/mfa/step-up', { code }); form.reset(); setNotice('Preuve MFA renouvelée.') })
    }}><label>Code TOTP<input name="code" required pattern="[0-9]{6}" maxLength={6} autoComplete="one-time-code" /></label><button disabled={busy} type="submit">Confirmer mon identité</button></form>
    <button type="button" disabled={busy} onClick={() => void run(async () => setSessions(await loadList<AccountSession>('/api/v1/auth/sessions')))}>Afficher mes sessions</button>
    <button type="button" disabled={busy} onClick={() => void run(async () => { await post('/api/v1/auth/sign-out-all', {}) })}>Déconnecter toutes mes sessions</button>
    <button type="button" disabled={busy} onClick={() => void run(async () => {
      const result = await post<{ recoveryCodes: string[] }>('/api/v1/auth/mfa/recovery/regenerate', {})
      onCodes(result.recoveryCodes)
    })}>Régénérer les codes de récupération</button>
    <ul>{sessions.map((session) => <li key={session.id}>{session.isCurrent ? 'Session actuelle' : 'Autre session'} · dernière activité {new Date(session.lastSeenAtUtc).toLocaleString('fr-FR')}
      {!session.isCurrent && <button disabled={busy} type="button" onClick={() => void run(async () => {
        await post(`/api/v1/auth/sessions/${session.id}/revoke`, {})
        setSessions((current) => current.filter((item) => item.id !== session.id))
      })}>Révoquer</button>}</li>)}</ul>
  </section>
}
