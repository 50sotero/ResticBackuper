#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
WEB_DIR="${ROOT_DIR}/src/dashboard/web"
WEB_INDEX="${ROOT_DIR}/src/dashboard/dist/web/index.html"

if [[ -f "${WEB_DIR}/package.json" ]]; then
  command -v npm >/dev/null 2>&1 || { printf 'prepare-web: npm is required to build the shared React client\n' >&2; exit 1; }
  npm --prefix "$WEB_DIR" ci
  npm --prefix "$WEB_DIR" run build
  node "$WEB_DIR/scripts/collect-licenses.mjs"
fi

if [[ ! -f "$WEB_INDEX" ]]; then
  printf 'prepare-web: expected the shared Beautiful UI build at %s\n' "$WEB_INDEX" >&2
  printf 'Build the frontend with src/dashboard/web/package.json before packaging.\n' >&2
  exit 1
fi

printf 'Rewindle web client ready: %s\n' "$WEB_INDEX"
