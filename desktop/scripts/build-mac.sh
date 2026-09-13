#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
ARCH="${1:-$(uname -m)}"
case "$ARCH" in
  arm64|aarch64) ARCH=arm64 ;;
  x64|amd64|x86_64) ARCH=x64 ;;
  *) printf 'build-mac: use arm64 or x64\n' >&2; exit 2 ;;
esac

cd "$ROOT_DIR"
"$SCRIPT_DIR/prepare-web.sh"
"$SCRIPT_DIR/fetch-restic.sh" --arch "$ARCH"

mkdir -p desktop/vendor/restic
rm -f desktop/vendor/restic/restic
cp "desktop/vendor/restic/$ARCH/restic" desktop/vendor/restic/restic
chmod 755 desktop/vendor/restic/restic

npm run dist:mac -- --${ARCH}
printf 'DMG and ZIP artifacts are in %s\n' "$ROOT_DIR/desktop/dist"
