#!/usr/bin/env bash
# stop-test-services.sh
# Stops all services started by start-test-services.sh.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PID_FILE="$REPO_ROOT/tests/e2e/.service-pids"

if [[ ! -f "$PID_FILE" ]]; then
  echo "No PID file found at $PID_FILE — services may not be running."
  exit 0
fi

echo "Stopping test services..."

while IFS='=' read -r name pid; do
  if kill -0 "$pid" 2>/dev/null; then
    echo "  Stopping $name (PID $pid)..."
    # Kill the process group so dotnet watch child processes also die
    kill -- "-$pid" 2>/dev/null || kill "$pid" 2>/dev/null || true
  else
    echo "  $name (PID $pid) already stopped"
  fi
done < "$PID_FILE"

# Also kill any lingering dotnet processes spawned by watch
# (targets our specific project DLLs to avoid killing unrelated dotnet processes)
for dll in \
  "Crypton.Api.MarketData.dll" \
  "Crypton.Api.ExecutionService.dll" \
  "Crypton.Api.AgentRunner.dll" \
  "Crypton.Api.MonitoringDashboard.dll"; do
  pkill -f "$dll" 2>/dev/null || true
done

rm -f "$PID_FILE"
echo "Done."
