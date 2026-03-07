#!/usr/bin/env bash
# start-test-services.sh
# Starts all 4 backend services in watch mode for E2E testing.
# Services use mock exchange data, deterministic test API keys, and
# a very long cycle interval so the agent never auto-runs during tests.
#
# Usage:
#   ./scripts/start-test-services.sh          # watch mode (restarts on file changes)
#   ./scripts/start-test-services.sh --no-watch  # run mode (faster start, no file watching)
#
# After running, execute:
#   cd tests/e2e && npx playwright test
#
# To stop all services:
#   ./scripts/stop-test-services.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOG_DIR="$REPO_ROOT/tests/e2e/.service-logs"
PID_FILE="$REPO_ROOT/tests/e2e/.service-pids"
ENV_FILE="$REPO_ROOT/tests/e2e/.env.test"

# Load .env.test for shared config
if [[ -f "$ENV_FILE" ]]; then
  # shellcheck disable=SC2046
  export $(grep -v '^#' "$ENV_FILE" | grep -v '^[[:space:]]*$' | xargs)
fi

WATCH_FLAG="watch"
for arg in "$@"; do
  [[ "$arg" == "--no-watch" ]] && WATCH_FLAG="run"
done

mkdir -p "$LOG_DIR"
rm -f "$PID_FILE"

# ---------------------------------------------------------------------------
# Helper: start a single service and record PIDs
# ---------------------------------------------------------------------------
start_service() {
  local name="$1"
  local project_path="$2"
  shift 2
  # Remaining args are env var overrides: KEY=value KEY2=value2 ...
  local env_overrides=("$@")

  local log_file="$LOG_DIR/${name}.log"

  echo "  Starting $name..."

  # Build the env prefix for the command
  local env_prefix=""
  for kv in "${env_overrides[@]}"; do
    env_prefix="$kv $env_prefix"
  done

  # Use 'dotnet watch run' or 'dotnet run' depending on mode
  local cmd
  if [[ "$WATCH_FLAG" == "watch" ]]; then
    cmd="dotnet watch run --project \"$project_path\" --no-hot-reload"
  else
    cmd="dotnet run --project \"$project_path\""
  fi

  # Launch in background, capture PID
  eval "env $env_prefix $cmd" > "$log_file" 2>&1 &
  local pid=$!
  echo "${name}=${pid}" >> "$PID_FILE"
  echo "    PID $pid → $log_file"
}

# ---------------------------------------------------------------------------
# Helper: wait for a health endpoint to respond 200
# ---------------------------------------------------------------------------
wait_healthy() {
  local name="$1"
  local url="$2"
  local timeout_secs="${3:-60}"
  local elapsed=0

  echo -n "  Waiting for $name to be healthy..."
  while ! curl -sf "$url" > /dev/null 2>&1; do
    sleep 1
    elapsed=$((elapsed + 1))
    if [[ $elapsed -ge $timeout_secs ]]; then
      echo " TIMEOUT after ${timeout_secs}s"
      echo "  Check log: $LOG_DIR/${name}.log"
      exit 1
    fi
    echo -n "."
  done
  echo " OK (${elapsed}s)"
}

# ---------------------------------------------------------------------------
# Service definitions
# ---------------------------------------------------------------------------
echo ""
echo "=== Starting Crypton services in test mode ($WATCH_FLAG) ==="
echo ""

# 1. MarketData — must come first, ExecutionService depends on it
start_service "market-data" \
  "$REPO_ROOT/src/Crypton.Api.MarketData/Crypton.Api.MarketData.csproj" \
  "ASPNETCORE_URLS=http://localhost:5002" \
  "ASPNETCORE_ENVIRONMENT=Development" \
  "EXCHANGE__USE_MOCK=true"

wait_healthy "market-data" "http://localhost:5002/health/live" 60

# 2. ExecutionService
start_service "execution-service" \
  "$REPO_ROOT/src/Crypton.Api.ExecutionService/Crypton.Api.ExecutionService.csproj" \
  "ASPNETCORE_URLS=http://localhost:5004" \
  "ASPNETCORE_ENVIRONMENT=Development" \
  "EXECUTION_SERVICE__API__ApiKey=${TEST_API_KEY:-test-key-1234}" \
  "EXECUTION_SERVICE__MarketDataServiceUrl=http://localhost:5002"

wait_healthy "execution-service" "http://localhost:5004/health/live" 60

# 3. AgentRunner
start_service "agent-runner" \
  "$REPO_ROOT/src/Crypton.Api.AgentRunner/Crypton.Api.AgentRunner.csproj" \
  "ASPNETCORE_URLS=http://localhost:5003" \
  "ASPNETCORE_ENVIRONMENT=Development" \
  "Api__ApiKey=${TEST_API_KEY:-test-key-1234}" \
  "Tools__MarketDataService__BaseUrl=http://localhost:5002" \
  "Tools__ExecutionService__BaseUrl=http://localhost:5004" \
  "Cycle__ScheduleIntervalMinutes=99999"

wait_healthy "agent-runner" "http://localhost:5003/health/live" 60

# 4. MonitoringDashboard
start_service "monitoring-dashboard" \
  "$REPO_ROOT/src/Crypton.Api.MonitoringDashboard/Crypton.Api.MonitoringDashboard.csproj" \
  "ASPNETCORE_URLS=http://localhost:5001" \
  "ASPNETCORE_ENVIRONMENT=Development" \
  "AgentRunner__Url=http://localhost:5003" \
  "AgentRunner__ApiKey=${TEST_API_KEY:-test-key-1234}" \
  "ExecutionService__Url=http://localhost:5004" \
  "MarketDataService__Url=http://localhost:5002"

wait_healthy "monitoring-dashboard" "http://localhost:5001/health/live" 60

# ---------------------------------------------------------------------------
echo ""
echo "=== All services ready ==="
echo ""
echo "  Market Data:         http://localhost:5002"
echo "  Execution Service:   http://localhost:5004"
echo "  Agent Runner:        http://localhost:5003"
echo "  Monitoring Dashboard: http://localhost:5001"
echo ""
echo "Run tests:  cd tests/e2e && npx playwright test"
echo "Stop all:   ./scripts/stop-test-services.sh"
echo ""
