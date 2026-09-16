import { chromium } from '@playwright/test'
import { mkdir } from 'node:fs/promises'

const email = process.env.DEMO_EMAIL ?? 'admin@demo.local'
const password = process.env.DEMO_PASSWORD
if (!password) throw new Error('Set DEMO_PASSWORD for the recording run.')

await mkdir('demo', { recursive: true })
const browser = await chromium.launch({
  headless: true,
  executablePath: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
})
const context = await browser.newContext({
  viewport: { width: 1440, height: 900 },
  recordVideo: { dir: 'demo', size: { width: 1440, height: 900 } },
})
await context.route('http://127.0.0.1:8787/**', async (route) => {
  const response = await route.fetch()
  const headers = response.headers()
  delete headers['content-security-policy']
  await route.fulfill({ response, headers })
})
const page = await context.newPage()
const video = page.video()
page.on('dialog', async (dialog) => { await dialog.accept() })
await page.goto('http://127.0.0.1:5173', { waitUntil: 'domcontentloaded' })
await page.setContent(`
  <style>
    * { box-sizing: border-box; }
    body { margin: 0; background: #101923; color: #eef5f7; font: 14px system-ui, sans-serif; overflow: hidden; }
    header { height: 48px; display: flex; align-items: center; padding: 0 18px; background: #08111a; border-bottom: 1px solid #2a3c4a; font-weight: 700; letter-spacing: .02em; }
    .layout { height: calc(100vh - 48px); display: grid; grid-template-columns: 70% 30%; gap: 8px; padding: 8px; }
    iframe { width: 100%; height: 100%; border: 1px solid #37505f; border-radius: 8px; background: white; }
  </style>
  <header>Display Control — SQL Server complete workflow (no manual display refresh)</header>
  <div class="layout"><iframe id="admin" title="Admin interface" src="http://127.0.0.1:5173"></iframe><iframe id="player" title="Display simulator" src="http://127.0.0.1:8787"></iframe></div>
`)

const admin = page.frameLocator('#admin')
const player = page.frameLocator('#player')
await admin.locator('input[name="email"]').waitFor()
await admin.locator('input[name="email"]').fill(email)
await admin.locator('input[name="password"]').fill(password)
await admin.getByRole('button', { name: 'Se connecter' }).click()
await admin.locator('#view-dashboard').waitFor()
await page.waitForTimeout(1200)

const tenantId = '9cf7b91a-b4c3-45a5-b8b9-516aeb022a45'
const deviceId = 'eb608ef7-d568-4925-8d5d-dcfd075ef795'
const groupId = '7285b135-d3a8-49ed-8ba2-52869532090b'
const adminFrame = page.frames().find((frame) => frame.url().startsWith('http://127.0.0.1:5173'))
if (!adminFrame) throw new Error('The administration frame is unavailable.')
const playerFrame = page.frames().find((frame) => frame.url().startsWith('http://127.0.0.1:8787'))
if (!playerFrame) throw new Error('The display frame is unavailable.')
async function readPlayerState() {
  return playerFrame.evaluate(async () => {
    const response = await fetch('/player/v1/state', { cache: 'no-store' })
    if (!response.ok) throw new Error(`Player state returned ${response.status}.`)
    return response.json()
  })
}
async function waitForPlayerState(predicate, description, timeoutMilliseconds = 20_000) {
  const deadline = Date.now() + timeoutMilliseconds
  while (Date.now() < deadline) {
    const state = await readPlayerState()
    if (predicate(state)) return state
    await page.waitForTimeout(250)
  }
  throw new Error(`Timed out waiting for player state: ${description}.`)
}
async function nextGlobalPriority() {
  const priorities = await adminFrame.evaluate(async ({ tenantId, deviceId, groupId }) => {
    const responses = await Promise.all([
      fetch(`/api/v1/tenants/${tenantId}/devices/${deviceId}/assignments`),
      fetch(`/api/v1/tenants/${tenantId}/device-groups/${groupId}/assignments`),
    ])
    if (responses.some((response) => !response.ok)) throw new Error('Unable to inspect current assignment priorities.')
    const assignments = (await Promise.all(responses.map((response) => response.json()))).flat()
    return assignments.map((assignment) => assignment.priority)
  }, { tenantId, deviceId, groupId })
  const priority = Math.max(-1001, ...priorities) + 1
  if (priority > 1000) throw new Error('No higher demo assignment priority is available.')
  return priority
}

await admin.getByRole('link', { name: 'Devices' }).click()
await admin.locator('#view-devices').waitFor()
await admin.getByRole('button', { name: /Virtual Raspberry Pi SQLServer/ }).click()
await admin.getByRole('dialog').waitFor()
await page.waitForTimeout(1200)
await admin.getByRole('button', { name: 'Fermer' }).click()

await admin.getByRole('link', { name: 'Groups' }).click()
await admin.locator('#view-groups').waitFor()
await admin.locator('button').filter({ hasText: 'Groupes enregistr' }).last().click()
await page.waitForTimeout(1000)

await admin.getByRole('link', { name: 'Content' }).click()
await admin.locator('#view-content').waitFor()
await admin.locator('button').filter({ hasText: 'Contenus enregistr' }).last().click()
await admin.locator('#saved-content-title').waitFor()
await admin.getByRole('button', { name: 'Aperçu' }).first().click()
await admin.getByRole('dialog').waitFor()
await page.waitForTimeout(1000)
await admin.getByRole('button', { name: 'Fermer' }).click()

await admin.getByRole('link', { name: 'Playlists / Publishing' }).click()
await admin.locator('#view-playlists').waitFor()
await admin.locator('button').filter({ hasText: 'Playlists enregistr' }).last().click()
await admin.locator('#saved-playlists-title').waitFor()
await page.waitForTimeout(1200)
await admin.locator('button').filter({ hasText: 'Créer et publier' }).last().click()

const publishing = admin.locator('form.publishing-form')
await publishing.locator('select[name="sourceId"]').selectOption('a1c7c3d4-834b-4f3b-8572-bb012d143881')
await publishing.locator('select[name="targetId"]').selectOption(deviceId)
await publishing.locator('input[name="priority"]').fill(String(await nextGlobalPriority()))
const versionBeforeContentA = (await readPlayerState()).desiredStateVersion
await publishing.getByRole('button', { name: 'Publier' }).click()
await admin.getByText(/Publication/).last().waitFor()
await waitForPlayerState(
  (state) => state.status === 'ready' && state.desiredStateVersion > versionBeforeContentA,
  'Content A desired-state version',
)
await page.waitForTimeout(1000)

await publishing.locator('select[name="sourceId"]').selectOption('09b21c39-99c2-440e-9e87-6f57e7d619a6')
await publishing.locator('select[name="targetId"]').selectOption(deviceId)
await publishing.locator('input[name="priority"]').fill(String(await nextGlobalPriority()))
const versionBeforeContentB = (await readPlayerState()).desiredStateVersion
await publishing.getByRole('button', { name: 'Publier' }).click()
await waitForPlayerState(
  (state) => state.status === 'ready' && state.desiredStateVersion > versionBeforeContentB,
  'Content B desired-state version',
)
await page.waitForTimeout(1000)

await admin.getByRole('link', { name: 'Groups' }).click()
await admin.locator('#view-groups').waitFor()
await admin.locator('button').filter({ hasText: 'Gérer les groupes' }).last().click()
const groupPublish = admin.locator('form').filter({ has: admin.getByRole('heading', { name: /Publier pour un groupe/ }) })
await groupPublish.locator('select[name="groupId"]').selectOption(groupId)
await groupPublish.locator('select[name="playlistVersionId"]').selectOption('09b21c39-99c2-440e-9e87-6f57e7d619a6')
await groupPublish.locator('input[name="priority"]').fill(String(await nextGlobalPriority()))
await groupPublish.getByRole('button', { name: 'Publier le planning' }).click()
await page.waitForTimeout(1800)

await admin.getByRole('link', { name: 'Licenses' }).click()
await admin.locator('#view-licenses').waitFor()
await admin.locator('button').filter({ hasText: 'Registre des licences' }).last().click()
await admin.locator('button.danger-button').first().click()
await page.waitForTimeout(2500)
await player.getByRole('heading', { name: 'Not licensed' }).waitFor()
await page.waitForTimeout(1000)

await admin.getByRole('link', { name: 'Devices' }).click()
await admin.locator('#view-devices').waitFor()
await admin.locator('.device-card-unlicensed').filter({ hasText: 'Virtual Raspberry Pi SQLServer' }).waitFor()
await page.waitForTimeout(1000)

await admin.getByRole('link', { name: 'Licenses' }).click()
await admin.locator('#view-licenses').waitFor()
await admin.locator('button').filter({ hasText: 'Gérer les licences' }).last().click()
const licenseForm = admin.locator('form.inline-form').filter({ has: admin.getByRole('button', { name: 'Créer la licence' }) })
await licenseForm.locator('select[name="deviceId"]').selectOption(deviceId)
await licenseForm.locator('input[name="expiresAt"]').fill('2030-01-01T12:00')
await licenseForm.getByRole('button', { name: 'Créer la licence' }).click()
await waitForPlayerState((state) => state.status === 'ready', 'licence recovery')
await player.getByRole('heading', { name: 'Not licensed' }).waitFor({ state: 'detached' })

await admin.getByRole('link', { name: 'Playlists / Publishing' }).click()
await admin.locator('#view-playlists').waitFor()
await admin.locator('button').filter({ hasText: 'Créer et publier' }).last().click()
const resumePublishing = admin.locator('form.publishing-form')
await resumePublishing.locator('select[name="sourceId"]').selectOption('09b21c39-99c2-440e-9e87-6f57e7d619a6')
await resumePublishing.locator('select[name="targetId"]').selectOption(deviceId)
await resumePublishing.locator('input[name="priority"]').fill(String(await nextGlobalPriority()))
const versionBeforeRecoveryPublish = (await readPlayerState()).desiredStateVersion
await resumePublishing.getByRole('button', { name: 'Publier' }).click()
await resumePublishing.locator('.form-notice').waitFor({ state: 'visible', timeout: 20_000 })
await waitForPlayerState(
  (state) => state.status === 'ready' && state.desiredStateVersion > versionBeforeRecoveryPublish,
  'post-revocation recovery publication',
)
await page.waitForTimeout(1000)

await admin.getByRole('link', { name: 'Devices' }).click()
await admin.locator('#view-devices').waitFor()
await admin.locator('.device-card-licensed').filter({ hasText: 'Virtual Raspberry Pi SQLServer' }).waitFor()
await page.waitForTimeout(1500)

await context.close()
if (!video) throw new Error('Playwright video capture was not available.')
await video.saveAs('demo/PiLicenseManager_Complete_Workflow_Demo_SQLServer.webm')
await video.delete()
await browser.close()
console.log('recording complete: demo/PiLicenseManager_Complete_Workflow_Demo_SQLServer.webm')
