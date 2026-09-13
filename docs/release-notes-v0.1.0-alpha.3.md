# ResticBackuper v0.1.0-alpha.3

This alpha adds protected run controls and editable scheduling, and refines the
dashboard into a more useful native Windows utility. The backup and recovery
model remains conservative: an accepted action is not reported as a successful
backup until protected telemetry and verification prove the final result.

## Highlights

- **Back up now** confirms the request, validates the exact installed task and
  protected launcher, and asks Windows to start that task through UAC. The
  dashboard never launches Restic, Python, or the launcher directly.
- **Edit schedule** supports daily and selected-day schedules plus the supported
  missed-run, wake, and battery settings. Changes are previewed, applied by a
  separate elevated manager, independently re-read, and rolled back if
  verification fails. Saving a schedule does not start a backup.
- **Cancel backup** remains visible but is enabled only for an exact active
  protected run. Cancellation uses a cryptographically random per-run event and
  one targeted Ctrl-Break so Restic can stop cooperatively; it never ends the
  scheduled task or kills a generic PID.
- Cancellation races are explicit. Restic exit 130 after the request, or a safe
  between-phase checkpoint with no child running, becomes `cancelled`. If the
  child exits first, exit 0 continues normal verification and another exit
  remains a failure. Existing verified snapshots are not changed.
- The dashboard now offers **System**, **Midnight**, and **Daylight** themes,
  follows Windows High Contrast, and uses a flatter responsive layout with
  protected folders kept prominent.
- Adding a folder has durable approval, applying, verification, success,
  cancellation, and failure states with accessible live announcements, inline
  retry/dismiss actions, and motion that respects Windows preferences.
- ETA can fall back immediately to an explicitly low-confidence estimate from
  comparable recent runs when matching folder-set history does not yet exist;
  matching history remains preferred.

## Install

Download both `ResticBackuper-v0.1.0-alpha.3-windows-x64.zip` and its `.sha256`
sidecar from the GitHub release. Compare the computed ZIP hash with the sidecar,
extract the ZIP to a normal local directory, review the included scripts, and
run `Install.cmd`. Full installation and independent restore-test instructions
are in the [README](https://github.com/50sotero/ResticBackuper#readme).

The ZIP contains checksum-pinned Windows x64 builds of Restic 0.19.1 and the
Python 3.14.6 embeddable runtime. A separate Python or Restic installation is
not required. Windows PowerShell 5.1 and .NET Framework 4.8 are required Windows
components and are checked before installation changes begin.

## Alpha warning

The installer and executables are not code-signed, broad Windows compatibility
validation remains incomplete, and there is no in-place upgrade. Test with
non-critical data, keep another backup, store the recovery key separately, and
complete an independent restore drill before relying on this release.

ResticBackuper still does not configure cloud replication, retention, pruning,
automatic updates, or repository deletion. The creator's separate Google Drive
copy is a deployment example, not an installed feature. Removing a protected
folder changes future backups only and never erases existing snapshots. Canary
verification is deliberately narrow and does not replace representative
user-file restore tests.
