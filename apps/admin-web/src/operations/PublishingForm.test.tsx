import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import PublishingForm from './PublishingForm'
import type { ContentItem, Device, Playlist, PostJson } from './types'

it('shows a compact content preview and publishes directly without a draft step', async () => {
  const content = {
    id: 'content-1', title: 'Lobby image', mediaKind: 'png', lifecycleState: 'approved', concurrencyToken: '',
    latestVersion: { id: 'content-version-1', byteLength: 10, detectedMimeType: 'image/png', originalDisplayFileName: 'lobby.png', scanState: 'clean', rejectionCode: null },
  } satisfies ContentItem
  const device = {
    id: 'device-1', displayName: 'Lobby screen', state: 'active', health: 'online', hostname: null,
    serialNumber: null, networkInterfaces: [], licenseState: 'active', licenseExpiresAtUtc: null, concurrencyToken: '',
  } satisfies Device
  const published = {
    id: 'playlist-1', name: 'Publication – Lobby image', description: null, concurrencyToken: 'playlist-token',
    latestVersion: { id: 'playlist-version-1', publicationState: 'published', itemCount: 1 },
  } satisfies Playlist
  const post = vi.fn().mockResolvedValueOnce(published).mockResolvedValueOnce({})
  const onPublished = vi.fn().mockResolvedValue(undefined)
  const user = userEvent.setup()

  render(<PublishingForm contents={[content]} devices={[device]} groups={[]} tenantId="tenant-1" post={post as PostJson} onPublished={onPublished} />)

  expect(screen.queryByRole('group', { name: /type/i })).not.toBeInTheDocument()
  await user.selectOptions(screen.getByLabelText('Contenu'), 'content-version-1')
  expect(screen.getByRole('img', { name: 'Aperçu de Lobby image' })).toHaveAttribute(
    'src',
    '/api/v1/tenants/tenant-1/contents/content-1/versions/content-version-1/preview',
  )
  await user.type(screen.getByLabelText('Texte superposé au contenu (facultatif)'), 'Bienvenue au bureau')
  await user.selectOptions(screen.getByLabelText('Destination'), 'device:device-1')
  await user.click(screen.getByRole('button', { name: 'Afficher maintenant' }))

  await waitFor(() => expect(onPublished).toHaveBeenCalledOnce())
  expect(post).toHaveBeenCalledTimes(2)
  expect(post.mock.calls[0][0]).toBe('/api/v1/tenants/tenant-1/playlists')
  expect(post.mock.calls[0][1]).toMatchObject({ publishImmediately: true, items: [{ captionText: 'Bienvenue au bureau' }] })
  expect(post.mock.calls[1][0]).toBe('/api/v1/tenants/tenant-1/devices/device-1/assignments')
  expect(post.mock.calls[1][1]).toMatchObject({ priority: 0, overrideEqualPriority: true })
  expect(post.mock.calls.some(([path]) => String(path).endsWith('/publish'))).toBe(false)
})
