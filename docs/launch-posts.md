# Social launch posts

These drafts are ready to adapt and post. They describe the open-source alpha
without implying that its separate off-site sync is a built-in feature.

## X / Twitter

I open-sourced ResticBackuper. Nightly: projects, local Google Drive mirror,
Codex config + personal folders, minus reproducible output. Encrypted snapshots +
verified canary restores; I separately sync the repo off-site to Drive. Alpha:
https://github.com/50sotero/ResticBackuper

## LinkedIn

I built **ResticBackuper** for the backup routine I actually wanted on my own
Windows machine, and I have now released it as an open-source alpha.

Every night, my setup protects software projects, a local Google Drive mirror,
Codex configuration and home data, and personal folders. I exclude dependency
trees, caches, and build outputs that can be reproduced from source and
lockfiles, keeping the backup focused on original work and irreplaceable data.

The local layer uses an encrypted Restic repository for incremental snapshots
and fast recovery. A run is not shown as successful until repository checks
complete and a real canary file has been restored and its content verified. I
also use a separate scheduled job to copy the encrypted repository off-site to
Google Drive, with recovery material kept separately. That cloud-sync job is
part of my deployment, not a feature the current installer configures.

ResticBackuper packages the reusable parts of that setup:

- encrypted, incremental, deduplicated Restic snapshots
- optional VSS capture for open files
- daily Task Scheduler automation
- a live dashboard with progress, throughput, ETA and run history
- UAC-protected folder management from the dashboard
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
