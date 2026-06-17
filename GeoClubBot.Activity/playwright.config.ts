import { defineConfig, devices } from '@playwright/test';

// E2E runs the Vite dev server in bypass mode (no real Discord SDK); the backend API is mocked
// per-test via route interception, so no running backend or Discord is required.
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: 'list',
  use: {
    baseURL: 'http://localhost:5174',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'npm run dev -- --port 5174 --strictPort',
    url: 'http://localhost:5174',
    reuseExistingServer: !process.env.CI,
    env: { VITE_DEV_BYPASS: 'true' },
  },
});
