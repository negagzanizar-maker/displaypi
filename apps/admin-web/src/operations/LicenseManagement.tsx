import { useState } from 'react'
import { deviceName, formatDate } from './formatters'
import { Badge } from './presentation'
import SectionSwitcher, { type SectionView } from './SectionSwitcher'
import type { Device, License, PostJson } from './types'

export default function LicenseManagement({ canAdminister, devices, licenses, tenantId, post, onChanged }: {
  canAdminister: boolean
  devices: Device[]
  licenses: License[]
  tenantId: string
  post: PostJson
  onChanged: () => Promise<void>
}) {
  const [extendId, setExtendId] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [section, setSection] = useState<SectionView>('manage')
  const extendLicense = licenses.find((license) => license.id === extendId)
  const now = Date.now()
  const activeDevices = devices.filter((device) => device.state === 'active')
  const licensableDevices = activeDevices.filter((device) => !licenses.some((license) => license.deviceId === device.id && license.controlState !== 'revoked' && new Date(license.expiresAtUtc).getTime() > now))

  const run = async (operation: () => Promise<unknown>, success: string) => {
    setBusy(true); setError(null); setNotice(null)
    try { await operation(); setNotice(success); await onChanged() } catch (reason: unknown) { setError(reason instanceof Error ? reason.message : 'Opération impossible.') } finally { setBusy(false) }
  }

  const stateAction = (license: License, action: 'suspend' | 'reactivate' | 'revoke') => {
    if (action === 'revoke' && !window.confirm(`Révoquer immédiatement la licence de ${deviceName(devices, license.deviceId)} ?`)) return
    const messages = { suspend: 'Licence suspendue.', reactivate: 'Licence réactivée.', revoke: 'Licence révoquée immédiatement.' }
    void run(() => post(`/api/v1/tenants/${tenantId}/licenses/${license.id}/${action}`, { concurrencyToken: license.concurrencyToken, reason: `${messages[action]} Action depuis l’administration.` }), messages[action])
  }

  return <>
    {error && <p className="form-error" role="alert">{error}</p>}{notice && <p className="form-notice" role="status">{notice}</p>}
    {canAdminister && <SectionSwitcher active={section} manageLabel="Gérer les licences" recordsLabel="Registre des licences" recordCount={licenses.length} onChange={setSection} />}
    {(!canAdminister || section === 'records') && <section className="collection-surface" aria-labelledby="license-register-title"><div className="collection-heading"><div><p className="eyebrow">Historique actif</p><h3 id="license-register-title">Registre des licences</h3></div><span>{licenses.length}</span></div><div className="table-wrap"><table><thead><tr><th>Appareil</th><th>Contrôle</th><th>État effectif</th><th>Début</th><th>Expiration</th>{canAdminister && <th>Actions</th>}</tr></thead><tbody>{licenses.map((license) => <tr key={license.id}><td>{deviceName(devices, license.deviceId)}</td><td><Badge value={license.controlState} /></td><td><Badge value={license.effectiveState} /></td><td>{formatDate(license.validFromUtc)}</td><td>{formatDate(license.expiresAtUtc)}</td>{canAdminister && <td><div className="table-actions">{license.controlState !== 'revoked' && license.controlState !== 'transferPending' && <button className="text-button" disabled={busy} onClick={() => setExtendId(license.id)} type="button">Prolonger</button>}{license.controlState === 'enabled' && <button className="text-button" disabled={busy} onClick={() => stateAction(license, 'suspend')} type="button">Suspendre</button>}{license.controlState === 'suspended' && <button className="text-button" disabled={busy} onClick={() => stateAction(license, 'reactivate')} type="button">Réactiver</button>}{license.controlState !== 'revoked' && <button className="text-button danger-button" disabled={busy} onClick={() => stateAction(license, 'revoke')} type="button">Arrêter / révoquer</button>}</div></td>}</tr>)}</tbody></table>{licenses.length === 0 && <p className="empty-state">Aucune licence.</p>}</div>{extendLicense && <form className="inline-form highlighted-form" onSubmit={(event) => { event.preventDefault(); const form = event.currentTarget; const values = new FormData(form); void run(async () => { await post(`/api/v1/tenants/${tenantId}/licenses/${extendLicense.id}/renew`, { expiresAtUtc: new Date(String(values.get('expiresAt'))).toISOString(), concurrencyToken: extendLicense.concurrencyToken, reason: String(values.get('reason')) }); setExtendId(null) }, 'Licence prolongée.') }}><div><strong>Prolonger {deviceName(devices, extendLicense.deviceId)}</strong><small>Expiration actuelle : {formatDate(extendLicense.expiresAtUtc)}</small></div><label>Nouvelle expiration<input name="expiresAt" type="datetime-local" required /></label><label>Motif<input name="reason" defaultValue="Extension de licence depuis l’administration" minLength={3} maxLength={1000} required /></label><button className="primary-button" disabled={busy} type="submit">Confirmer l’extension</button><button className="text-button" onClick={() => setExtendId(null)} type="button">Annuler</button></form>}</section>}

    {canAdminister && section === 'manage' && <div className="management-stack"><form className="inline-form" onSubmit={(event) => { event.preventDefault(); const form = event.currentTarget; const values = new FormData(form); void run(async () => { await post(`/api/v1/tenants/${tenantId}/licenses`, { deviceId: String(values.get('deviceId')), validFromUtc: new Date().toISOString(), expiresAtUtc: new Date(String(values.get('expiresAt'))).toISOString(), reason: 'Création depuis l’administration' }); form.reset() }, 'Licence créée.') }}><label>Nouvel appareil sans licence<select name="deviceId" required><option value="">Sélectionner</option>{licensableDevices.map((device) => <option key={device.id} value={device.id}>{device.displayName}</option>)}</select></label><label>Expiration<input name="expiresAt" type="datetime-local" required /></label><button className="primary-button" disabled={busy || licensableDevices.length === 0} type="submit">Créer la licence</button></form>{licenses.some((license) => license.controlState === 'enabled' && license.effectiveState === 'active') && <form className="inline-form" onSubmit={(event) => { event.preventDefault(); const form = event.currentTarget; const values = new FormData(form); const source = licenses.find((license) => license.id === values.get('licenseId')); if (!source) return; void run(async () => { await post(`/api/v1/tenants/${tenantId}/licenses/${source.id}/transfer`, { destinationDeviceId: String(values.get('destinationDeviceId')), concurrencyToken: source.concurrencyToken, reason: 'Remplacement d’écran depuis l’administration' }); form.reset() }, 'Transfert engagé après expiration du dernier bail source.') }}><label>Licence source<select name="licenseId" required><option value="">Sélectionner</option>{licenses.filter((license) => license.controlState === 'enabled' && license.effectiveState === 'active').map((license) => <option key={license.id} value={license.id}>{deviceName(devices, license.deviceId)}</option>)}</select></label><label>Appareil de remplacement<select name="destinationDeviceId" required><option value="">Sélectionner</option>{licensableDevices.map((device) => <option key={device.id} value={device.id}>{device.displayName}</option>)}</select></label><button className="primary-button" disabled={busy || licensableDevices.length === 0} type="submit">Transférer la licence</button></form>}</div>}
  </>
}
