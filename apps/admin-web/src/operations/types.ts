export type PostJson = <T>(path: string, body: unknown) => Promise<T>

export type DeviceNetwork = {
  interfaceName: string
  macAddress: string | null
  localAddresses: string[]
  observedAtUtc: string
}

export type Device = {
  id: string
  displayName: string
  state: string
  health: string
  hostname: string | null
  serialNumber: string | null
  networkInterfaces: DeviceNetwork[]
  licenseState: string | null
  licenseExpiresAtUtc: string | null
  concurrencyToken: string
  lastSeenUtc?: string | null
  appliedManifestVersion?: number | null
  playbackHealthCode?: string | null
  playback?: DevicePlayback | null
}

export type DevicePlayback = {
  playerState: string
  contentVersionId: string | null
  contentId: string | null
  title: string | null
  mediaKind: string | null
  desiredStateVersion: number | null
  reportedAtUtc: string
  errorCode: string | null
}

export type License = {
  id: string
  deviceId: string
  controlState: string
  effectiveState: string
  validFromUtc: string
  expiresAtUtc: string
  concurrencyToken: string
}

export type DeviceGroup = {
  id: string
  name: string
  description: string | null
  deviceIds: string[]
  concurrencyToken: string
}

export type ContentVersion = {
  id: string
  byteLength: number
  detectedMimeType: string
  originalDisplayFileName: string
  scanState: string
  rejectionCode: string | null
}

export type ContentItem = {
  id: string
  title: string
  mediaKind: string
  lifecycleState: string
  concurrencyToken: string
  latestVersion: ContentVersion | null
}

export type PlaylistVersion = {
  id: string
  publicationState: string
  itemCount: number
}

export type Playlist = {
  id: string
  name: string
  description: string | null
  latestVersion: PlaylistVersion | null
}

export type EnrollmentCode = {
  enrollmentCode: string
  expiresAtUtc: string
}

export type Invitation = {
  email: string
  token: string
  expiresAtUtc: string
}

export type Member = {
  membershipId: string
  userId: string
  displayName: string
  email: string
  role: string
  state: string
  mfaEnabled: boolean
  concurrencyToken: string
}

export type AuditEvent = {
  id: string
  action: string
  targetType: string
  outcome: string
  reasonCode: string | null
  occurredAtUtc: string
}
