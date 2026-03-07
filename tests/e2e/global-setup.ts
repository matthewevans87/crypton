import * as dotenv from 'dotenv';
import * as path from 'path';
import * as fs from 'fs';
import { ChildProcess, spawn } from 'child_process';

dotenv.config({ path: path.join(__dirname, '.env.test') });

const SERVICES = [
  { name: 'market-data',        url: process.env.MARKET_DATA_URL         ?? 'http://localhost:5002', healthPath: '/health/live' },
  { name: 'execution-service',  url: process.env.EXECUTION_SERVICE_URL   ?? 'http://localhost:5004', healthPath: '/health/live' },
  { name: 'agent-runner',       url: process.env.AGENT_RUNNER_URL        ?? 'http://localhost:5003', healthPath: '/health/live' },
  { name: 'monitoring-dashboard', url: process.env.MONITORING_DASHBOARD_URL ?? 'http://localhost:5001', healthPath: '/health/live' },
];

const MANAGED_PROCESSES_FILE = path.join(__dirname, '.managed-pids.json');
const REPO_ROOT = path.join(__dirname, '..', '..');

async function pollUntilHealthy(name: string, url: string, timeoutMs = 60_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  const healthUrl = url.replace(/\/$/, '') + '/health/live';

  while (Date.now() < deadline) {
    try {
      const res = await fetch(healthUrl, { signal: AbortSignal.timeout(2000) });
      if (res.ok) return;
    } catch {
      // not yet up
    }
    await new Promise(r => setTimeout(r, 1000));
  }
  throw new Error(
    `[global-setup] Service "${name}" at ${healthUrl} did not become healthy within ${timeoutMs / 1000}s.\n` +
    `  → If running in watch mode, start services first: ./scripts/start-test-services.sh`
  );
}

async function startManagedServices(): Promise<void> {
  const logDir = path.join(__dirname, '.service-logs');
  fs.mkdirSync(logDir, { recursive: true });

  type ServiceDef = {
    name: string;
    project: string;
    env: Record<string, string>;
  };

  const testApiKey = process.env.TEST_API_KEY ?? 'test-key-1234';
  const defs: ServiceDef[] = [
    {
      name: 'market-data',
      project: path.join(REPO_ROOT, 'src/Crypton.Api.MarketData/Crypton.Api.MarketData.csproj'),
      env: {
        ASPNETCORE_URLS: 'http://localhost:5002',
        ASPNETCORE_ENVIRONMENT: 'Development',
        MARKETDATA__EXCHANGE__USEMOCK: 'true',
      },
    },
    {
      name: 'execution-service',
      project: path.join(REPO_ROOT, 'src/Crypton.Api.ExecutionService/Crypton.Api.ExecutionService.csproj'),
      env: {
        ASPNETCORE_URLS: 'http://localhost:5004',
        ASPNETCORE_ENVIRONMENT: 'Development',
        EXECUTIONSERVICE__API__APIKEY: testApiKey,
        EXECUTIONSERVICE__MARKETDATASERVICEURL: 'http://localhost:5002',
      },
    },
    {
      name: 'agent-runner',
      project: path.join(REPO_ROOT, 'src/Crypton.Api.AgentRunner/Crypton.Api.AgentRunner.csproj'),
      env: {
        ASPNETCORE_URLS: 'http://localhost:5003',
        ASPNETCORE_ENVIRONMENT: 'Development',
        AGENTRUNNER__API__APIKEY: testApiKey,
        AGENTRUNNER__TOOLS__MARKETDATASERVICE__BASEURL: 'http://localhost:5002',
        AGENTRUNNER__TOOLS__EXECUTIONSERVICE__BASEURL: 'http://localhost:5004',
        AGENTRUNNER__CYCLE__SCHEDULEINTERVALMINUTES: '99999',
      },
    },
    {
      name: 'monitoring-dashboard',
      project: path.join(REPO_ROOT, 'src/Crypton.Api.MonitoringDashboard/Crypton.Api.MonitoringDashboard.csproj'),
      env: {
        ASPNETCORE_URLS: 'http://localhost:5001',
        ASPNETCORE_ENVIRONMENT: 'Development',
        MONITORINGDASHBOARD__AGENTRUNNER__URL: 'http://localhost:5003',
        MONITORINGDASHBOARD__AGENTRUNNER__APIKEY: testApiKey,
        MONITORINGDASHBOARD__EXECUTIONSERVICE__URL: 'http://localhost:5004',
        MONITORINGDASHBOARD__MARKETDATASERVICE__URL: 'http://localhost:5002',
      },
    },
  ];

  const pids: number[] = [];

  for (const def of defs) {
    console.log(`[global-setup] Starting ${def.name}...`);
    const logStream = fs.createWriteStream(path.join(logDir, `${def.name}.log`));

    const proc: ChildProcess = spawn('dotnet', ['run', '--project', def.project], {
      env: { ...process.env, ...def.env },
      detached: true,
      stdio: ['ignore', 'pipe', 'pipe'],
    });

    if (proc.stdout) proc.stdout.pipe(logStream);
    if (proc.stderr) proc.stderr.pipe(logStream);
    proc.unref();

    if (proc.pid) pids.push(proc.pid);

    const svc = SERVICES.find(s => s.name === def.name)!;
    await pollUntilHealthy(def.name, svc.url, 90_000);
    console.log(`[global-setup] ${def.name} ready`);
  }

  fs.writeFileSync(MANAGED_PROCESSES_FILE, JSON.stringify(pids));
}

export default async function globalSetup(): Promise<void> {
  const managed = (process.env.TEST_MANAGED_SERVICES ?? 'false').toLowerCase() === 'true';

  if (managed) {
    console.log('[global-setup] Managed mode: starting services...');
    await startManagedServices();
  } else {
    console.log('[global-setup] Watch mode: checking services are up...');
    // Verify all services are healthy before tests start; give a helpful error if not.
    await Promise.all(
      SERVICES.map(svc => pollUntilHealthy(svc.name, svc.url, 10_000))
    );
    console.log('[global-setup] All services healthy.');
  }
}
