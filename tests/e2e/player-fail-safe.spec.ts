import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'

test('player fails closed to Not licensed when its local agent is unavailable', async ({ page }) => {
  await page.route('**/player/v1/state', (route) => route.fulfill({ status: 503 }))

  await page.goto('http://127.0.0.1:4174/')
  await expect(page.getByRole('heading', { name: 'Not licensed' })).toBeVisible()

  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()
  expect(results.violations).toEqual([])
})

test('player renders only the manifest content returned by the local agent', async ({ page }) => {
  await page.route('**/player/v1/state', (route) => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      status: 'ready',
      message: 'Ready',
      desiredStateVersion: 7,
      authorizationExpiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
      authorizationRemainingMilliseconds: 60_000,
    }),
  }))
  await page.route('**/player/v1/manifest', (route) => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      desiredStateId: '11111111-1111-1111-1111-111111111111',
      version: 7,
      assets: [{
        contentVersionId: '22222222-2222-2222-2222-222222222222',
        position: 0,
        mediaKind: 'plainText',
        durationMilliseconds: null,
        loopVideo: false,
        url: '/player/v1/assets/message.txt',
      }],
    }),
  }))
  await page.route('**/player/v1/assets/message.txt', (route) => route.fulfill({
    status: 200,
    contentType: 'text/plain; charset=utf-8',
    body: 'Contenu autorisé par le manifeste local',
  }))

  await page.goto('http://127.0.0.1:4174/')
  await expect(page.getByText('Contenu autorisé par le manifeste local')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Not licensed' })).toHaveCount(0)
})
