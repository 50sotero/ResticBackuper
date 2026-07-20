# Launch posts

These drafts describe the v0.1.0-alpha.1 release as published. Replace the
release link if a version-specific URL is preferred.

## X / Twitter

I just open-sourced ResticBackuper: verified, encrypted, incremental Windows
backups powered by Restic. It adds VSS, scheduling, a live dashboard with
folder management, and real canary-restore verification. Alpha:
https://github.com/50sotero/ResticBackuper

## LinkedIn

I just open-sourced **ResticBackuper**, a project that turns a careful Restic
setup into a guided Windows backup package.

It combines encrypted, incremental and deduplicated snapshots with:

- VSS capture for open files
- daily Task Scheduler automation
- a live dashboard with progress, throughput, ETA and run history
- UAC-protected folder management from the dashboard
- repository structure checks
- a real canary restore with content verification before a run is marked
  successful
- a separate recovery key for disaster recovery

The first release, **v0.1.0-alpha.1**, is deliberately transparent: it ships as
a reviewable ZIP installer with pinned, checksum-verified Restic and Python
runtimes. The source is available under the MIT License.

This is an early Windows x64 alpha, not a promise that your data is safe. The
binaries are not yet code-signed, and every backup setup needs an independent
restore test before it can be trusted. I would especially value feedback on
Windows compatibility, installer safety, recovery ergonomics and the dashboard.

Repository and release:
https://github.com/50sotero/ResticBackuper

#opensource #backup #restic #windows #dataprotection #python
