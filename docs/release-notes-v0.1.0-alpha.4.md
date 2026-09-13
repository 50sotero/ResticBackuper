# ResticBackuper v0.1.0-alpha.4

Alpha.4 turns the dashboard from a backup monitor into a guarded recovery and
plan-management utility. The release remains an unsigned Windows x64 alpha and
should be evaluated with non-critical data and independent restore tests.

## Highlights

- A dedicated **Restore** page opens a UAC-protected Restore Center. It browses
  bounded snapshot metadata, binds the final request to an immutable snapshot
  ID, rejects overlapping or non-empty destinations, and verifies the restore.
- Snapshots created before plan IDs can appear as **Legacy / unbound** only
  after an exact computer, scheduled-tag, and complete-source-set match. They
  require a full immutable ID plus a separate warning confirmation; the app
  never rewrites or retags repository history during migration.
- DPAPI password-helper processes run without writing Python bytecode, keeping
  the protected runtime byte-for-byte consistent with its signed manifest
  after backup, browse, and restore operations.
- **Recovery Readiness** independently exercises the active DPAPI credential,
  separate recovery key, portable recovery bundle, repository identity,
  capacity, locks, and a real alternate-location restore drill.
- Guided recovery actions can rebuild the active credential from a proven
  recovery key, ask Restic to remove only locks it classifies as stale, and
  rotate repository access while retaining the previous key for rollback.
- **Change location** copies the encrypted repository to a reviewed empty local
  destination, verifies identity, history, and structure, then atomically
  activates the new path. The old repository is retained deliberately.
- Stable plan IDs, monotonic configuration generations, and exact source-volume
  identities reject stale approvals and unexpected drive-letter reuse.
- Strict cloud-placeholder preflight prevents online-only files from being
  silently omitted unless the operator explicitly chooses the permissive
  policy.
- Preflight uses Windows extended-length paths and skips only definitely
  excluded dependency/cache directories, preventing false failures in large
  source trees while keeping included access errors fail-closed.
- Schedule-aware freshness warnings, structured run details, suggested recovery
  actions, and redacted diagnostic ZIP export make failures easier to diagnose.
- Unusually destructive change sets are held for explicit, plan-bound review;
  acknowledgement never deletes or rewrites snapshots.

## Transaction safety

Repository relocation, source updates, and credential rotation use protected,
flushed journals and the shared run lock. Other backup and configuration
operations fail closed while a transaction needs recovery. Relocation tests
cover normal completion, every metadata publication boundary, abrupt process
termination, exact rollback, destination ownership, and preservation of the
production task/configuration.

Restore operations are read-only against the repository and never write to live
sources. Restic receives `--overwrite never`; a partial destination is retained
and clearly reported if recovery fails.

## Important limitations

- The executables and scripts are not Authenticode-signed.
- There is no in-place alpha upgrade or automatic updater.
- Cloud upload, off-site replication, retention, pruning, and repository
  deletion are not built in.
- Scheduled runs require the installing user to be signed in because the task
  uses that profile's CurrentUser DPAPI credential.
- A verified canary is useful evidence, not proof that every personal file is
  recoverable. Perform independent restores of representative data.

## Download and verify

Download both `ResticBackuper-v0.1.0-alpha.4-windows-x64.zip` and the adjacent
`.sha256` file from the GitHub release. Compare the SHA-256 locally before
extracting, then test a restore to a separate location before relying on the
installation.
