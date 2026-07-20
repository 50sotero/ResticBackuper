# ResticBackuper

Verified, encrypted, incremental Windows backups powered by Restic.

ResticBackuper turns a careful Restic setup into a guided Windows package. It
adds VSS capture for open files, a daily Task Scheduler job, a live read-only
telemetry dashboard, UAC-protected source-folder management, protected local
credentials, and verification that includes a real canary restore.

> [!WARNING]
> **v0.1.0-alpha.1 is an early public test release.** Its installer and
> executables are not code-signed, and the project has not yet been validated
> across a broad range of Windows machines. Test it with non-critical data and
> an independent restore target before relying on it. Keep another backup while
> you evaluate it.

## What it does

- Creates encrypted, deduplicated, incremental snapshots with
  [Restic](https://restic.net/).
- Optionally uses Windows Volume Shadow Copy Service (VSS) to capture a
  consistent view of open files on local NTFS volumes.
- Runs every day through Task Scheduler at the time you choose while the
  installing Windows user is signed in.
- Shows live progress, throughput, elapsed time, ETA, errors, and local run
  history in a tray-friendly dashboard.
- Displays every backed-up folder and lets the user add or remove folders
  through a separate elevated manager. Removing a folder never deletes old
  snapshots.
- Rejects a run as unsuccessful unless the resulting snapshot exactly matches
  the configured source set.
- Runs a Restic repository structure check and restores a small canary file,
  then verifies its content hash.
- Rotates through a deeper repository data subset check on the configured
  weekday.
- Stores the repository password in a CurrentUser Windows DPAPI envelope and
  creates a separate recovery key for offline storage.
- Preserves the repository, recovery material, and run history when the app is
  uninstalled.

ResticBackuper does not automatically prune snapshots or delete repository
data. The repository will therefore grow until you deliberately introduce and
test a retention policy.

## Requirements

- 64-bit Windows 10 or Windows 11
- An administrator approval through UAC, using the same Windows account that
  starts the installer
- A local fixed or removable NTFS drive-letter path for the Restic repository;
  a separate physical drive is strongly recommended
- Local NTFS source volumes when VSS is enabled; other sources require the
  `-DisableVss` installer option
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
   Get-FileHash .\ResticBackuper-v0.1.0-alpha.1-windows-x64.zip -Algorithm SHA256
   Get-Content .\ResticBackuper-v0.1.0-alpha.1-windows-x64.zip.sha256
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
6. Start the first real backup when you are ready:

   ```powershell
   Start-ScheduledTask -TaskName ResticBackuper
   ```

The first backup may take a long time. Later runs reuse Restic's stored data,
but Restic still needs to inspect the selected sources to determine what
changed.

### Advanced unattended install

Run the PowerShell installer directly from an elevated-capable session. Source
paths are separated by semicolons:

```powershell
.\Install-ResticBackuper.ps1 `
  -Repository 'D:\ResticBackups\Personal' `
  -SourceList 'C:\Users\you\Documents;C:\Users\you\Pictures' `
  -Schedule '02:00' `
  -Unattended `
  -StartBackup
```

Omit `-StartBackup` to install and schedule the job without immediately
starting the first backup. Use `-DisableVss` only when you accept losing VSS
capture, or `-SkipDashboard` for a headless installation.

## Verify that recovery works

A green dashboard is useful evidence, but it is not a recovery test for your
actual files. After the first successful run:

1. Open the generated recovery-tools directory next to the repository.
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
| Repository | Chosen during installation |
| Portable recovery tools | `RecoveryTools` beside the chosen repository |
| Plaintext recovery key | User-profile root initially; move it offline |
| Dashboard shortcut | Start menu |

Uninstall from Windows **Installed apps**. Uninstall removes the application,
scheduled tasks, and shortcut, but intentionally leaves the repository,
ProgramData state, DPAPI envelope, recovery tools, and recovery key in place.
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

Release builds are produced on 64-bit Windows. The build verifies and stages
the pinned Python and Restic dependencies, compiles the native Windows
launcher and WPF dashboard with .NET Framework 4.8 tooling, generates a
payload manifest, and creates the distributable ZIP plus checksum.

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
