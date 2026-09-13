# Social launch posts

These drafts are ready to adapt and post. They describe the open-source alpha
and keep the single streamed-repository tradeoffs explicit.

## X / Twitter

ResticBackuper alpha.7 adds an explicit single-repository Google Drive for
desktop mode, transactional DriveFS moves, exact API inventory checks, a
nanosecond-safe latest-snapshot check, and a direct-cloud restore proof to
encrypted incremental Windows backups:
https://github.com/50sotero/ResticBackuper

## LinkedIn

I built **ResticBackuper** for the backup routine I actually wanted on my own
Windows machine, and I have now released it as an open-source alpha.

Every night, my setup protects software projects, cloud-synchronized working
folders, Codex configuration and home data, and personal folders. I exclude
dependency trees, caches, and build outputs that can be reproduced from source
and lockfiles, keeping the backup focused on original work and irreplaceable
data.

The core uses an encrypted Restic repository for incremental snapshots and
recovery. A run is not shown as successful until repository checks complete and
a real canary file has been restored and its content verified. The repository
can stay on local NTFS, or an explicit opt-in mode can use one live path below
Google Drive for desktop's My Drive streaming mount while keeping credentials,
state, recovery tools, and the recovery key outside DriveFS.

For that streamed mode, an optional verification-only task compares every live
repository object with the read-only Google Drive API view and restores the
canary through a direct cloud backend. It does not make or maintain a second
repository mirror.

ResticBackuper packages the reusable parts of that setup:

- encrypted, incremental, deduplicated Restic snapshots
- optional VSS capture for open files
- editable daily or selected-day Task Scheduler automation
- protected **Back up now** and cooperative cancellation for the exact active run
- a themed, accessible dashboard with progress, throughput, ETA and run history
- UAC-protected folder management from the dashboard
- a protected Restore Center bound to immutable snapshots and safe destinations
- independent readiness checks for the active credential, recovery key,
  portable tools, locks, capacity, and a real alternate-location restore
- crash-safe repository relocation that verifies before activation and retains
  the old repository
- explicit single-repository DriveFS mode with transactional staged promotion
- optional exact API inventory and independent direct-cloud restore evidence
- guided credential repair, stale-lock cleanup, and rollback-preserving key
  rotation
- structured run details, overdue-backup detection, redacted diagnostics, and
  explicit review for unusually destructive changes
- repository structure checks
- canary-restore verification before a run is marked successful
- a separate recovery key for disaster recovery

The alpha is deliberately transparent: it ships as a reviewable ZIP installer
with pinned, checksum-verified Restic and Python runtimes. The source is
available under the MIT License.

This is an early Windows x64 alpha, not a claim that any backup is automatically
safe. The binaries are not yet code-signed, and every setup needs an independent
restore test before it can be trusted. I would especially value feedback on
Windows compatibility, installer safety, recovery ergonomics, and the
dashboard.

Repository and alpha release:
https://github.com/50sotero/ResticBackuper

#opensource #backup #restic #windows #dataprotection #python
