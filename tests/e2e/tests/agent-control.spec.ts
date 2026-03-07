import { test, expect } from '../fixtures/base';

/**
 * Agent Control tests
 * Verify that the /api/control endpoints on AgentRunner work correctly and
 * that the UI reflects the resulting state changes.
 */
test.describe('Agent Control', () => {
  test.beforeEach(async ({ api }) => {
    // Ensure agent starts each test in a known state: attempt resume so it's
    // running (or at idle ready to run). If it's already running this is a no-op.
    try {
      await api.resumeAgent();
    } catch {
      // agent may already be running, or endpoint may return 409 — that's fine
    }
  });

  test('agent is reachable and returns a state', async ({ api }) => {
    const status = await api.getAgentStatus();
    expect(status).toHaveProperty('currentState');
    expect(typeof status.currentState).toBe('string');
    expect(status.currentState.length).toBeGreaterThan(0);
  });

  test('AgentStatePanel renders current state', async ({ dashboardPage: page }) => {
    const panel = page.locator('[data-testid="panel-agent-state"]');
    await panel.waitFor({ state: 'visible', timeout: 10_000 });

    const stateLabel = page.locator('[data-testid="agent-current-state"]');
    // Wait up to 20s: the panel renders immediately but agent data loads asynchronously
    await expect(stateLabel).toBeVisible({ timeout: 20_000 });
    // The state should be a non-empty string (Idle, WaitingForNextCycle, etc.)
    const stateText = await stateLabel.textContent();
    expect(stateText?.trim().length).toBeGreaterThan(0);
  });

  test('pause agent via API and verify state', async ({ api }) => {
    const initial = await api.getAgentStatus();
    const pausableStates = ['WaitingForNextCycle', 'Plan', 'Research', 'Analyze', 'Synthesize'];
    test.skip(
      !pausableStates.includes(initial.currentState),
      `Agent is in "${initial.currentState}" — must be in a running state to pause`
    );

    await api.pauseAgent();

    // Poll until currentState transitions to "Paused"
    await expect.poll(
      () => api.getAgentStatus().then(s => s.currentState),
      { timeout: 10_000, intervals: [500] }
    ).toBe('Paused');
  });

  test('resume agent after pause via API', async ({ api }) => {
    const initial = await api.getAgentStatus();
    const pausableStates = ['WaitingForNextCycle', 'Plan', 'Research', 'Analyze', 'Synthesize'];
    test.skip(
      !pausableStates.includes(initial.currentState),
      `Agent is in "${initial.currentState}" — must be in a running state to pause/resume`
    );

    await api.pauseAgent();
    await expect.poll(
      () => api.getAgentStatus().then(s => s.currentState),
      { timeout: 10_000, intervals: [500] }
    ).toBe('Paused');

    await api.resumeAgent();
    await expect.poll(
      () => api.getAgentStatus().then(s => s.currentState),
      { timeout: 10_000, intervals: [500] }
    ).not.toBe('Paused');
  });
});
