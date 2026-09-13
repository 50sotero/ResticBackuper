# ResticBackuper v0.1.0-alpha.7

Alpha.7 hardens direct-cloud verification for the single live Restic
repository below Google Drive for desktop's **My Drive** streaming mount. This
remains an unsigned Windows x64 alpha. Keep independent recovery material,
another backup appropriate to your threat model, and test representative
restores before relying on it.

## What changed

- The verifier now obtains `restic snapshots --json` through the read-only
  direct-cloud backend and requires a top-level JSON array.
- Windows PowerShell 5.1's non-enumerated `ConvertFrom-Json` array result is
  explicitly normalized before validation. Multi-snapshot repositories are no
  longer misread as one object with member-enumerated fields.
- Every snapshot must be an object with a unique full lowercase Restic ID and a
  string RFC3339 timestamp. Wrong JSON shapes, missing fields, duplicate IDs,
  invalid calendar values, malformed offsets, and unsupported precision fail
  closed.
- RFC3339Nano ordering preserves all nine fractional digits and normalizes
  numeric offsets to UTC. The verifier does not truncate to .NET's seven-digit
  tick precision.
- The snapshot recorded by protected successful-backup evidence must be among
  the newest timestamps in the direct-cloud repository before the canary
  restore runs or immutable cloud evidence is published.
- Verification evidence now records the validated snapshot timestamp, the
  direct-cloud snapshot count, and successful latest-snapshot binding.

## Architecture retained from alpha.6

- `google_drivefs_stream` remains an explicit opt-in mode with one live
  encrypted repository below the configured My Drive streaming root.
- Credentials, protected state, the recovery key, portable recovery tools, and
  cloud-verification assets remain on NTFS outside DriveFS.
- The optional 03:00 task remains verification-only. It compares an exact
  read-only Google Drive API inventory and performs an independent canary
  restore through the direct `rclone:` backend; it does not copy, overwrite,
  delete, prune, or purge repository objects.
- Exact case-sensitive paths, byte counts, MD5/SHA-256 hashes, provider IDs,
  repository identity, protected configuration binding, and restored canary
  content must all agree.

## Important limitations

- Google Drive for desktop must be signed in and healthy. DriveFS or network
  outages can prevent backups and restores because the streamed path is the
  live repository.
- Direct verification needs separately reviewed read-only rclone credentials;
  the installer does not create cloud credentials or grant Google access.
- Whole-repository API inventory and hashing can consume substantial time and
  provider quota.
- A streamed repository is not an offline or immutable second copy.
- Scripts and executables are not Authenticode-signed, and automatic alpha
  upgrades are not supported.

## Download and verify

Download `ResticBackuper-v0.1.0-alpha.7-windows-x64.zip` and its adjacent
`.sha256` file. Compare the SHA-256 values before extracting, then perform an
independent restore before relying on the installation.
