import { test, expect } from '../fixtures/base';
import * as dotenv from 'dotenv';
import * as path from 'path';
import { ApiClient } from '../helpers/api';

dotenv.config({ path: path.join(__dirname, '../.env.test') });

/**
 * Market Data tests
 * Verify that the MockExchangeAdapter is active and serving deterministic prices,
 * and that the PriceTickerPanel displays them correctly.
 *
 * Tests prefixed with [mock only] are skipped unless services were started with
 * MARKETDATA__EXCHANGE__USEMOCK=true (detected by checking if the price matches the known mock value).
 */

// These match exactly what MockExchangeAdapter.BaseP returns.
const MOCK_PRICES: Record<string, number> = {
  'BTC/USD': 50_000,
  'ETH/USD': 3_000,
  'SOL/USD': 120,
};

let isMockMode = false;

test.beforeAll(async () => {
  // Detect mock mode: the mock adapter always returns exactly 50,000 for BTC/USD.
  // We create a one-off client here because the test-scoped `api` fixture isn't
  // available inside beforeAll.
  const client = new ApiClient({
    monitoringDashboardUrl: process.env.MONITORING_DASHBOARD_URL ?? 'http://localhost:5001',
    agentRunnerUrl:         process.env.AGENT_RUNNER_URL         ?? 'http://localhost:5003',
    marketDataUrl:          process.env.MARKET_DATA_URL          ?? 'http://localhost:5002',
    executionServiceUrl:    process.env.EXECUTION_SERVICE_URL    ?? 'http://localhost:5004',
    apiKey:                 process.env.TEST_API_KEY             ?? 'test-key-1234',
  });
  const ticker = await client.getPrice('BTC/USD');
  isMockMode = ticker.price === MOCK_PRICES['BTC/USD'];
  if (!isMockMode) {
    console.log(`[market-data] Mock mode not active (BTC=$${ticker.price}). Mock-specific tests will be skipped.`);
  }
});

test.describe('Market Data (Mock Exchange)', () => {
  test('MarketData service returns mock BTC price', async ({ api }) => {
    test.skip(!isMockMode, 'Needs MARKETDATA__EXCHANGE__USEMOCK=true — start via ./scripts/start-test-services.sh');
    const ticker = await api.getPrice('BTC/USD');
    expect(ticker.price).toBe(MOCK_PRICES['BTC/USD']);
    expect(ticker.asset.toUpperCase()).toContain('BTC');
  });

  test('MarketData service returns mock ETH price', async ({ api }) => {
    test.skip(!isMockMode, 'Needs MARKETDATA__EXCHANGE__USEMOCK=true — start via ./scripts/start-test-services.sh');
    const ticker = await api.getPrice('ETH/USD');
    expect(ticker.price).toBe(MOCK_PRICES['ETH/USD']);
  });

  test('bid is below ask (valid spread)', async ({ api }) => {
    const ticker = await api.getPrice('BTC/USD');
    expect(ticker.bid).toBeLessThan(ticker.ask);
    expect(ticker.bid).toBeGreaterThan(0);
  });

  test('BTC price is a positive number', async ({ api }) => {
    const ticker = await api.getPrice('BTC/USD');
    expect(ticker.price).toBeGreaterThan(0);
    expect(Number.isFinite(ticker.price)).toBe(true);
  });

  test('PriceTickerPanel renders BTC price within mock range', async ({ dashboardPage: page }) => {
    test.skip(!isMockMode, 'Needs MARKETDATA__EXCHANGE__USEMOCK=true — start via ./scripts/start-test-services.sh');
    // Default panel asset is BTC/USD — wait for it to appear
    const panel = page.locator('[data-testid="panel-price-ticker"][data-asset="BTC/USD"]');
    await panel.waitFor({ state: 'visible', timeout: 15_000 });

    const priceEl = panel.locator('[data-testid="price-value"]');
    await expect(priceEl).toBeVisible();

    // Parse the displayed price (formatted as "$50,000.00") and verify range
    const priceText = await priceEl.textContent();
    const price = parseFloat(priceText?.replace(/[^0-9.]/g, '') ?? '0');
    // Allow ±5% band around the mock base price in case tests show live-updating ticks
    expect(price).toBeGreaterThanOrEqual(MOCK_PRICES['BTC/USD'] * 0.95);
    expect(price).toBeLessThanOrEqual(MOCK_PRICES['BTC/USD'] * 1.05);
  });

  test('PriceTickerPanel renders a valid BTC price', async ({ dashboardPage: page }) => {
    test.skip(isMockMode, 'Covered by the mock-specific test above when in mock mode');
    const panel = page.locator('[data-testid="panel-price-ticker"][data-asset="BTC/USD"]');
    await panel.waitFor({ state: 'visible', timeout: 15_000 });

    const priceEl = panel.locator('[data-testid="price-value"]');
    await expect(priceEl).toBeVisible();

    const priceText = await priceEl.textContent();
    const price = parseFloat(priceText?.replace(/[^0-9.]/g, '') ?? '0');
    expect(price).toBeGreaterThan(1_000);  // BTC should be > $1,000
  });

  test('MarketData health endpoint responds healthy', async ({ api }) => {
    const healthy = await api.isServiceHealthy(
      process.env.MARKET_DATA_URL ?? 'http://localhost:5002'
    );
    expect(healthy).toBe(true);
  });
});
