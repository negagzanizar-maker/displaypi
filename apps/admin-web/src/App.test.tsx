import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'

const anonymousSession = { authenticated: false, authenticationStage: null, csrfToken: 'csrf-test-token', userId: null, email: null, displayName: null, tenantId: null, tenantRole: null, mfaSatisfied: false, mfaRequired: true }

describe('administration authentication shell', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    window.history.replaceState(null, '', '/')
  })

  it('bootstraps CSRF and shows invitation-only sign-in without fake data', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(anonymousSession), { status: 200 })))
    render(<App />)
    expect(await screen.findByRole('heading', { name: 'Se connecter' })).toBeInTheDocument()
    expect(screen.getByText(/comptes sont créés uniquement sur invitation/i)).toBeInTheDocument()
    expect(screen.queryByText(/raspberry pi connecté/i)).not.toBeInTheDocument()
  })

  it('sends the in-memory CSRF token with sign-in and renders the verified dashboard', async () => {
    const authenticatedSession = { ...anonymousSession, authenticated: true, authenticationStage: 'full', csrfToken: 'authenticated-csrf', userId: '11111111-1111-1111-1111-111111111111', email: 'admin@example.test', displayName: 'Admin Test', tenantId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', tenantRole: 'TenantAdmin', mfaSatisfied: false, mfaRequired: false }
    const deviceInventory = [{
      id: '22222222-2222-2222-2222-222222222222', displayName: 'Écran accueil', state: 'active', health: 'online',
      hostname: 'pi-lobby', serialNumber: '10000000ABCD1234', licenseState: 'active', licenseExpiresAtUtc: '2026-09-15T12:00:00Z',
      osDescription: 'Debian GNU/Linux 13', architecture: 'arm64', agentVersion: '0.1.15', playerVersion: '0.1.15',
      diskCapacityBytes: 34359738368, freeDiskBytes: 17179869184, serverObservedIp: '192.168.1.44',
      networkInterfaces: [{ interfaceName: 'wlan0', macAddress: 'B827EB123456', localAddresses: ['192.168.1.44'], observedAtUtc: '2026-08-16T12:00:00Z' }],
    }]
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(anonymousSession), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ status: 'authenticated' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(authenticatedSession), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(deviceInventory), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)
    await user.type(await screen.findByLabelText('Adresse e-mail'), 'admin@example.test')
    await user.type(screen.getByLabelText('Mot de passe'), 'StrongPassword123')
    await user.click(screen.getByRole('button', { name: 'Se connecter' }))
    expect(await screen.findByText(/bonjour admin test/i)).toBeInTheDocument()
    expect(screen.getByText('Session active')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Sécurité du compte' })).not.toBeInTheDocument()
    const signInOptions = fetchMock.mock.calls[1]?.[1] as RequestInit
    expect((signInOptions.headers as Record<string, string>)['X-CSRF-TOKEN']).toBe('csrf-test-token')
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(10))
    expect(screen.getByRole('heading', { name: 'État du parc' })).toBeInTheDocument()
    await user.click(screen.getByRole('link', { name: 'Devices' }))
    expect(await screen.findByRole('heading', { name: 'Appareils Raspberry Pi' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Écran accueil/ }))
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(screen.getByText('pi-lobby')).toBeInTheDocument()
    expect(screen.getByText('10000000ABCD1234')).toBeInTheDocument()
    expect(screen.getAllByText('192.168.1.44')).toHaveLength(2)
    expect(screen.getByText('Debian GNU/Linux 13')).toBeInTheDocument()
    expect(screen.getByText('arm64')).toBeInTheDocument()
    expect(screen.getAllByText('0.1.15')).toHaveLength(2)
    expect(screen.getByText('32 Go')).toBeInTheDocument()
    expect(screen.getByText('16 Go')).toBeInTheDocument()
    expect(screen.getByText('B8:27:EB:12:34:56')).toBeInTheDocument()
  })

  it('renders the platform catalog and creates an isolated customer with its one-time invitation', async () => {
    const platformSession = { ...anonymousSession, authenticated: true, authenticationStage: 'full', csrfToken: 'platform-csrf', userId: '33333333-3333-3333-3333-333333333333', email: 'platform@example.test', displayName: 'Platform Admin', mfaSatisfied: true }
    const created = {
      tenant: { id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', name: 'Customer B', slug: 'customer-b', timeZone: 'Africa/Casablanca', state: 'active', createdAtUtc: '2026-08-15T12:00:00Z', updatedAtUtc: '2026-08-15T12:00:00Z', concurrencyToken: 'cccccccc-cccc-cccc-cccc-cccccccccccc' },
      invitationId: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
      initialAdministratorEmail: 'customer@example.test',
      invitationExpiresAtUtc: '2026-08-16T12:00:00Z',
      invitationToken: 'one-time-invitation',
    }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(platformSession), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(created), { status: 201 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([created.tenant]), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Clients isolés' })).toBeInTheDocument()
    await user.type(screen.getByLabelText('Nom du client'), 'Customer B')
    await user.type(screen.getByLabelText('Identifiant URL'), 'customer-b')
    await user.type(screen.getByLabelText('Administrateur initial'), 'customer@example.test')
    await user.click(screen.getByRole('button', { name: 'Créer le client' }))

    expect(await screen.findByText(/#\/accept-invitation\?token=one-time-invitation/)).toBeInTheDocument()
    expect(screen.getByText('Customer B')).toBeInTheDocument()
    const createOptions = fetchMock.mock.calls[2]?.[1] as RequestInit
    expect((createOptions.headers as Record<string, string>)['X-CSRF-TOKEN']).toBe('platform-csrf')
  })

  it('accepts an invitation from the one-time link and returns to sign-in', async () => {
    window.history.replaceState(null, '', '/#/accept-invitation?token=invite-token')
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(anonymousSession), { status: 200 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Créer votre compte' })).toBeInTheDocument()
    await user.type(screen.getByLabelText('Nom affiché'), 'New User')
    await user.type(screen.getByLabelText('Mot de passe'), 'StrongPassword123')
    await user.click(screen.getByRole('button', { name: 'Créer le compte' }))

    expect(await screen.findByRole('heading', { name: 'Se connecter' })).toBeInTheDocument()
    expect(screen.getByText(/invitation acceptée/i)).toBeInTheDocument()
    expect(fetchMock.mock.calls[1]?.[0]).toBe('/api/v1/invitations/accept')
    const acceptanceCall = fetchMock.mock.calls[1]
    expect(acceptanceCall).toBeDefined()
    expect(JSON.parse(String((acceptanceCall![1] as RequestInit).body))).toEqual({
      token: 'invite-token',
      displayName: 'New User',
      password: 'StrongPassword123',
    })
  })
})
