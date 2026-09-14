import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page, type Route } from '@playwright/test'

const anonymousSession = {
  authenticated: false,
  authenticationStage: null,
  csrfToken: 'browser-csrf-token',
  userId: null,
  email: null,
  displayName: null,
  tenantId: null,
  tenantRole: null,
  mfaSatisfied: false,
  mfaRequired: true,
}

const platformSession = {
  ...anonymousSession,
  authenticated: true,
  authenticationStage: 'full',
  csrfToken: 'platform-csrf-token',
  userId: '33333333-3333-3333-3333-333333333333',
  email: 'platform@example.test',
  displayName: 'Platform Admin',
  mfaSatisfied: true,
  mfaRequired: true,
}

async function fulfilJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  })
}

async function expectNoWcagViolations(page: Page) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()

  expect(results.violations).toEqual([])
}

test('mobile viewer navigation links resolve to visible sections', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.route('**/api/v1/**', (route) => {
    if (new URL(route.request().url()).pathname === '/api/v1/session') {
      return fulfilJson(route, { ...platformSession, tenantId: 'tenant', tenantRole: 'Viewer' })
    }
    return fulfilJson(route, [])
  })
  await page.goto('http://127.0.0.1:4173/')
  const navigation = page.getByRole('navigation')
  await expect(navigation).toBeVisible()
  await expect(navigation.getByRole('link', { name: 'Utilisateurs' })).toHaveCount(0)
  const targets = await navigation.getByRole('link').evaluateAll((links) => links.map((link) => link.getAttribute('href')!))
  for (const target of targets) {
    await navigation.locator(`a[href="${target}"]`).click()
    const renderedTarget = target === '#account-security' ? target : `#view-${target.slice(1)}`
    await expect(page.locator(renderedTarget)).toBeVisible()
  }
})

test('MFA step-up refreshes CSRF before the next account mutation', async ({ page }) => {
  let rotated = false
  await page.route('**/api/v1/**', (route) => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/v1/session') return fulfilJson(route, { ...platformSession, csrfToken: rotated ? 'rotated-csrf' : 'original-csrf' })
    if (path === '/api/v1/auth/mfa/step-up') {
      expect(route.request().headers()['x-csrf-token']).toBe('original-csrf')
      rotated = true
      return fulfilJson(route, { status: 'authenticated' })
    }
    if (path === '/api/v1/auth/mfa/recovery/regenerate') {
      expect(route.request().headers()['x-csrf-token']).toBe('rotated-csrf')
      return fulfilJson(route, { recoveryCodes: ['test-recovery-code'] })
    }
    return fulfilJson(route, [])
  })
  await page.goto('http://127.0.0.1:4173/')
  const account = page.locator('#account-security')
  await account.getByLabel('Code TOTP', { exact: true }).fill('123456')
  await account.getByRole('button', { name: 'Confirmer mon identité' }).click()
  await expect(account.getByText('Preuve MFA renouvelée.')).toBeVisible()
  await account.getByRole('button', { name: 'Régénérer les codes de récupération' }).click()
  await expect(page.getByText('test-recovery-code')).toBeVisible()
})

test('sign-in uses the CSRF session token and opens the platform dashboard', async ({ page }) => {
  let authenticated = false

  await page.route('**/api/v1/**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname

    if (path === '/api/v1/session' && request.method() === 'GET') {
      return fulfilJson(route, authenticated ? platformSession : anonymousSession)
    }

    if (path === '/api/v1/auth/sign-in' && request.method() === 'POST') {
      expect(request.headers()['x-csrf-token']).toBe(anonymousSession.csrfToken)
      expect(request.postDataJSON()).toEqual({
        email: 'platform@example.test',
        password: 'StrongPassword123!',
      })
      authenticated = true
      return fulfilJson(route, { status: 'authenticated' })
    }

    if (path === '/api/v1/platform/tenants' && request.method() === 'GET') {
      return fulfilJson(route, [])
    }

    return route.abort('failed')
  })

  await page.goto('http://127.0.0.1:4173/')
  await expect(page.getByRole('heading', { name: 'Se connecter' })).toBeVisible()
  await page.getByLabel('Adresse e-mail').fill('platform@example.test')
  await page.getByLabel('Mot de passe').fill('StrongPassword123!')
  await page.getByRole('button', { name: 'Se connecter' }).click()

  await expect(page.getByRole('heading', { name: 'Clients isolés' })).toBeVisible()
  await expect(page.getByText('Bonjour Platform Admin.')).toBeVisible()
  await expectNoWcagViolations(page)
})

test('password recovery remains enumeration-safe and accessible', async ({ page }) => {
  await page.route('**/api/v1/**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname

    if (path === '/api/v1/session') return fulfilJson(route, anonymousSession)
    if (path === '/api/v1/auth/forgot-password' && request.method() === 'POST') {
      expect(request.headers()['x-csrf-token']).toBe(anonymousSession.csrfToken)
      expect(request.postDataJSON()).toEqual({ email: 'unknown@example.test' })
      return route.fulfill({ status: 204 })
    }

    return route.abort('failed')
  })

  await page.goto('http://127.0.0.1:4173/')
  await page.getByRole('button', { name: 'Mot de passe oublié' }).click()
  await expect(page.getByRole('heading', { name: 'Mot de passe oublié' })).toBeVisible()
  await expectNoWcagViolations(page)

  await page.getByLabel('Adresse e-mail').fill('unknown@example.test')
  await page.getByRole('button', { name: 'Demander la récupération' }).click()
  await expect(page.getByRole('status')).toContainText('Si ce compte est éligible')
})

test('one-use invitation screen exposes labelled controls and no WCAG A/AA violation', async ({ page }) => {
  await page.route('**/api/v1/session', (route) => fulfilJson(route, anonymousSession))

  await page.goto('http://127.0.0.1:4173/#/accept-invitation?token=one-use-test-token')
  await expect(page.getByRole('heading', { name: 'Créer votre compte' })).toBeVisible()
  await expect(page.getByLabel('Nom affiché')).toBeVisible()
  await expect(page.getByLabel('Mot de passe')).toHaveAttribute('autocomplete', 'new-password')
  await expectNoWcagViolations(page)
})
