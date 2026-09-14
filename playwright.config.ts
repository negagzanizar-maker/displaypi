import { existsSync } from 'node:fs'
import { defineConfig, devices } from '@playwright/test'

const windowsChromePath = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
const useInstalledChrome = process.platform === 'win32'
  && !process.env.CI
  && existsSync(windowsChromePath)

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'line',
  outputDir: 'artifacts/playwright',
  use: {
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        channel: useInstalledChrome ? 'chrome' : undefined,
      },
    },
  ],
  webServer: [
    {
      command: 'npm run dev --workspace admin-web -- --host 127.0.0.1 --port 4173 --strictPort',
      url: 'http://127.0.0.1:4173',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
    {
      command: 'npm run dev --workspace player-web -- --host 127.0.0.1 --port 4174 --strictPort',
      url: 'http://127.0.0.1:4174',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
  ],
})
