import { useCallback, useEffect, useState, type FormEvent } from 'react'
import type { Session } from './App'
import { loadList, readProblem } from './api'

type PostJson = <T>(path: string, body: unknown) => Promise<T>
type TenantState = 'active' | 'suspended' | 'archived'
type PlatformTenant = {
  id: string
  name: string
  slug: string
  timeZone: string
  state: TenantState
  createdAtUtc: string
  updatedAtUtc: string
  concurrencyToken: string
}
type TenantCreated = {
  tenant: PlatformTenant
  invitationId: string
  initialAdministratorEmail: string
  invitationExpiresAtUtc: string
  invitationToken: string
}

function PlatformDashboard({ session, post }: { session: Session; post: PostJson }) {
  const [tenants, setTenants] = useState<PlatformTenant[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [invitation, setInvitation] = useState<TenantCreated | null>(null)

  const load = useCallback(async () => {
    setTenants(await loadList<PlatformTenant>('/api/v1/platform/tenants'))
    setError(null)
  }, [])

  useEffect(() => {
    void load().catch((reason: unknown) => setError(
      reason instanceof Error ? reason.message : 'Catalogue clients indisponible.',
    ))
  }, [load])

  const mutate = async (operation: () => Promise<void>, success: string) => {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await operation()
      setNotice(success)
      await load()
    } catch (reason: unknown) {
      setError(reason instanceof Error ? reason.message : 'Opération impossible.')
    } finally {
      setBusy(false)
    }
  }

  const changeState = async (tenant: PlatformTenant, state: TenantState) => {
    await mutate(async () => {
      const response = await fetch(`/api/v1/platform/tenants/${tenant.id}/state`, {
        method: 'PATCH',
        credentials: 'same-origin',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json',
          'X-CSRF-TOKEN': session.csrfToken,
        },
        body: JSON.stringify({
          state,
          concurrencyToken: tenant.concurrencyToken,
          reason: state === 'active' ? 'Réactivation depuis la console plateforme' : 'Suspension depuis la console plateforme',
        }),
      })
      if (!response.ok) throw new Error(await readProblem(response))
    }, state === 'active' ? 'Client réactivé.' : 'Client suspendu et sessions révoquées.')
  }

  const activeCount = tenants.filter((tenant) => tenant.state === 'active').length
  return (
    <>
      <section className="status-section" id="platform-overview" aria-labelledby="platform-title">
        <div className="section-heading">
          <div><p className="eyebrow">Administration globale</p><h2 id="platform-title">Clients isolés</h2></div>
          <button className="text-button" onClick={() => void load()} type="button">Actualiser</button>
        </div>
        {error && <p className="form-error" role="alert">{error}</p>}
        {notice && <p className="form-notice" role="status">{notice}</p>}
        <div className="metric-grid">
          <article><strong>{tenants.length}</strong><span>clients</span></article>
          <article><strong>{activeCount}</strong><span>actifs</span></article>
          <article><strong>{tenants.filter((tenant) => tenant.state === 'suspended').length}</strong><span>suspendus</span></article>
          <article><strong>RLS</strong><span>isolation forcée</span></article>
        </div>
      </section>

      <section className="data-section" id="platform-create" aria-labelledby="create-tenant-title">
        <div className="section-heading"><div><p className="eyebrow">Nouveau client</p><h2 id="create-tenant-title">Créer un espace isolé</h2></div><p>Un lien d’invitation à usage unique est créé pour le premier administrateur.</p></div>
        <form className="inline-form" onSubmit={(event: FormEvent<HTMLFormElement>) => {
          event.preventDefault()
          const form = event.currentTarget
          const values = new FormData(form)
          void mutate(async () => {
            const created = await post<TenantCreated>('/api/v1/platform/tenants', {
              name: String(values.get('name') ?? ''),
              slug: String(values.get('slug') ?? ''),
              timeZone: String(values.get('timeZone') ?? ''),
              initialAdministratorEmail: String(values.get('email') ?? ''),
            })
            setInvitation(created)
            form.reset()
          }, 'Client créé.')
        }}>
          <label>Nom du client<input disabled={busy} maxLength={160} name="name" required /></label>
          <label>Identifiant URL<input disabled={busy} maxLength={80} minLength={3} name="slug" pattern="[A-Za-z0-9](?:[A-Za-z0-9-]{1,78}[A-Za-z0-9])?" required /></label>
          <label>Fuseau IANA<input defaultValue="Africa/Casablanca" disabled={busy} maxLength={80} name="timeZone" required /></label>
          <label>Administrateur initial<input disabled={busy} maxLength={320} name="email" required type="email" /></label>
          <button className="primary-button" disabled={busy} type="submit">Créer le client</button>
        </form>
        {invitation && <div className="one-time-secret" role="status"><strong>Invitation à transmettre par un canal sûr</strong><span>{invitation.initialAdministratorEmail} · expire le {new Date(invitation.invitationExpiresAtUtc).toLocaleString('fr-FR')}</span><code>{`${window.location.origin}/#/accept-invitation?token=${encodeURIComponent(invitation.invitationToken)}`}</code><button className="text-button" onClick={() => setInvitation(null)} type="button">Masquer</button></div>}
      </section>

      <section className="data-section" id="platform-tenants" aria-labelledby="tenant-list-title">
        <div className="section-heading"><div><p className="eyebrow">Catalogue</p><h2 id="tenant-list-title">Espaces clients</h2></div><p>La suspension coupe les sessions humaines et les prochains échanges des appareils.</p></div>
        <div className="table-wrap"><table><thead><tr><th>Client</th><th>Slug</th><th>Fuseau</th><th>État</th><th>Action</th></tr></thead><tbody>
          {tenants.map((tenant) => <tr key={tenant.id}><td><strong>{tenant.name}</strong><small>{tenant.id}</small></td><td><code>{tenant.slug}</code></td><td>{tenant.timeZone}</td><td><span className={`state-badge state-${tenant.state}`}>{tenant.state}</span></td><td>{tenant.state !== 'archived' && <button className="text-button" disabled={busy} onClick={() => void changeState(tenant, tenant.state === 'active' ? 'suspended' : 'active')} type="button">{tenant.state === 'active' ? 'Suspendre' : 'Réactiver'}</button>}</td></tr>)}
        </tbody></table>{tenants.length === 0 && <p className="empty-state">Aucun client créé.</p>}</div>
      </section>

      {session.mfaRequired && <section className="data-section" id="platform-step-up" aria-labelledby="platform-step-up-title">
        <div className="section-heading"><div><p className="eyebrow">Action sensible</p><h2 id="platform-step-up-title">Renouveler la preuve MFA</h2></div><p>À utiliser si une action plateforme est refusée après dix minutes.</p></div>
        <form className="inline-form" onSubmit={(event: FormEvent<HTMLFormElement>) => {
          event.preventDefault()
          const form = event.currentTarget
          const values = new FormData(form)
          void mutate(async () => {
            await post('/api/v1/auth/mfa/step-up', { code: String(values.get('code') ?? '') })
            form.reset()
          }, 'Preuve MFA renouvelée pour dix minutes.')
        }}>
          <label>Code TOTP<input autoComplete="one-time-code" disabled={busy} inputMode="numeric" maxLength={6} minLength={6} name="code" pattern="[0-9]{6}" required /></label>
          <button className="primary-button" disabled={busy} type="submit">Confirmer</button>
        </form>
      </section>}
    </>
  )
}

export default PlatformDashboard
