#!/usr/bin/env bash
set -euo pipefail

VERSION="0.19.1"
RELEASE_URL="https://github.com/restic/restic/releases/download/v${VERSION}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PINNED_MANIFEST="${SCRIPT_DIR}/restic-SHA256SUMS.txt"
PINNED_MANIFEST_SHA="${SCRIPT_DIR}/restic-SHA256SUMS.sha256"
OUTPUT_ROOT="${ROOT_DIR}/desktop/vendor/restic"

die() {
  printf 'fetch-restic: %s\n' "$*" >&2
  exit 1
}

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print toupper($1)}'
  else
    shasum -a 256 "$1" | awk '{print toupper($1)}'
  fi
}

normalize_arch() {
  case "$1" in
    arm64|aarch64) printf 'arm64\n' ;;
    x64|amd64|x86_64) printf 'x64\n' ;;
    *) die "unsupported macOS architecture '$1' (use arm64 or x64)" ;;
  esac
}

requested_arch=""
if [[ "${1:-}" == "--arch" ]]; then
  [[ -n "${2:-}" ]] || die '--arch requires arm64 or x64'
  requested_arch="$2"
elif [[ -n "${1:-}" ]]; then
  requested_arch="$1"
fi

arch="$(normalize_arch "${requested_arch:-$(uname -m)}")"
case "$arch" in
  arm64) asset="restic_${VERSION}_darwin_arm64.bz2" ;;
  x64) asset="restic_${VERSION}_darwin_amd64.bz2" ;;
esac

[[ -f "$PINNED_MANIFEST" ]] || die "missing checked-in hash manifest: $PINNED_MANIFEST"
[[ -f "$PINNED_MANIFEST_SHA" ]] || die "missing checked-in manifest hash: $PINNED_MANIFEST_SHA"

expected_manifest_sha="$(tr -d '[:space:]' < "$PINNED_MANIFEST_SHA" | tr '[:lower:]' '[:upper:]')"
actual_manifest_sha="$(sha256_file "$PINNED_MANIFEST")"
[[ "$actual_manifest_sha" == "$expected_manifest_sha" ]] || die 'checked-in hash manifest has been modified'

expected_archive_sha="$(awk -v name="$asset" '$2 == name {print toupper($1)}' "$PINNED_MANIFEST")"
[[ "$expected_archive_sha" =~ ^[0-9A-F]{64}$ ]] || die "no pinned checksum for $asset"

tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/rewindle-restic.XXXXXX")"
cleanup() { rm -rf "$tmp_dir"; }
trap cleanup EXIT

remote_manifest="$tmp_dir/SHA256SUMS"
archive="$tmp_dir/$asset"
printf 'Fetching and authenticating Restic %s (%s)\n' "$VERSION" "$arch"
curl --fail --location --silent --show-error --retry 3 --retry-delay 2 \
  --proto '=https' --tlsv1.2 "$RELEASE_URL/SHA256SUMS" -o "$remote_manifest"
remote_manifest_sha="$(sha256_file "$remote_manifest")"
[[ "$remote_manifest_sha" == "$expected_manifest_sha" ]] || die 'official SHA256SUMS does not match the pinned release manifest'

remote_archive_sha="$(awk -v name="$asset" '$2 == name {print toupper($1)}' "$remote_manifest")"
[[ "$remote_archive_sha" == "$expected_archive_sha" ]] || die "official SHA256SUMS entry for $asset changed"

curl --fail --location --silent --show-error --retry 3 --retry-delay 2 \
  --proto '=https' --tlsv1.2 "$RELEASE_URL/$asset" -o "$archive"
actual_archive_sha="$(sha256_file "$archive")"
[[ "$actual_archive_sha" == "$expected_archive_sha" ]] || die "archive checksum mismatch for $asset"

target_dir="$OUTPUT_ROOT/$arch"
mkdir -p "$target_dir"
bzip2 --decompress --stdout "$archive" > "$target_dir/restic"
chmod 755 "$target_dir/restic"
printf '%s\n' "$expected_archive_sha" > "$target_dir/restic.sha256"
printf '%s\n' "${VERSION} ${asset} ${expected_archive_sha}" > "$target_dir/version.txt"
# Keep an architecture-specific copy for CI evidence and a single active copy
# for electron-builder's extraResources entry.  Each CI architecture job has
# its own workspace, so this cannot accidentally mix binaries in one artifact.
cp "$target_dir/restic" "$OUTPUT_ROOT/restic"
cp "$target_dir/restic.sha256" "$OUTPUT_ROOT/restic.sha256"
chmod 755 "$OUTPUT_ROOT/restic"
printf 'Restic %s staged at %s\n' "$VERSION" "$target_dir/restic"
