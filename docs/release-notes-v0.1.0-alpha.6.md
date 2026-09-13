# ResticBackuper v0.1.0-alpha.6

Alpha.6 introduces an explicit single-repository Google Drive for desktop mode.
When selected, the live encrypted Restic repository is stored below the **My
Drive** streaming mount; there is no second local mirror. This remains an
unsigned Windows x64 alpha. Keep another independent backup and test
representative restores before relying on it.

## Highlights

- `google_drivefs_stream` is an explicit opt-in storage mode bound to a strict
  descendant of `G:\My Drive` and the current user's DriveFS cache.
- Repository relocation uses a nonce-bound sibling stage, exact file inventory,
  repository-ID and snapshot-history checks, `restic check`, and same-parent
  promotion. The previous repository is retained for rollback.
- Credentials, ProgramData state, the recovery key, and portable recovery tools
  remain on NTFS outside DriveFS.
- An optional verification-only 03:00 task holds the protected run lock and
  compares the live repository with an exact, read-only Google Drive API
  inventory. It does not copy, overwrite, delete, prune, or purge repository
  objects.
- Verification requires exact case-sensitive paths, byte counts, MD5 and
  SHA-256 hashes, unique provider IDs, matching Restic repository identity, the
  latest complete snapshot, and an independently verified canary restore
  through the direct `rclone:` cloud backend.
- Rclone, its encrypted configuration, its DPAPI-protected configuration
  password, and the packaged reveal helper are installed under a protected
  ProgramData tree and bound by an exact four-file asset manifest.
- Primary-backup and cloud-verifier task evidence use distinct protected
  filenames. The verifier accepts Task Scheduler's valid omission of the
  default-enabled XML field, and atomic replacement uses a real temporary
  backup path compatible with Windows PowerShell 5.1 and .NET Framework.
- The dashboard trusts only fresh schema-2 evidence bound to the current plan,
  configuration generation, repository path and ID, latest snapshot, protected
  assets, exact inventory, and direct-cloud restore.
- DriveFS anomaly acknowledgement is described truthfully as a review for later
  maintenance decisions, not an upload pause.

## Important limitations

- Google Drive for desktop must be signed in and healthy. The streamed mount is
  the only live repository in this mode, so DriveFS or network outages can
  prevent backups and restores.
- The optional direct verifier requires a separately reviewed, read-only rclone
  remote and CurrentUser-DPAPI setup. The installer does not create cloud
  credentials or grant Google access automatically.
- Exact whole-repository API inventories and hash verification can take
  substantial time and provider quota as the repository grows.
- A streamed cloud repository is not an offline or immutable second copy. Keep
  independent recovery material and another backup appropriate to your threat
  model.
- Restic snapshot pruning and repository deletion remain manual and are not
  performed by the backup or verification tasks.
- The executables and scripts are not Authenticode-signed, and there is no
  automatic updater or supported in-place alpha upgrade.

## Download and verify

Download both `ResticBackuper-v0.1.0-alpha.6-windows-x64.zip` and the adjacent
`.sha256` file from the GitHub release. Compare the SHA-256 locally before
extracting, then perform an independent restore before relying on the
installation.
