# ResticBackuper

Verified, encrypted, incremental Windows backups powered by Restic.

ResticBackuper turns a careful Restic setup into a guided Windows package. It
adds VSS capture for open files, a daily Task Scheduler job, a live read-only
telemetry dashboard, UAC-protected source-folder management, protected local
credentials, and verification that includes a real canary restore.

> [!WARNING]
> **v0.1.0-alpha.8 is an early public test release.** Its installer and
> executables are not code-signed, and the project has not yet been validated
> across a broad range of Windows machines. Test it with non-critical data and
> an independent restore target before relying on it. Keep another backup while
> you evaluate it.

## How the creator uses it

ResticBackuper grew out of a real nightly Windows backup routine. The protected
set includes software projects, cloud-synchronized working folders, Codex
configuration and home data, and ordinary personal folders. Dependency trees,
caches, and build outputs that can be reproduced from source and lockfiles are
excluded, so the backup concentrates on original work and irreplaceable data.

Each run writes an encrypted, incremental snapshot to the explicitly selected
Restic repository. A successful status also requires repository checks and a
real canary-file restore whose content is verified. The repository can be local
NTFS, or an explicit single live path below Google Drive for desktop's **My
Drive** streaming mount.

For DriveFS mode, the optional 03:00 task does not make a second mirror. It
compares the live repository with a read-only Google Drive API inventory and
performs an independent restore through the cloud API. See
[Direct Google Drive repository verification](docs/google-drive-direct-verification.md).
This is not a substitute for choosing sources, exclusions, retention, and
restore tests appropriate to your own threat model.

## What it does

- Creates encrypted, deduplicated, incremental snapshots with
  [Restic](https://restic.net/).
- Optionally uses Windows Volume Shadow Copy Service (VSS) to capture a
  consistent view of open files on local fixed NTFS volumes.
- Runs every day through Task Scheduler at the time you choose while the
  installing Windows user is signed in.
- Uses a compact four-page navigation rail in its tray-friendly Windows
  utility: **Protection** for backup state and sources, **Activity** for full
  run history and trends, **Restore** for recovery workflows, and **Settings**
  for storage, freshness, appearance, and preview controls.
- Shows live progress, throughput, elapsed time, ETA, and errors on
  **Protection** while a backup is active. The installed schedule, latest
  result, and real repository path remain available in a compact facts row.
- Provides a confirmation-protected **Back up now** action. The normal-user
  dashboard validates and requests the installed scheduled task through Windows
  approval; it never launches Restic, Python, or the protected launcher directly.
- Displays the installed Task Scheduler cadence, time, enabled state, and next
  run. **Edit schedule** supports daily or selected-day schedules and the
  relevant missed-run, wake, and battery settings through a reviewed,
  UAC-protected update; saving a schedule never starts a backup.
- Keeps **Cancel backup** visible for discoverability, with explanatory help
  while idle, and enables it only for an exact active protected run.
  Cancellation uses a run-bound event and targeted `CTRL_BREAK_EVENT` so Restic
  can stop cooperatively; it never uses Task Scheduler End or generic PID
  termination.
  Existing verified snapshots are unaffected. If a run is already finishing,
  it may complete and be verified before cancellation takes effect.
- Keeps protected folders dominant and above the fold on **Protection**.
  **Add folder** remains visible, every removable folder has an explicit remove
  action, and the folder list scrolls internally when the source set grows.
- Offers **System**, **Midnight**, and **Daylight** dashboard themes plus the
  preview animation on **Settings**. System follows the Windows app theme and
  automatically respects Windows High Contrast; the choice is stored only in
  the current user's local presentation settings.
- Opens at a work-friendly 1280 × 720 size and collapses the navigation rail to
  icons at narrower widths. A single **Refresh** command stays in the app
  header, and `F5` refreshes the current page from the keyboard.
- Applies folder changes through a separate elevated manager. Removing a folder
  stops future backups of that source but never deletes old snapshots, and the
  required canary source cannot be removed.
- Gives every installed plan a stable identity and monotonic configuration
  generation. Each source is bound to its expected volume serial, so a reused
  drive letter or stale dashboard request fails closed instead of silently
  protecting the wrong data. Cloud placeholders are rejected by default unless
  the operator explicitly accepts that policy.
- Provides a protected **Change location** workflow for the encrypted
  repository. It copies into an empty local destination, verifies repository
  identity and history, atomically activates the new path, and retains the old
  repository as a rollback copy. Interrupted moves are recovered from a durable
  journal before another protected operation can proceed.
- Adds a UAC-protected **Restore Center** that browses immutable snapshot IDs and
  restores selected content only to a new or empty, non-overlapping destination
  with Restic verification and a bound restore report.
- Shows compatible snapshots created before plan IDs as **Legacy / unbound**
  only when computer, scheduled tag, and the complete source set exactly match
  the current configuration. An exact full ID and a separate warning
  confirmation are required; no snapshot metadata is rewritten or retagged.
- Adds **Recovery Readiness** checks for the repository, active DPAPI credential,
  separate recovery key, portable recovery bundle, locks, capacity, and a real
  alternate-location restore drill. **Run restore drill** uses same-user Windows
  approval to recover the latest plan-bound snapshot's canary and a bounded
  ordinary-data sample into a new protected folder. Only fully verified,
  durably recorded evidence for the current plan and generation clears the
  review warning; failed output is retained and never recorded as a pass.
  Other guided repair actions can rebuild the active credential from the
  recovery key, safely remove Restic-confirmed stale locks, and rotate access
  while retaining the previous repository key for rollback.
- Detects overdue scheduled protection independently of process telemetry and
  records structured failure details. Activity rows open a bounded run-details
  view with suggested next actions, and the app can export a redacted diagnostic
  ZIP that removes credentials, identities, host names, command lines, and
  personal paths.
- Holds unusually destructive change sets for explicit review using protected,
  plan-bound evidence. Reviewing the warning never deletes a snapshot or changes
  repository history. In DriveFS mode this is a maintenance/deletion review
  acknowledgement, not an upload gate: the live repository may already be
  synchronizing with Google Drive.
- Supports keyboard navigation, visible focus states, accessible control names,
  readable action targets, and status cues that do not rely on color alone.
- Rejects a run as unsuccessful unless the resulting snapshot exactly matches
  the configured source set.
- Runs a Restic repository structure check and restores a small canary file,
  then verifies its content hash.
- Rotates through a deeper repository data subset check on the configured
  weekday.
- Stores the repository password in a CurrentUser Windows DPAPI envelope and
  creates a separate recovery key for offline storage.
- Includes an optional verification-only Google Drive task for the explicit
  streamed repository mode. It runs elevated after the backup, holds the
  protected run lock, verifies exact API file hashes and repository identity,
  and restores the canary through a direct cloud backend. It never creates a
  local mirror or copies, deletes, or prunes repository objects.
- Preserves the repository, recovery material, and run history when the app is
  uninstalled.

ResticBackuper does not automatically prune snapshots or delete repository
data. The repository will therefore grow until you deliberately introduce and
test a retention policy.

## Requirements

- 64-bit Windows 10 or Windows 11
- Windows PowerShell 5.1 and .NET Framework 4.8. The installer checks these
  Windows components before making changes.
- An administrator approval through UAC, using the same Windows account that
  starts the installer
- Either a local fixed/removable NTFS repository, or the explicit
  `google_drivefs_stream` backend at a path strictly beneath `G:\My Drive`.
  DriveFS mode requires Google Drive for desktop to be running, its current-user
  cache to be available on NTFS with at least 10 GiB free, and every Restic
  object to remain strictly smaller than 4 GiB.
- Source folders on ready local fixed or removable drive-letter volumes. UNC
  and network sources are not supported, including with `-DisableVss`.
- With VSS enabled (the default), every source volume must be fixed and NTFS.
  `-DisableVss` permits supported local removable or non-NTFS source volumes.
- Enough free space for the first full snapshot (the installer requires at
  least 10 GiB by default)

The release contains pinned Windows x64 builds of Restic 0.19.1 and the Python
3.14.6 embeddable runtime. A separate Python or Restic installation is not
required.

## Install the alpha

1. Download the Windows x64 ZIP and its `.sha256` file from
   [GitHub Releases](https://github.com/50sotero/ResticBackuper/releases).
2. Verify the download before extracting it:

   ```powershell
   Get-FileHash .\ResticBackuper-v0.1.0-alpha.8-windows-x64.zip -Algorithm SHA256
   Get-Content .\ResticBackuper-v0.1.0-alpha.8-windows-x64.zip.sha256
   ```

   The two SHA-256 values must match exactly.
3. Extract the ZIP to a normal local directory. Review the included scripts if
   you wish; this is a transparent script-based installer, not an opaque
   bootstrap executable.
4. Double-click `Install.cmd`. Choose the repository, source folders, and daily
   schedule. VSS and the dashboard are enabled by default; advanced flags can
   disable either. Approve UAC with the same Windows account.
5. Move the generated `ResticBackuper-RecoveryKey.txt` to a password manager,
   encrypted removable storage, or another secure offline location. Do not
   leave the only copy on the computer being backed up.
6. Start the first real backup when you are ready by choosing **Back up now**
   in the dashboard. From an elevated Windows PowerShell session, you can also
   request the same installed task directly:

   ```powershell
   Start-ScheduledTask -TaskName ResticBackuper
   ```

The first backup may take a long time. Later runs reuse Restic's stored data,
but Restic still needs to inspect the selected sources to determine what
changed.

### Google Drive storage modes

The default `local_ntfs` backend keeps the live repository on a normal local
volume. This release does not claim an off-site copy for that mode; use a
separately designed and tested off-site system if you select it.

An installation can instead opt in explicitly to
`google_drivefs_stream`. This mode is narrowly bound to `G:\My Drive` and the
current user's `%LOCALAPPDATA%\Google\DriveFS` cache. Startup fails closed if the
provider process, mount identity, FAT32 semantics, cache headroom, atomic
create/flush/rename/read/delete probe, or strict sub-4-GiB object invariant
cannot be proved. The DPAPI envelope, ProgramData state, recovery key, and
portable recovery tools remain outside DriveFS on NTFS.
The canonical protected fields are `repository_storage_mode`,
`drivefs_my_drive_root`, and `drivefs_cache_directory`; the installer never
emits the retired `repository_drivefs_root` alias.

Repository relocation never writes directly into the final DriveFS path. It
copies to a nonce-bound sibling stage, verifies an exact path/size/SHA-256
inventory manifest, Restic repository ID, snapshot history, and `restic check`,
then promotes the stage with a same-parent rename. The old repository is
retained. Provider upload completion and an independent cloud restore remain
separate operational evidence; a successful local promotion alone does not
prove that Google has finished uploading every object.

The optional post-backup task proves that separate operational evidence without
creating another repository path. Its protected schema-2 proof requires an
exact case-sensitive API inventory, matching byte/MD5/SHA-256 values and
provider object IDs, identical local/API Restic repository identity, and a
verified canary restore through the `rclone:` backend. Full setup and trust
boundaries are documented in
[Direct Google Drive repository verification](docs/google-drive-direct-verification.md).

### Advanced unattended install

Run the PowerShell installer directly from an elevated-capable session. Source
paths are separated by semicolons:

```powershell
.\Install-ResticBackuper.ps1 `
  -Repository 'D:\Backups\ResticRepository' `
  -SourceList 'C:\Users\you\Documents;C:\Users\you\Pictures' `
  -Schedule '02:00' `
  -Unattended `
  -StartBackup
```

Omit `-StartBackup` to install and schedule the job without immediately
starting the first backup. Use `-DisableVss` only when you accept losing VSS
capture and need a supported local removable or non-NTFS source; it does not
enable UNC or network sources. Use `-SkipDashboard` for a headless installation.

For an explicit DriveFS installation:

```powershell
.\Install-ResticBackuper.ps1 `
  -Repository 'G:\My Drive\ResticBackups\Personal' `
  -RepositoryStorageMode google_drivefs_stream `
  -DriveFsMyDriveRoot 'G:\My Drive' `
  -DriveFsCacheDirectory "$env:LOCALAPPDATA\Google\DriveFS" `
  -SourceList 'C:\Users\you\Documents;C:\Users\you\Pictures' `
  -Unattended
```

The canonical configuration fields are illustrated in
[`src/backup-config.drivefs.example.json`](src/backup-config.drivefs.example.json).

## Verify that recovery works

A green dashboard is useful evidence, but it is not a recovery test for your
actual files. After the first successful run:

1. Open the generated recovery-tools directory (beside a local repository, or
   in `C:\ProgramData\ResticBackuperRecoveryTools` for DriveFS).
2. Follow its `RECOVERY.md` instructions.
3. Restore a representative set of files to a new, empty directory on a
   different path.
4. Open and compare those files independently.
5. Repeat the exercise periodically and after material configuration changes.

Keep the repository and recovery key separate when possible. Anyone who has
both can decrypt the backup; losing both the Windows profile and the offline
recovery key can make the repository unrecoverable.

For the guarded wrapper, pass `--source` with one exact configured source. It
restores that source without recreating absolute-path ancestors and their
Windows ACLs:

```powershell
& 'C:\Program Files\ResticBackuper\Python\python.exe' -I `
  'C:\Program Files\ResticBackuper\restore.py' `
  --snapshot latest `
  --source 'C:\Users\you\Documents' `
  --target 'X:\Restored-Documents'
```

## Where files live

| Purpose | Default location |
| --- | --- |
| Protected runtime and configuration | `C:\Program Files\ResticBackuper` |
| DPAPI credential, protected canary, status, logs, and run history | `C:\ProgramData\ResticBackuper` |
| Protected Google API verification assets and evidence | `C:\ProgramData\ResticBackuperCloudVerification` |
| Per-user dashboard history, heartbeat, and theme preference | `%LOCALAPPDATA%\ResticBackuperDashboard` |
| Repository | Chosen during installation |
| Portable recovery tools | Beside a local NTFS repository; `C:\ProgramData\ResticBackuperRecoveryTools` for DriveFS |
| Plaintext recovery key | User-profile root initially; move it offline |
| Dashboard shortcut | Start menu |

Uninstall from Windows **Installed apps**. Uninstall removes the application,
its owned backup, dashboard, and cloud-verification tasks, plus the shortcut,
but intentionally leaves the repository, ProgramData state, DPAPI envelope,
recovery tools, recovery key, and protected cloud-verification assets/evidence
in place.
This alpha does not support an in-place upgrade; uninstall the old application
binaries, confirm that the protected data remains, and then install the newer
release.

## Safety and transparency

- The release builder downloads immutable, versioned upstream archives and
  checks their pinned SHA-256 values from [`dependencies.json`](dependencies.json).
- The installer verifies every extracted payload file against a generated
  manifest before copying the payload into the protected runtime.
- The repository password is generated locally. Restic receives it through a
  password command, not a task argument or ordinary log line.
- The elevated runtime and writable state are separated and protected with
  Windows ACLs.
- Dashboard telemetry remains read-only. Folder-list changes are performed by
  a separate UAC-elevated manager in the protected runtime, never by the
  limited dashboard process.
- Schedule changes and cancellation requests use separate protected managers,
  nonce-bound result files, the current Windows SID, and an exact scheduled-task
  fingerprint. The dashboard independently re-reads protected state before it
  reports a schedule change or cooperative cancellation as complete.
- A cancellation request is not a successful backup. A run becomes `cancelled`
  only when Restic exits 130 after the signal or the request is consumed at a
  protected between-phase checkpoint while no Restic child is running. If the
  child exits first, exit 0 continues normal verification and any other exit
  remains a failure.
- Manual starts validate the fixed scheduled-task identity and protected
  launcher before requesting that task through a separate UAC-approved Windows
  process. Exit code zero means only that the request was accepted; verified
  telemetry remains the source of truth for completion.
- Source-list changes use a flushed protected undo journal plus atomic
  per-file replacements for the live configuration and both runtime/recovery
  manifests. Until journal deletion commits a fully verified change, backup
  runs fail closed; a later manager invocation restores the complete previous
  file set after an interrupted update.
- Repository relocation and credential rotation use their own flushed journals,
  bind requests to the current plan ID, generation, Windows SID, and exact
  configuration hash, and block backup or configuration mutations until an
  interrupted transaction is safely resumed or rolled back.
- Restore operations use an exact immutable snapshot ID and a destination that
  is absent or empty. The protected manager rejects source/repository overlap,
  never selects a moving `latest` snapshot after review, and never overwrites
  existing destination files.
- Legacy / unbound snapshots are never selected through `latest` or an ID
  prefix. They are eligible only after exact configuration matching and require
  a separately bound opt-in for browsing and restore.
- Restore targets must be new or empty and cannot overlap a configured source
  or the repository.

There is no code signature in this alpha. Windows SmartScreen or antivirus may
therefore warn about the downloaded scripts and executables. Verify the release
checksum, review the source, and proceed only if you trust it. Do not disable
security controls globally to make the installation run.

See [`docs/architecture.md`](docs/architecture.md) for the data flow and trust
boundaries, [`SECURITY.md`](SECURITY.md) for reporting vulnerabilities, and
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) for dependency licenses.

## Build from source

Release builds are produced with Windows PowerShell 5.1 and .NET Framework 4.8
tooling on 64-bit Windows. The build verifies and stages
the pinned Python and Restic dependencies, compiles the native Windows
launcher and WPF dashboard with .NET Framework 4.8 tooling, generates a
payload manifest, and creates the distributable ZIP plus checksum.

The ZIP writer normalizes package entry metadata, but the legacy .NET Framework
C# compiler can emit nondeterministic executable bytes. Builds from the same
source are therefore not guaranteed to reproduce the published ZIP byte for
byte. A release SHA-256 identifies and verifies that exact frozen artifact; it
is not a reproducible-build claim.

```powershell
.\build\Build-Release.ps1
.\tests\Test-ReleaseArtifact.ps1
```

See [`CONTRIBUTING.md`](CONTRIBUTING.md) before submitting a change. In
particular, never commit real paths, usernames, hostnames, volume serials,
logs, repositories, DPAPI envelopes, or recovery keys.

## License

ResticBackuper is available under the [MIT License](LICENSE). Release bundles
also contain Restic under the BSD 2-Clause License and Python under the Python
Software Foundation License and its accompanying notices.
