import { defineConfig, devices } from '@playwright/test';
import * as dotenv from 'dotenv';
import * as path from 'path';

// Load .env.test so env vars are available throughout the config.
dotenv.config({ path: path.join(__dirname, '.env.test') });

export default defineConfig({
  testDir: './tests',
  fullyParallel: false, // Services are shared; run serially to avoid state conflicts.
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: [
    ['list'],
    ['html', { open: 'never', outputFolder: 'playwright-report' }],
  ],

  use: {
    baseURL: process.env.DASHBOARD_UI_URL ?? 'http://localhost:3000',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'off',
    // Pass X-Api-Key header to all API requests
    extraHTTPHeaders: {
      'X-Api-Key': process.env.TEST_API_KEY ?? 'test-key-1234',
    },
  },

  // Start the React dev server if it isn't already running.
  webServer: {
    command: 'npm run dev',
    cwd: path.join(__dirname, '../../src/Crypton.Web.Dashboard'),
    port: 3000,
    reuseExistingServer: true, // Key: skip restart if already running (watch mode).
    timeout: 60_000,
    env: {
      VITE_API_BASE_URL: '',  // Use /api proxy (dev server proxies to localhost:5001)
      VITE_ENABLE_WEBSOCKET: 'true',
    },
  },

  globalSetup: './global-setup.ts',
  globalTeardown: './global-teardown.ts',

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
