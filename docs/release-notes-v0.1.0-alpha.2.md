# ResticBackuper v0.1.0-alpha.2

This alpha refresh makes protected-folder management a first-class part of the
ResticBackuper dashboard while preserving the conservative backup and recovery
model introduced in alpha.1.

## Highlights

- **Protected folders** now appears above the detailed metrics and run history,
  keeping the actual backup scope visible without scrolling.
- A persistent, high-contrast **Add folder** button makes source management
  discoverable in the main window.
- Each removable source has an explicit **Remove from future backups** action.
  Removing it does not delete any existing Restic snapshot, and the source that
  contains the required verification canary remains protected.
- Folder-list changes now use a protected undo journal across the live config
  and runtime/recovery manifests. If the manager is interrupted before commit,
  backup runs fail closed until the exact previous file set is restored and
  verified.
- Installer and folder-manager validation now consistently reject UNC/network
  sources. Local fixed or removable drive-letter sources are supported, with
  fixed NTFS required while VSS is enabled.
- The denser backup-health presentation leaves more room for the controls that
  determine what is protected, including on shorter windows.
- Keyboard navigation, visible focus states, accessible control names, readable
  action targets, contrast, and non-color-only status cues improve usability.

## The workflow behind the project

ResticBackuper grew out of the creator's real nightly Windows routine. That
deployment protects software projects, a local Google Drive mirror, Codex
configuration and home data, and ordinary personal folders. Reproducible
dependency trees, caches, and build outputs are excluded so the backup focuses
on original work and irreplaceable data.

Each run creates an encrypted, incremental snapshot in a local Restic
repository. A successful status also requires repository checks and a real
canary restore with content verification. A separate scheduled job copies the
encrypted repository to Google Drive as an off-site layer; that cloud sync is
specific to the creator's deployment and is not installed or configured by the
current ResticBackuper alpha.

## Install

Download both Windows x64 release files, verify that the SHA-256 values match,
extract the ZIP, review the included scripts, and run `Install.cmd`. Full
installation and recovery-test instructions are in the
[README](https://github.com/50sotero/ResticBackuper#readme).

SHA-256:

```text
b7726c704cae43d910710306be8b9145664cbef513dc17e08facc3e7b9397c8f  ResticBackuper-v0.1.0-alpha.2-windows-x64.zip
```

## Alpha warning

This remains an early Windows x64 alpha. The project and its executables are
not code-signed, broad Windows compatibility validation is incomplete, and
there is no in-place upgrade. Test with non-critical data, keep another backup,
store the recovery key separately, and complete an independent restore drill
before relying on it.

The release supports only local fixed or removable NTFS repositories. It does
not configure cloud replication, retention, pruning, automatic updates, or
repository deletion. Removing a protected folder affects future snapshots only
and does not erase historical backup data. Canary verification is deliberately
narrow and does not replace representative user-file restore tests.
