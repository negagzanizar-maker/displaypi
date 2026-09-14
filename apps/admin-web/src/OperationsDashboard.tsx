import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from 'react'
import type { Session, TenantView } from './App'
import { loadList, readProblem as readApiProblem } from './api'
import PlaylistEditor from './operations/PlaylistEditor'
import ContentPreview from './operations/ContentPreview'
import LicenseManagement from './operations/LicenseManagement'
import PublishingForm from './operations/PublishingForm'
import SectionSwitcher, { type SectionView } from './operations/SectionSwitcher'
import { deviceName, formatDate, optionalUtc } from './operations/formatters'
import { Badge, NetworkAddresses } from './operations/presentation'
import type { AuditEvent, ContentItem, Device, DeviceGroup, EnrollmentCode, Invitation, License, Member, Playlist, PostJson } from './operations/types'

function OperationsDashboard({ session, post, view }: { session: Session; post: PostJson; view: TenantView }) {
  const tenantId = session.tenantId as string
  const canManageContent = session.tenantRole === 'TenantAdmin' || session.tenantRole === 'ContentManager'
  const canAdminister = session.tenantRole === 'TenantAdmin'
  const [devices, setDevices] = useState<Device[]>([])
  const [licenses, setLicenses] = useState<License[]>([])
  const [contents, setContents] = useState<ContentItem[]>([])
  const [playlists, setPlaylists] = useState<Playlist[]>([])
  const [deviceGroups, setDeviceGroups] = useState<DeviceGroup[]>([])
  const [members, setMembers] = useState<Member[]>([])
  const [auditEvents, setAuditEvents] = useState<AuditEvent[]>([])
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [refreshedAt, setRefreshedAt] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [selectedDevice, setSelectedDevice] = useState<Device | null>(null)
  const [selectedContent, setSelectedContent] = useState<ContentItem | null>(null)
  const [oneTimeSecret, setOneTimeSecret] = useState<{ label: string; value: string; expiresAtUtc: string } | null>(null)
  const [contentSection, setContentSection] = useState<SectionView>('manage')
  const [playlistSection, setPlaylistSection] = useState<SectionView>('manage')
  const [groupSection, setGroupSection] = useState<SectionView>('manage')
  const [userSection, setUserSection] = useState<SectionView>('manage')

  const load = useCallback(async () => {
    const paths = ['devices', 'licenses', 'contents', 'playlists', 'device-groups']
    const [nextDevices, nextLicenses, nextContents, nextPlaylists, nextDeviceGroups] = await Promise.all(paths.map((path) => loadList(`/api/v1/tenants/${tenantId}/${path}`)))
    setDevices(nextDevices as Device[]); setLicenses(nextLicenses as License[]); setContents(nextContents as ContentItem[]); setPlaylists(nextPlaylists as Playlist[]); setDeviceGroups(nextDeviceGroups as DeviceGroup[])
    if (canAdminister) {
      const [nextMembers, nextAudit] = await Promise.all([loadList<Member>(`/api/v1/tenants/${tenantId}/members`), loadList<AuditEvent>(`/api/v1/tenants/${tenantId}/audit-events`)]); setMembers(nextMembers); setAuditEvents(nextAudit)
    }
    setError(null); setRefreshedAt(new Date().toISOString())
  }, [canAdminister, tenantId])

  useEffect(() => { void load().catch((reason: unknown) => setError(reason instanceof Error ? reason.message : 'Données indisponibles.')) }, [load])

  useEffect(() => {
    if (view !== 'dashboard') return
    const refreshPlayback = () => {
      void loadList<Device>(`/api/v1/tenants/${tenantId}/devices`)
        .then((nextDevices) => { setDevices(nextDevices); setRefreshedAt(new Date().toISOString()) })
        .catch(() => { /* Keep the last known status during a transient refresh failure. */ })
    }
    const interval = window.setInterval(refreshPlayback, 3_000)
    return () => window.clearInterval(interval)
  }, [tenantId, view])

  const mutate = async (operation: () => Promise<void>, success: string) => {
    setBusy(true); setError(null); setNotice(null)
    try { await operation(); setNotice(success); await load() } catch (reason: unknown) { setError(reason instanceof Error ? reason.message : 'Opération impossible.') } finally { setBusy(false) }
  }

  const upload = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); const form = event.currentTarget; const body = new FormData(form)
    await mutate(async () => { const response = await fetch(`/api/v1/tenants/${tenantId}/contents`, { method: 'POST', credentials: 'same-origin', headers: { Accept: 'application/json', 'X-CSRF-TOKEN': session.csrfToken }, body }); if (!response.ok) throw new Error(await readApiProblem(response)); form.reset() }, 'Fichier contrôlé, analysé et rendu utilisable automatiquement.')
  }

  const put = async <T,>(path: string, body: unknown): Promise<T> => {
    const response = await fetch(path, { method: 'PUT', credentials: 'same-origin', headers: { Accept: 'application/json', 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken }, body: JSON.stringify(body) })
    if (!response.ok) throw new Error(await readApiProblem(response)); return (await response.json()) as T
  }

  const renameDevice = async (device: Device, displayName: string) => {
    await mutate(async () => {
      const response = await fetch(`/api/v1/tenants/${tenantId}/devices/${device.id}`, {
        method: 'PATCH', credentials: 'same-origin',
        headers: { Accept: 'application/json', 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken },
        body: JSON.stringify({ displayName, concurrencyToken: device.concurrencyToken }),
      })
      if (!response.ok) throw new Error(await readApiProblem(response))
      setSelectedDevice(null)
    }, 'Nom de l’appareil mis à jour.')
  }

  const activeDevices = devices.filter((device) => device.state === 'active')
  const approvedContents = contents.filter((content) => content.lifecycleState === 'approved' && content.latestVersion)
  const publishedPlaylists = playlists.filter((playlist) => playlist.latestVersion?.publicationState === 'published')
  const activeLicenses = devices.filter((device) => device.licenseState === 'active').length

  return <>
    <section className="view-status" aria-live="polite">
      {error && <p className="form-error" role="alert">{error}</p>}{notice && <p className="form-notice" role="status">{notice}</p>}
      {view !== 'dashboard' && <p className="view-refresh">Dernière actualisation : {refreshedAt ? formatDate(refreshedAt) : 'en attente'}</p>}
    </section>

    {view === 'dashboard' && <section className="status-section" id="view-dashboard" aria-labelledby="status-title">
      <div className="section-heading"><div><p className="eyebrow">Vue réelle</p><h2 id="status-title">État du parc</h2></div><button className="text-button" disabled={refreshing} onClick={() => { setRefreshing(true); void load().catch((reason: Error) => setError(`Actualisation échouée : ${reason.message}.`)).finally(() => setRefreshing(false)) }} type="button">{refreshing ? 'Actualisation…' : 'Actualiser'}</button></div>
      <p className="view-refresh">Dernière actualisation réussie : {refreshedAt ? formatDate(refreshedAt) : 'en attente'}</p>
      <div className="metric-grid"><article><strong>{devices.length}</strong><span>appareils</span></article><article><strong>{activeDevices.length}</strong><span>actifs</span></article><article><strong>{activeLicenses}</strong><span>licences actives</span></article><article><strong>{approvedContents.length}</strong><span>contenus approuvés</span></article></div>
      <div className="overview-grid"><article className="overview-card"><span className="overview-card-icon">●</span><div><strong>Écrans opérationnels</strong><p>{activeDevices.length} actif(s) sur {devices.length} appareil(s).</p></div><a href="#devices">Voir les appareils</a></article><article className="overview-card"><span className="overview-card-icon overview-card-icon-warning">◷</span><div><strong>Publication</strong><p>{publishedPlaylists.length} playlist(s) publiée(s), {deviceGroups.length} groupe(s).</p></div><a href="#playlists">Gérer la publication</a></article></div>
      <section className="live-playback" aria-labelledby="live-playback-title" aria-live="polite">
        <div className="live-playback-heading"><div><p className="eyebrow">Retour des écrans</p><h3 id="live-playback-title">Affichage en cours</h3></div><span><i aria-hidden="true" /> Actualisation automatique</span></div>
        <div className="live-playback-grid">{devices.map((device) => <article className="live-playback-card" key={device.id}>
          <div className="live-playback-card-heading"><div><strong>{device.displayName}</strong><small>{device.playback ? `Signal reçu ${formatDate(device.playback.reportedAtUtc)}` : 'Aucun signal reçu'}</small></div><Badge value={device.playback?.playerState ?? device.health} /></div>
          <div className="live-playback-content"><span>{device.playback?.title ? 'Affiché maintenant' : 'État actuel'}</span><strong>{device.playback?.title ?? (device.playback?.playerState === 'synchronizing' ? 'Synchronisation…' : device.playback?.playerState === 'noContent' ? 'Aucun contenu' : device.playback?.playerState === 'notLicensed' ? 'Non licencié' : 'En attente du lecteur')}</strong>{device.playback?.mediaKind && <small>{device.playback.mediaKind} · manifeste {device.playback.desiredStateVersion ?? '—'}</small>}{device.playback?.errorCode && <small className="live-playback-error">Erreur : {device.playback.errorCode}</small>}</div>
        </article>)}</div>
        {devices.length === 0 && <p className="empty-state">Aucun appareil à surveiller.</p>}
      </section>
    </section>}

    {view === 'devices' && <section className="data-section" id="view-devices" aria-labelledby="devices-title">
      <div className="section-heading"><div><p className="eyebrow">Inventaire</p><h2 id="devices-title">Appareils Raspberry Pi</h2></div><p>Données réelles déclarées par l’agent. Sélectionnez un appareil pour consulter ses détails techniques.</p></div>
      <div className="device-grid">{devices.map((device) => { const licensed = device.licenseState === 'active'; return <button className={`device-card ${licensed ? 'device-card-licensed' : 'device-card-unlicensed'}`} key={device.id} onClick={() => setSelectedDevice(device)} type="button"><span className="device-card-status"><span aria-hidden="true" />{licensed ? 'Licence active' : 'Licence inactive'}</span><strong>{device.displayName}</strong><span className="device-card-state"><Badge value={device.state} /><Badge value={device.health} /></span><span className="device-card-action">Voir les détails <span aria-hidden="true">→</span></span></button> })}</div>
      {devices.length === 0 && <p className="empty-state">Aucun appareil dans ce tenant.</p>}
    </section>}
    {selectedDevice && <DeviceDetails canRename={canAdminister} device={selectedDevice} onClose={() => setSelectedDevice(null)} onRename={(displayName) => void renameDevice(selectedDevice, displayName)} />}

    {view === 'licenses' && <section className="data-section" id="view-licenses" aria-labelledby="licenses-title">
      <div className="section-heading"><div><p className="eyebrow">Autorisation</p><h2 id="licenses-title">Licences</h2></div><p>L’expiration est appliquée côté serveur et dans le bail hors ligne signé.</p></div>
      <LicenseManagement canAdminister={canAdminister} devices={devices} licenses={licenses} tenantId={tenantId} post={post} onChanged={load} />
    </section>}

    {view === 'content' && <section className="data-section" id="view-content" aria-labelledby="contents-title">
      <div className="section-heading"><div><p className="eyebrow">Médiathèque privée</p><h2 id="contents-title">Contenus</h2></div><p>JPEG, PNG, WebP, MP4 ou texte UTF-8. Aucun fichier n’est public.</p></div>
      {canManageContent && <SectionSwitcher active={contentSection} manageLabel="Ajouter un contenu" recordsLabel="Contenus enregistrés" recordCount={contents.length} onChange={setContentSection} />}
      {canManageContent && contentSection === 'manage' && <form className="inline-form content-upload-form" onSubmit={(event) => void upload(event)}><label>Titre<input name="Title" maxLength={200} required /></label><label>Contenu<input name="File" type="file" accept="image/jpeg,image/png,image/webp,video/mp4,text/plain,.txt" required /></label><button className="primary-button" disabled={busy} type="submit">Ajouter le contenu</button><small className="form-help">Le format est détecté automatiquement.</small></form>}
      {(!canManageContent || contentSection === 'records') && <section className="collection-surface" aria-labelledby="saved-content-title"><div className="collection-heading"><div><p className="eyebrow">Bibliothèque</p><h3 id="saved-content-title">Contenus enregistrés</h3></div><span>{contents.length}</span></div><div className="card-list">{contents.map((content) => <article key={content.id}><div><strong>{content.title}</strong><small>{content.latestVersion?.originalDisplayFileName ?? 'Sans version'}</small></div><Badge value={content.lifecycleState} />{content.lifecycleState === 'approved' && content.latestVersion?.scanState === 'clean' && <button className="text-button" onClick={() => setSelectedContent(content)} type="button">Aperçu</button>}</article>)}</div>{contents.length === 0 && <p className="empty-state">Aucun contenu enregistré.</p>}</section>}
    </section>}
    {selectedContent && <ContentPreview content={selectedContent} tenantId={tenantId} onClose={() => setSelectedContent(null)} />}

    {view === 'playlists' && <section className="data-section" id="view-playlists" aria-labelledby="playlists-title">
      <div className="section-heading"><div><p className="eyebrow">Affichage direct</p><h2 id="playlists-title">Contenus et publication</h2></div><p>Choisissez un contenu, vérifiez son aperçu, puis envoyez-le directement à un écran ou un groupe.</p></div>
      {canManageContent && <SectionSwitcher active={playlistSection} manageLabel="Créer et publier" recordsLabel="Playlists enregistrées" recordCount={playlists.length} onChange={setPlaylistSection} />}
      {canManageContent && playlistSection === 'manage' && <div className="workflow-grid"><PublishingForm contents={approvedContents} devices={activeDevices} groups={deviceGroups} tenantId={tenantId} post={post} onPublished={load} /><PlaylistEditor contents={approvedContents} tenantId={tenantId} post={post} onCreated={(created) => { setPlaylists((current) => [...current, created]); setNotice('Playlist enregistrée et publiée.'); setPlaylistSection('records') }} /></div>}
      {(!canManageContent || playlistSection === 'records') && <section className="collection-surface" aria-labelledby="saved-playlists-title"><div className="collection-heading"><div><p className="eyebrow">Catalogue</p><h3 id="saved-playlists-title">Playlists enregistrées</h3></div><span>{playlists.length}</span></div><div className="card-list">{playlists.map((playlist) => <article key={playlist.id}><div><strong>{playlist.name}</strong><small>{playlist.latestVersion?.itemCount ?? 0} élément(s)</small></div><Badge value={playlist.latestVersion?.publicationState ?? 'vide'} />{canManageContent && playlist.latestVersion?.publicationState === 'draft' && <button type="button" disabled={busy} onClick={() => void mutate(async () => { await post(`/api/v1/tenants/${tenantId}/playlists/${playlist.id}/versions/${playlist.latestVersion?.id}/publish`, {}) }, 'Playlist publiée.')}>Publier le brouillon</button>}</article>)}</div>{playlists.length === 0 && <p className="empty-state">Aucune playlist enregistrée.</p>}</section>}
    </section>}

    {view === 'groups' && <section className="data-section" id="view-groups" aria-labelledby="groups-title">
      <div className="section-heading"><div><p className="eyebrow">Ciblage déterministe</p><h2 id="groups-title">Groupes</h2></div><p>Consultez les membres d’un groupe et publiez une playlist pour plusieurs écrans. La publication active la plus récente remplace l’affichage précédent.</p></div>
      {(canAdminister || canManageContent) && <SectionSwitcher active={groupSection} manageLabel="Gérer les groupes" recordsLabel="Groupes enregistrés" recordCount={deviceGroups.length} onChange={setGroupSection} />}
      {(!canAdminister && !canManageContent || groupSection === 'records') && <section className="collection-surface" aria-labelledby="saved-groups-title"><div className="collection-heading"><div><p className="eyebrow">Répertoire</p><h3 id="saved-groups-title">Groupes enregistrés</h3></div><span>{deviceGroups.length}</span></div><div className="group-list">{deviceGroups.map((group) => <article key={group.id}><div><strong>{group.name}</strong><small>{group.deviceIds.length} appareil(s) · {group.description ?? 'Sans description'}</small></div><div className="group-members">{group.deviceIds.map((deviceId) => <span key={deviceId}>{deviceName(devices, deviceId)}</span>)}</div><a className="text-button" href="#playlists">Publier</a></article>)}</div>{deviceGroups.length === 0 && <p className="empty-state">Aucun groupe enregistré.</p>}</section>}
      {(canAdminister || canManageContent) && groupSection === 'manage' && <div className="workflow-grid">{canAdminister && <form className="stack-form" onSubmit={(event) => { event.preventDefault(); const form = event.currentTarget; const values = new FormData(form); void mutate(async () => { await post(`/api/v1/tenants/${tenantId}/device-groups`, { name: String(values.get('name')), description: String(values.get('description')) || null }); form.reset() }, 'Groupe créé.') }}><h3>Créer un groupe</h3><label>Nom<input name="name" maxLength={160} required /></label><label>Description<input name="description" maxLength={2000} /></label><button className="primary-button" disabled={busy} type="submit">Créer</button></form>}{canAdminister && <form className="stack-form" onSubmit={(event) => { event.preventDefault(); const form = event.currentTarget; const values = new FormData(form); const group = deviceGroups.find((item) => item.id === values.get('groupId')); const select = form.elements.namedItem('deviceIds') as HTMLSelectElement | null; if (!group || !select) return; const deviceIds = Array.from(select.selectedOptions, (option) => option.value); void mutate(async () => { await put(`/api/v1/tenants/${tenantId}/device-groups/${group.id}/members`, { deviceIds, concurrencyToken: group.concurrencyToken }); form.reset() }, 'Membres du groupe remplacés.') }}><h3>Définir les membres</h3><label>Groupe<select name="groupId" required><option value="">Sélectionner</option>{deviceGroups.map((group) => <option key={group.id} value={group.id}>{group.name}</option>)}</select></label><label>Appareils<select name="deviceIds" multiple required size={Math.min(Math.max(devices.length, 2), 8)}>{devices.filter((device) => device.state !== 'retired' && device.state !== 'quarantined').map((device) => <option key={device.id} value={device.id}>{device.displayName}</option>)}</select></label><button className="primary-button" disabled={busy || deviceGroups.length === 0} type="submit">Remplacer les membres</button></form>}{canManageContent && <form className="stack-form" onSubmit={(event) => { event.preventDefault(); const form = event.currentTarget; const values = new FormData(form); void mutate(async () => { await post(`/api/v1/tenants/${tenantId}/device-groups/${String(values.get('groupId'))}/assignments`, { playlistVersionId: String(values.get('playlistVersionId')), priority: Number(values.get('priority')), startsAtUtc: optionalUtc(values.get('startsAt')), endsAtUtc: optionalUtc(values.get('endsAt')), presentationTimeZone: 'UTC' }); form.reset() }, 'Playlist planifiée pour le groupe.') }}><h3>Publier pour un groupe</h3><label>Groupe<select name="groupId" required><option value="">Sélectionner</option>{deviceGroups.map((group) => <option key={group.id} value={group.id}>{group.name}</option>)}</select></label><label>Playlist<select name="playlistVersionId" required><option value="">Sélectionner</option>{publishedPlaylists.map((playlist) => <option key={playlist.id} value={playlist.latestVersion?.id}>{playlist.name}</option>)}</select></label><label>Priorité<input name="priority" type="number" min={-1000} max={1000} defaultValue={0} required /></label><label>Début UTC (facultatif)<input name="startsAt" type="datetime-local" /></label><label>Fin UTC exclusive (facultative)<input name="endsAt" type="datetime-local" /></label><button className="primary-button" disabled={busy || deviceGroups.length === 0 || publishedPlaylists.length === 0} type="submit">Publier le planning</button></form>}</div>}
    </section>}

    {view === 'users' && canAdminister && <section className="data-section" id="view-users" aria-labelledby="users-title">
      <div className="section-heading"><div><p className="eyebrow">Accès sur invitation</p><h2 id="users-title">Utilisateurs</h2></div><p>{session.mfaRequired ? 'Les changements de rôle et transferts exigent une preuve MFA datant de moins de dix minutes.' : 'Les administrateurs connectés peuvent gérer les accès avec leur session active.'}</p></div>
      <SectionSwitcher active={userSection} manageLabel="Inviter un utilisateur" recordsLabel="Utilisateurs enregistrés" recordCount={members.length} onChange={setUserSection} />
      {userSection === 'manage' && <>{session.mfaRequired && <form className="inline-form" onSubmit={(event) => { event.preventDefault(); const form = event.currentTarget; const values = new FormData(form); void mutate(async () => { await post('/api/v1/auth/mfa/step-up', { code: String(values.get('code')) }); form.reset() }, 'Preuve MFA renouvelée pour dix minutes.') }}><label>Code TOTP actuel<input name="code" inputMode="numeric" pattern="[0-9]{6}" minLength={6} maxLength={6} autoComplete="one-time-code" required /></label><button className="primary-button" disabled={busy} type="submit">Renouveler la preuve MFA</button></form>}<form className="inline-form" onSubmit={(event) => { event.preventDefault(); const form = event.currentTarget; const values = new FormData(form); void mutate(async () => { const invitation = await post<Invitation>(`/api/v1/tenants/${tenantId}/invitations`, { email: String(values.get('email')), role: String(values.get('role')) }); setOneTimeSecret({ label: `Invitation pour ${invitation.email}`, value: `${window.location.origin}/#/accept-invitation?token=${encodeURIComponent(invitation.token)}`, expiresAtUtc: invitation.expiresAtUtc }); form.reset() }, 'Invitation créée.') }}><label>E-mail<input name="email" type="email" required /></label><label>Rôle<select name="role"><option value="viewer">Lecture</option><option value="contentManager">Gestionnaire de contenu</option><option value="tenantAdmin">Administrateur tenant</option></select></label><button className="primary-button" disabled={busy} type="submit">Créer l’invitation</button></form></>}
      {userSection === 'records' && <section className="collection-surface" aria-labelledby="saved-users-title"><div className="collection-heading"><div><p className="eyebrow">Annuaire</p><h3 id="saved-users-title">Utilisateurs enregistrés</h3></div><span>{members.length}</span></div><div className="card-list">{members.map((member) => <article key={member.membershipId}><div><strong>{member.displayName}</strong><small>{member.email}{session.mfaRequired ? ` · MFA ${member.mfaEnabled ? 'active' : 'non activée'}` : ''}</small></div><Badge value={member.state} /><select aria-label={`Rôle de ${member.displayName}`} disabled={busy || member.userId === session.userId || member.state !== 'active'} value={member.role} onChange={(event) => { const role = event.currentTarget.value; void mutate(async () => { await post(`/api/v1/tenants/${tenantId}/members/${member.membershipId}/role`, { role, concurrencyToken: member.concurrencyToken, reason: 'Modification depuis le tableau de bord' }) }, 'Rôle modifié et sessions révoquées.') }}><option value="tenantAdmin">Administrateur</option><option value="contentManager">Gestionnaire</option><option value="viewer">Lecture</option></select>{member.userId !== session.userId && member.state !== 'removed' && <button className="text-button" disabled={busy} onClick={() => void mutate(async () => { const action = member.state === 'active' ? 'suspend' : 'reactivate'; await post(`/api/v1/tenants/${tenantId}/members/${member.membershipId}/${action}`, { concurrencyToken: member.concurrencyToken, reason: 'Modification depuis le tableau de bord' }) }, member.state === 'active' ? 'Accès suspendu.' : 'Accès réactivé.')} type="button">{member.state === 'active' ? 'Suspendre' : 'Réactiver'}</button>}</article>)}</div>{members.length === 0 && <p className="empty-state">Aucun utilisateur enregistré.</p>}</section>}
    </section>}
    {view === 'audit' && canAdminister && <section className="data-section" id="view-audit" aria-labelledby="audit-title"><div className="section-heading"><div><p className="eyebrow">Traçabilité</p><h2 id="audit-title">Journal d’audit</h2></div><p>Les détails sensibles ne sont pas exposés dans cette vue.</p></div><div className="table-wrap"><table><thead><tr><th>Date</th><th>Action</th><th>Cible</th><th>Résultat</th><th>Motif</th></tr></thead><tbody>{auditEvents.map((event) => <tr key={event.id}><td>{formatDate(event.occurredAtUtc)}</td><td><code>{event.action}</code></td><td>{event.targetType}</td><td><Badge value={event.outcome} /></td><td>{event.reasonCode ?? '—'}</td></tr>)}</tbody></table>{auditEvents.length === 0 && <p className="empty-state">Aucun événement d’audit.</p>}</div></section>}
    {view === 'users' && canAdminister && userSection === 'manage' && <>{<section className="data-section action-panel" aria-labelledby="enroll-title"><div><p className="eyebrow">Enrôlement à usage unique</p><h2 id="enroll-title">Ajouter un écran</h2><p>Le secret expire après quinze minutes.</p></div><form onSubmit={(event) => { event.preventDefault(); const form = event.currentTarget; const values = new FormData(form); void mutate(async () => { const created = await post<EnrollmentCode>(`/api/v1/tenants/${tenantId}/enrollment-codes`, { displayName: String(values.get('displayName')), expectedSerialNumber: String(values.get('serial')) || null, expiresInMinutes: 15 }); setOneTimeSecret({ label: 'Code d’enrôlement', value: created.enrollmentCode, expiresAtUtc: created.expiresAtUtc }); form.reset() }, 'Code créé.') }}><label>Nom de l’écran<input name="displayName" maxLength={160} required /></label><label>Série attendue<input name="serial" maxLength={32} /></label><button className="primary-button" disabled={busy} type="submit">Créer le code</button></form></section>}{oneTimeSecret && <div className="one-time-secret" role="status"><strong>{oneTimeSecret.label} — à copier maintenant</strong><code>{oneTimeSecret.value}</code><span>Expire le {formatDate(oneTimeSecret.expiresAtUtc)}</span></div>}</>}
  </>
}

function DeviceDetails({ canRename, device, onClose, onRename }: { canRename: boolean; device: Device; onClose: () => void; onRename: (displayName: string) => void }) {
  const [displayName, setDisplayName] = useState(device.displayName)
  return <div className="device-detail-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose() }}><section className="device-detail" role="dialog" aria-modal="true" aria-labelledby="device-detail-title"><div className="device-detail-heading"><div><p className="eyebrow">Détails de l’appareil</p><h2 id="device-detail-title">{device.displayName}</h2></div><button className="text-button" onClick={onClose} type="button">Fermer</button></div><div className="device-detail-grid"><Detail label="Nom d’affichage" value={device.displayName} /><Detail label="Nom système" value={device.hostname} code /><Detail label="Numéro de série" value={device.serialNumber} code /><Detail label="État" value={<Badge value={device.state} />} /><Detail label="Santé" value={<Badge value={device.health} />} /><Detail label="Licence" value={<Badge value={device.licenseState ?? 'aucune'} />} /><div className="device-detail-field"><span>Adresses IP</span><NetworkAddresses device={device} kind="ip" /></div><div className="device-detail-field"><span>Adresses MAC</span><NetworkAddresses device={device} kind="mac" /></div><Detail label="Expiration de licence" value={formatDate(device.licenseExpiresAtUtc)} /><Detail label="Dernière activité" value={formatDate(device.lastSeenUtc ?? null)} /><Detail label="Manifest publié" value={device.appliedManifestVersion ?? null} /><Detail label="Santé de lecture" value={device.playbackHealthCode} code /><Detail label="Identifiant interne" value={device.id} code /></div>{canRename && <form className="device-rename-form" onSubmit={(event) => { event.preventDefault(); const value = displayName.trim(); if (value && value !== device.displayName) onRename(value) }}><label>Modifier le nom d’affichage<input value={displayName} maxLength={160} onChange={(event) => setDisplayName(event.target.value)} required /></label><button className="primary-button" disabled={!displayName.trim() || displayName.trim() === device.displayName} type="submit">Enregistrer le nom</button></form>}</section></div>
}

function Detail({ label, value, code = false }: { label: string; value: ReactNode; code?: boolean }) {
  return <div className="device-detail-field"><span>{label}</span>{code ? <code>{value ?? '—'}</code> : <strong>{value ?? '—'}</strong>}</div>
}

export default OperationsDashboard
