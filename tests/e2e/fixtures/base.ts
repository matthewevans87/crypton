import { test as base, expect, Page } from '@playwright/test';
import * as dotenv from 'dotenv';
import * as path from 'path';
import { ApiClient } from '../helpers/api';

dotenv.config({ path: path.join(__dirname, '../.env.test') });

type CryptonFixtures = {
  /** Pre-authenticated API client for REST calls during test setup/assertions */
  api: ApiClient;
  /** Navigate to the dashboard and wait for SignalR to connect */
  dashboardPage: Page;
};

export const test = base.extend<CryptonFixtures>({
  api: async ({}, use) => {
    const client = new ApiClient({
      monitoringDashboardUrl: process.env.MONITORING_DASHBOARD_URL ?? 'http://localhost:5001',
      agentRunnerUrl:         process.env.AGENT_RUNNER_URL         ?? 'http://localhost:5003',
      marketDataUrl:          process.env.MARKET_DATA_URL          ?? 'http://localhost:5002',
      executionServiceUrl:    process.env.EXECUTION_SERVICE_URL    ?? 'http://localhost:5004',
      apiKey:                 process.env.TEST_API_KEY             ?? 'test-key-1234',
    });
    await use(client);
  },

  dashboardPage: async ({ page }, use) => {
    await page.goto('/');
    // Wait for the StatusBar connection indicator to show "connected"
    await page.locator('[data-testid="status-connected"]').waitFor({ state: 'visible', timeout: 15_000 });
    await use(page);
  },
});

export { expect };
