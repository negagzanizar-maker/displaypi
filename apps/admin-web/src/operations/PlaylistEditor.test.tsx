import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import PlaylistEditor from './PlaylistEditor'
import type { ContentItem, Device, Playlist, PostJson } from './types'

it('saves and publishes ordered multiple items in one action', async () => {
  const contents = ['First', 'Second'].map((title, index) => ({
    id: `content-${index}`, title, mediaKind: 'png', lifecycleState: 'approved', concurrencyToken: '',
    latestVersion: { id: `version-${index}`, byteLength: 10, detectedMimeType: 'image/png', originalDisplayFileName: title, scanState: 'clean', rejectionCode: null },
  })) satisfies ContentItem[]
  const created = { id: 'playlist', name: 'Lobby', description: null, concurrencyToken: 'playlist-token', latestVersion: { id: 'version', publicationState: 'published', itemCount: 2 } } satisfies Playlist
  const device = { id: 'device-1', displayName: 'Lobby screen', state: 'active', health: 'online', hostname: null, serialNumber: null, networkInterfaces: [], licenseState: 'active', licenseExpiresAtUtc: null, concurrencyToken: '' } satisfies Device
  const post = vi.fn().mockResolvedValueOnce(created).mockResolvedValueOnce({})
  const onCreated = vi.fn()
  render(<PlaylistEditor contents={contents} devices={[device]} groups={[]} tenantId="tenant" post={post as PostJson} onCreated={onCreated} />)
  const user = userEvent.setup()
  await user.type(screen.getByLabelText('Nom'), 'Lobby')
  await user.selectOptions(screen.getByLabelText('Contenu de l’élément 1'), 'version-0')
  await user.click(screen.getByRole('button', { name: 'Ajouter un élément' }))
  await user.selectOptions(screen.getByLabelText('Contenu de l’élément 2'), 'version-1')
  await user.click(screen.getByRole('button', { name: 'Monter l’élément 2' }))
  await user.click(screen.getByRole('button', { name: 'Enregistrer et publier' }))
  await waitFor(() => expect(onCreated).toHaveBeenCalledWith(created))
  expect(screen.getByText('Playlist enregistrée')).toBeInTheDocument()
  await user.selectOptions(screen.getByLabelText('Destination de la playlist'), 'device:device-1')
  await user.click(screen.getByRole('button', { name: 'Afficher cette playlist' }))
  await waitFor(() => expect(post).toHaveBeenCalledTimes(2))
  expect(post.mock.calls[0][1].publishImmediately).toBe(true)
  expect(post.mock.calls[0][1].items.map((item: { contentVersionId: string }) => item.contentVersionId)).toEqual(['version-1', 'version-0'])
  expect(post.mock.calls[1][0]).toBe('/api/v1/tenants/tenant/devices/device-1/assignments')
  expect(post.mock.calls[1][1]).toMatchObject({ playlistVersionId: 'version', priority: 0, overrideEqualPriority: true })
})

it('accepts caption text directly beneath a video', async () => {
  const contents = [{
    id: 'video', title: 'Welcome video', mediaKind: 'mp4', lifecycleState: 'approved', concurrencyToken: '',
    latestVersion: { id: 'video-version', byteLength: 10, detectedMimeType: 'video/mp4', originalDisplayFileName: 'welcome.mp4', scanState: 'clean', rejectionCode: null },
  }, {
    id: 'caption', title: 'Welcome caption', mediaKind: 'plainText', lifecycleState: 'approved', concurrencyToken: '',
    latestVersion: { id: 'caption-version', byteLength: 10, detectedMimeType: 'text/plain', originalDisplayFileName: 'welcome.txt', scanState: 'clean', rejectionCode: null },
  }] satisfies ContentItem[]
  const created = { id: 'playlist', name: 'Welcome', description: null, concurrencyToken: 'playlist-token', latestVersion: { id: 'version', publicationState: 'published', itemCount: 1 } } satisfies Playlist
  const post = vi.fn().mockResolvedValue(created)
  const user = userEvent.setup()
  render(<PlaylistEditor contents={contents} devices={[]} groups={[]} tenantId="tenant" post={post as PostJson} onCreated={vi.fn()} />)

  await user.type(screen.getByLabelText('Nom'), 'Welcome')
  await user.selectOptions(screen.getByLabelText('Contenu de l’élément 1'), 'video-version')
  await user.type(screen.getByLabelText('Texte sous le contenu (facultatif)'), 'Welcome to the lobby')
  await user.click(screen.getByLabelText(/Répéter la vidéo/))
  await user.click(screen.getByRole('button', { name: 'Enregistrer et publier' }))

  await waitFor(() => expect(post).toHaveBeenCalledOnce())
  expect(post.mock.calls[0][1].items[0]).toEqual({
    contentVersionId: 'video-version',
    captionContentVersionId: null,
    captionText: 'Welcome to the lobby',
    durationMilliseconds: 10000,
    loopVideo: true,
  })
})
