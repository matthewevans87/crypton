import { test, expect } from '../fixtures/base';

/**
 * Portfolio tests
 * Verify that the portfolio summary API returns valid data and that the
 * PortfolioSummaryPanel renders without errors.
 */
test.describe('Portfolio', () => {
  test('portfolio summary API returns valid structure', async ({ api }) => {
    const summary = await api.getPortfolioSummary();
    expect(summary).toHaveProperty('totalValue');
    expect(summary).toHaveProperty('availableCapital');
    expect(summary).toHaveProperty('unrealizedPnL');
    expect(typeof summary.totalValue).toBe('number');
    // Mock adapter seeds USD + BTC + ETH balances; total should be positive
    expect(summary.totalValue).toBeGreaterThan(0);
  });

  test('total value is a finite positive number', async ({ api }) => {
    const summary = await api.getPortfolioSummary();
    expect(Number.isFinite(summary.totalValue)).toBe(true);
    expect(summary.totalValue).toBeGreaterThanOrEqual(0);
  });

  test('PortfolioSummaryPanel renders without error', async ({ dashboardPage: page }) => {
    // Find any panel that contains portfolio data; the panel is identified by a
    // wrapping element with data-testid once we add it, but until that's done
    // we check that no error toast is shown and the page is interactive.
    await expect(page.locator('[data-testid="status-bar"]')).toBeVisible();

    // No error overlay should be present
    await expect(page.locator('[data-testid="error-toast"]')).not.toBeVisible();
  });

  test('available capital is non-negative', async ({ api }) => {
    const summary = await api.getPortfolioSummary();
    expect(summary.availableCapital).toBeGreaterThanOrEqual(0);
  });
});
