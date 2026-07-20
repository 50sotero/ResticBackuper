# Changelog

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
