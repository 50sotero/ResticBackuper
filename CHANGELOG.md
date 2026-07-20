# Changelog

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
