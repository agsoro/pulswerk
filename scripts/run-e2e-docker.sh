#!/usr/bin/env bash
# scripts/run-e2e-docker.sh
#
# Run all Playwright E2E tests inside Docker (no Node.js required on the host).
#
# Usage:
#   bash scripts/run-e2e-docker.sh [playwright-args...]
#
# Examples:
#   bash scripts/run-e2e-docker.sh                       # run all tests
#   bash scripts/run-e2e-docker.sh --update-snapshots    # update visual baselines
#   bash scripts/run-e2e-docker.sh --grep "Billing"      # run matching tests only
#
# Requirements:
#   • Docker running
#   • The test stack running:
#       docker compose -f docker-compose.yml -f docker-compose.test.yml up -d
#
# Network:
#   The Playwright container joins "pulswerk_default" (the compose network) so all
#   services are reachable by container name without port-forwarding:
#     pulswerk      → http://pulswerk:5000
#     auth-proxy    → http://auth-proxy:80  (users)  / http://auth-proxy:81 (admins)
#     browserless   → ws://browserless:3000
#
# Output:
#   HTML report → src/Pulswerk.Dashboard/playwright-report/
#   JSON report → src/Pulswerk.Dashboard/test-results/playwright-report.json
#   Traces      → src/Pulswerk.Dashboard/test-results/

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DASHBOARD_DIR="${REPO_ROOT}/src/Pulswerk.Dashboard"
RESULTS_DIR="${DASHBOARD_DIR}/test-results"
REPORT_DIR="${DASHBOARD_DIR}/playwright-report"

PLAYWRIGHT_VERSION="1.60.0"
PLAYWRIGHT_IMAGE="mcr.microsoft.com/playwright:v${PLAYWRIGHT_VERSION}-jammy"

# Within the pulswerk_default compose network, services are reachable by name.
DASHBOARD_URL="${DASHBOARD_URL:-http://pulswerk:5000}"
# Port 81 on auth-proxy injects Remote-User=admin Remote-Groups=admins headers
ADMIN_PROXY_URL="${ADMIN_PROXY_URL:-http://auth-proxy:81}"
COMPOSE_NETWORK="${COMPOSE_NETWORK:-pulswerk_default}"

mkdir -p "${RESULTS_DIR}" "${REPORT_DIR}"

echo ""
echo "┌─────────────────────────────────────────────────────────────────┐"
echo "│  Pulswerk – Playwright E2E Test Runner (Docker)                 │"
echo "│  Image   : ${PLAYWRIGHT_IMAGE}     │"
echo "│  Network : ${COMPOSE_NETWORK}                            │"
echo "│  Target  : ${DASHBOARD_URL}                          │"
echo "└─────────────────────────────────────────────────────────────────┘"
echo ""

# Verify that the compose network exists
if ! docker network inspect "${COMPOSE_NETWORK}" &>/dev/null; then
  echo "❌  Compose network '${COMPOSE_NETWORK}' not found."
  echo "   Start the test stack first:"
  echo "   docker compose -f docker-compose.yml -f docker-compose.test.yml up -d"
  exit 1
fi

# Verify pulswerk service is reachable on the compose network
echo "▶ Checking pulswerk reachability on ${COMPOSE_NETWORK}…"
if ! docker run --rm --network "${COMPOSE_NETWORK}" curlimages/curl:latest \
    -sf "${DASHBOARD_URL}/plswk/" -o /dev/null 2>/dev/null; then
  echo "⚠️  WARNING: ${DASHBOARD_URL}/plswk/ is not reachable from within Docker."
  echo "   Continuing anyway — individual test timeouts will show the real error."
fi

echo "▶ Running Playwright tests…"
echo ""

PLAYWRIGHT_ARGS="${*}"

docker run --rm \
  --network "${COMPOSE_NETWORK}" \
  -e DASHBOARD_URL="${DASHBOARD_URL}" \
  -e ADMIN_PROXY_URL="${ADMIN_PROXY_URL}" \
  -e CI="${CI:-false}" \
  -v "${DASHBOARD_DIR}:/work" \
  -w /work \
  "${PLAYWRIGHT_IMAGE}" \
  bash -c "
    set -euo pipefail
    echo '  → Installing npm dependencies…'
    npm install --silent --omit=optional 2>&1 | grep -v 'npm notice' || true
    echo '  → Running Playwright…'
    npx playwright test ${PLAYWRIGHT_ARGS} 2>&1
  "

EXIT_CODE=$?

echo ""
if [ "${EXIT_CODE}" -eq 0 ]; then
  echo "✅  All E2E tests passed."
else
  echo "❌  Some E2E tests failed (exit code: ${EXIT_CODE})."
fi

echo ""
echo "Reports:"
echo "  HTML  → ${REPORT_DIR}/index.html"
echo "  JSON  → ${RESULTS_DIR}/playwright-report.json"
echo ""

exit "${EXIT_CODE}"
