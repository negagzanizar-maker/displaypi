import type { Device } from './types'

export function formatMac(value: string) {
  const compact = value.replace(/[:-]/g, '').toUpperCase()
  return compact.length === 12 ? compact.match(/.{2}/g)?.join(':') ?? compact : value
}

export function formatDate(value: string | null) {
  return value ? new Date(value).toLocaleString('fr-FR') : '—'
}

export function deviceName(devices: Device[], deviceId: string) {
  return devices.find((device) => device.id === deviceId)?.displayName ?? deviceId
}

export function optionalUtc(value: FormDataEntryValue | null) {
  const text = typeof value === 'string' ? value.trim() : ''
  if (!text) return null
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?$/.test(text)) throw new Error('Date UTC invalide.')
  const date = new Date(`${text}Z`)
  if (!Number.isFinite(date.getTime()) || !date.toISOString().startsWith(text)) throw new Error('Date UTC invalide.')
  return date.toISOString()
}
