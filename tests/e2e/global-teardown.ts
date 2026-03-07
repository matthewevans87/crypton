import * as fs from 'fs';
import * as path from 'path';

const MANAGED_PROCESSES_FILE = path.join(__dirname, '.managed-pids.json');

export default async function globalTeardown(): Promise<void> {
  const managed = (process.env.TEST_MANAGED_SERVICES ?? 'false').toLowerCase() === 'true';
  if (!managed) return;

  if (!fs.existsSync(MANAGED_PROCESSES_FILE)) return;

  const pids: number[] = JSON.parse(fs.readFileSync(MANAGED_PROCESSES_FILE, 'utf8'));
  console.log('[global-teardown] Stopping managed services...');

  for (const pid of pids) {
    try {
      process.kill(pid, 'SIGTERM');
    } catch {
      // process already dead, that's fine
    }
  }

  // Also kill known test service DLLs to handle dotnet watch spawned children
  for (const dll of [
    'Crypton.Api.MarketData.dll',
    'Crypton.Api.ExecutionService.dll',
    'Crypton.Api.AgentRunner.dll',
    'Crypton.Api.MonitoringDashboard.dll',
  ]) {
    try {
      // pkill -f is Linux-specific; acceptable since we target Linux dev/CI
      const { execSync } = await import('child_process');
      execSync(`pkill -f "${dll}" 2>/dev/null || true`, { shell: '/bin/bash' });
    } catch {
      // ignore
    }
  }

  fs.unlinkSync(MANAGED_PROCESSES_FILE);
  console.log('[global-teardown] Services stopped.');
}
