# Changelog

## Unreleased

## 0.1.0-alpha.8

- Adds an in-app, same-user-UAC recovery drill that restores a bounded sample
  from the latest current-plan snapshot with the independent recovery key and
  keeps the protected result available for review.
- Requires Restic verification plus independent canary, size, and exact-
  inventory checks before atomically recording protected restore evidence.
  Failed or partial output is retained but cannot clear Recovery Readiness.
- Binds qualifying history to the current plan, configuration generation,
  repository identity, exact snapshot, plan binding, and representative-drill
  credential source; malformed, stale, legacy, partial, or writable evidence
  fails closed.
- Hardens the bounded dashboard probe and restore-manager result channels, and
  extends regression and release-artifact coverage for the guided workflow.

## 0.1.0-alpha.7

- Fixes direct-cloud latest-snapshot verification under Windows PowerShell 5.1
  by explicitly normalizing Restic's top-level JSON array instead of allowing
  `ConvertFrom-Json` to preserve it as one pipeline object.
- Validates every direct-cloud snapshot object, full lowercase identity, and
  RFC3339 timestamp before restore. Malformed JSON shapes, missing or
  non-string fields, duplicate identities, invalid offsets, and timestamps with
  unsupported precision fail closed.
- Orders Restic RFC3339Nano timestamps without losing the eighth or ninth
  fractional digit, normalizes numeric offsets to UTC, and requires the
  protected last-success snapshot to be among the newest direct-cloud
  snapshots before publishing verification evidence.

## 0.1.0-alpha.6

- Adds an explicit `google_drivefs_stream` storage mode whose sole live Restic
  repository is a strict descendant of Google Drive for desktop's **My Drive**
  streaming mount. Recovery tools, credentials, protected state, and the
  recovery key stay on NTFS outside DriveFS.
- Makes repository relocation into DriveFS transactional: copy into a
  nonce-bound sibling stage, verify an exact path/size/SHA-256 inventory,
  repository identity, snapshot history, and `restic check`, then publish with
  a same-parent rename while retaining the old repository.
- Replaces the alpha.5 local-mirror design with an optional verification-only
  task. It compares the live repository against an exact read-only Google Drive
  API inventory and performs an independent canary restore through the direct
  cloud backend; it never copies, deletes, prunes, or rewrites repository
  objects.
- Protects the cloud verifier with the shared run-lock handshake, an elevated
  interactive task, a four-file size/hash manifest, strict ProgramData ACL and
  reparse-point checks, CurrentUser DPAPI, and binary-safe native-output
  capture.
- Keeps primary and cloud-verifier task-definition evidence distinct, permits
  Task Scheduler's valid omission of the default-enabled XML field, tolerates
  inherit-only `OWNER RIGHTS` entries omitted by the .NET managed ACL view, and
  uses real same-directory backup paths for Windows PowerShell 5.1/.NET atomic
  replacement of cloud evidence and dashboard state.
- Publishes immutable schema-2 cloud evidence only after repository identity,
  case-sensitive paths, byte counts, MD5/SHA-256 hashes, unique provider IDs,
  the latest complete snapshot, and a direct-cloud canary restore agree. The
  dashboard rejects stale, legacy, or differently bound evidence.
- Clarifies destructive-change review in DriveFS mode: acknowledgement controls
  later maintenance decisions but cannot pause uploads from the sole live
  streamed repository.
- Extends release and regression coverage for DriveFS validation, transactional
  repository moves, cloud inventory and verifier behavior, verification-task
  installation, dashboard evidence handling, and a disposable real Restic
  backup-and-restore test from the packaged artifact.

## 0.1.0-alpha.5

- Adds an optional, post-install Google Drive for desktop mirror task that keeps
  the live Restic repository on local storage, stages only missing immutable
  files outside the provider root, promotes complete files without overwrite or
  deletion, and publishes a committed generation only after exact SHA-256/MD5
  inventory checks, Restic authentication/check/restore, and DriveFS upload
  confirmation.
- Uses the protected backup run lock through a read-only byte-range handshake,
  so the limited-user mirror task coordinates with backup and configuration
  operations without weakening the protected ACL or running user-writable code
  elevated.
- Adds bounded off-site telemetry to the dashboard, distinguishing failure,
  in-progress, locally verified, provider-confirmed, and restore-verified states.
- Ships fail-closed DriveFS metadata verification, repository-ID-scoped public
  destinations, a 48-hour task ceiling with one 24-hour provider deadline, and
  disposable transaction/provider regression tests.
- Fixes strict cloud-placeholder preflight on large Windows source trees by
  using extended-length paths, pruning only directories definitely covered by
  the active Restic exclusion file, and tolerating only descendants that
  disappear during enumeration. Included access errors and cloud-only content
  continue to fail closed.

## 0.1.0-alpha.4

- Adds a dedicated **Restore** page and UAC-protected Restore Center for
  browsing immutable snapshot IDs and restoring selected content to a new or
  empty non-overlapping destination with verification and bound reports.
- Safely surfaces pre-plan snapshots as **Legacy / unbound** only when their
  computer, scheduled tags, and complete source set exactly match the current
  protected configuration. Browsing or restoring one requires its full
  immutable ID and restore adds a separate explicit warning; metadata is never
  rewritten or retagged.
- Runs the DPAPI password helper with isolated, no-site, no-bytecode Python
  flags so repository reads cannot create unmanifested runtime cache files.
- Adds **Recovery Readiness** checks for repository identity, the active DPAPI
  credential, the independent recovery key, portable recovery tools, capacity,
  Restic locks, and an alternate-location restore drill.
- Adds guided credential repair, Restic-confirmed stale-lock cleanup, and
  journaled key rotation that proves the new key while retaining the prior key
  and recovery document for rollback.
- Adds a protected repository-location workflow that copies to an empty local
  destination, verifies repository identity and snapshot history, atomically
  activates the new path, and deliberately retains the old repository.
- Makes repository relocation crash-safe across copy and every publication
  boundary with a durable recovery journal, destination ownership proof, exact
  rollback, and a dashboard recovery surface.
- Introduces stable plan IDs, monotonic configuration generations, exact
  source-volume identities, and strict cloud-placeholder preflight so stale
  requests, drive-letter reuse, and online-only data fail closed.
- Adds schedule-aware freshness monitoring that detects overdue protection even
  when process telemetry is stale or absent.
- Adds structured failure records, a bounded run-details dialog with suggested
  recovery actions, and redacted diagnostic ZIP export.
- Detects anomalously large deletion/change sets and holds their promotion for
  explicit, plan-bound review without deleting or modifying snapshots.
- Refreshes portable recovery tools transactionally after protected plan
  changes while preserving their recovery-local executable and credential
  paths.
- Extends protected-operation interlocks so backups, source changes, schedule
  changes, restores, repository moves, and credential operations do not consume
  partially published state.
- Expands the automated suite with plan/source preflight, cancellation races,
  freshness, restore, repository-relocation interruption recovery, recovery
  health and repair, anomaly review, run-details, diagnostic redaction, and
  credential-rotation integration coverage.

## 0.1.0-alpha.3

- Adds a confirmation-protected **Back up now** action that validates and
  requests the existing hardened scheduled task through Windows approval,
  without launching Restic or Python from the dashboard.
- Adds an **Edit schedule** workflow for daily or selected-day triggers and the
  supported missed-run, wake, and battery settings. A separate elevated manager
  binds the request to the installed task, verifies the result, and rolls back
  failed changes without starting a backup.
- Adds cooperative **Cancel backup** control for an exact active run. A
  per-run protected event requests one targeted Ctrl-Break; task termination and
  generic PID killing are not used, and child-exit races preserve normal success
  or failure semantics.
- Adds live System, Midnight, and Daylight dashboard themes with a persistent
  per-user selector and automatic Windows High Contrast handling.
- Replaces the transient add-folder message with durable approval, applying,
  verification, success, cancellation, and failure states; motion follows the
  Windows animation and High Contrast preferences.
- Provides an immediate, explicitly low-confidence ETA from comparable recent
  runs when no matching folder-set history exists, while preferring matching
  history as soon as it is available.
- Introduces a semantic color system across cards, charts, dialogs, tables,
  source controls, status badges, selections, and keyboard focus indicators.
- Adds a responsive 1080-DIP layout that stacks backup scope and run-history
  views at narrower widths while keeping source management prominent.
- Refreshes dashboard hierarchy, spacing, headings, and contrast without
  adding telemetry or backup-health claims the protected state cannot prove.
- Replaces the web-style card grid with a compact native Windows utility:
  status and recent activity on the left, protected folders on the right,
  inline metrics, flat surfaces, and a compact activity-header sparkline.
- Targets a 1280 × 720 default window, shows more source paths above the fold,
  and stacks status, sources, then activity below 1080 DIPs while retaining a
  single page scrollbar.

## 0.1.0-alpha.2

- Moves the protected-folder inventory into the above-the-fold backup overview
  so the configured sources are visible without scrolling past telemetry.
- Adds a persistent, high-contrast **Add folder** action and an explicit
  per-folder **Remove from future backups** action.
- Clarifies that removing a source changes future backups only and preserves
  every existing snapshot; the required canary source remains protected.
- Makes four-file source-list changes crash-safe with a flushed protected undo
  journal. Backup and dry-run wrappers fail closed until an interrupted change
  is reconciled to its exact previous state.
- Aligns installation and later folder changes on the same local-drive policy:
  fixed or removable drive-letter sources, tightened to fixed NTFS while VSS is
  enabled; UNC and network sources remain unsupported.
- Improves dashboard keyboard navigation, focus visibility, accessible control
  names, action sizing, contrast, and status cues that do not rely on color
  alone.
- Compresses the backup-health presentation and gives source management
  priority over detailed metrics and run history, including on shorter windows.
- Documents the creator's real workflow: nightly protection for software
  projects, a local Google Drive mirror, Codex configuration and home data, and
  personal folders, while excluding reproducible dependencies and build output.

## 0.1.0-alpha.1

- Initial public alpha.
- Encrypted, incremental Restic snapshots on Windows.
- Optional VSS capture of open files.
- Highest-level Task Scheduler automation through a kill-on-close launcher.
- Read-only animated dashboard with run history and ETA estimates.
- Dashboard folder inventory with UAC-protected add/remove management; source
  removal does not delete historical snapshots.
- Exact source-set validation, repository structural checks, and canary restore
  verification before a run is marked successful.
- Guided ZIP installer with pinned, checksum-verified Restic and Python
  runtimes.
- Conservative uninstaller that preserves repositories, state, credentials,
  recovery keys, and recovery tools.
