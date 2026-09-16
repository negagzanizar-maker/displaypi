import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'
import { apiRequest } from '../api'
import PlaylistManagement from './PlaylistManagement'
import type { Playlist, PostJson } from './types'

vi.mock('../api', () => ({ apiRequest: vi.fn() }))

const playlist = {
  id: 'playlist-1',
  name: 'Accueil',
  description: null,
  concurrencyToken: 'playlist-token',
  latestVersion: { id: 'version-1', publicationState: 'published', itemCount: 2 },
} satisfies Playlist

beforeEach(() => {
  vi.mocked(apiRequest).mockReset().mockResolvedValue([
    { id: 'item-1', contentVersionId: 'content-1', title: 'Logo', mediaKind: 'Png', position: 0, durationMilliseconds: 10000, loopVideo: false },
    { id: 'item-2', contentVersionId: 'content-2', title: 'Bienvenue', mediaKind: 'PlainText', position: 1, durationMilliseconds: 10000, loopVideo: false },
  ])
  vi.spyOn(window, 'confirm').mockReturnValue(true)
})

it('loads playlist items and removes one through a new playlist revision', async () => {
  const post = vi.fn().mockResolvedValue({})
  const onChanged = vi.fn().mockResolvedValue(undefined)
  const user = userEvent.setup()
  render(<PlaylistManagement playlists={[playlist]} tenantId="tenant-1" post={post as PostJson} onChanged={onChanged} />)

  await user.click(screen.getByRole('button', { name: 'Gérer les éléments' }))
  expect(await screen.findByText('1. Logo')).toBeInTheDocument()
  await user.click(screen.getAllByRole('button', { name: 'Retirer' })[0])

  await waitFor(() => expect(onChanged).toHaveBeenCalledOnce())
  expect(post).toHaveBeenCalledWith(
    '/api/v1/tenants/tenant-1/playlists/playlist-1/items/item-1/remove',
    { concurrencyToken: 'playlist-token', reason: 'Retrait depuis le tableau de bord' },
  )
})

it('archives a playlist after confirmation', async () => {
  const post = vi.fn().mockResolvedValue(undefined)
  const onChanged = vi.fn().mockResolvedValue(undefined)
  const user = userEvent.setup()
  render(<PlaylistManagement playlists={[playlist]} tenantId="tenant-1" post={post as PostJson} onChanged={onChanged} />)

  await user.click(screen.getByRole('button', { name: 'Supprimer' }))

  await waitFor(() => expect(onChanged).toHaveBeenCalledOnce())
  expect(post).toHaveBeenCalledWith(
    '/api/v1/tenants/tenant-1/playlists/playlist-1/archive',
    { concurrencyToken: 'playlist-token', reason: 'Suppression depuis le tableau de bord' },
  )
})
