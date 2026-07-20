# ResticBackuper v0.1.0-alpha.1

This is the first public alpha of ResticBackuper: a transparent Windows x64
package for verified, encrypted, incremental backups powered by Restic.

## Highlights

- encrypted and deduplicated Restic snapshots;
- optional VSS capture for open files on local NTFS volumes;
- daily Task Scheduler automation through a supervised launcher;
- an animated dashboard with progress, ETA, throughput, and local run history;
- a visible inventory of backed-up folders with UAC-protected add/remove
  controls;
- repository structure checks and a real canary restore before a run is marked
  successful; and
- a separate recovery key plus portable recovery tools.

The ZIP embeds checksum-pinned Restic 0.19.1 and Python 3.14.6 runtimes. A
separate Restic or Python installation is not required.

## Install

Download both release files, verify that the SHA-256 value matches, extract the
ZIP, review the included scripts, and run `Install.cmd`. Full installation and
recovery-test instructions are in the
[README](https://github.com/50sotero/ResticBackuper#readme).

SHA-256:

```text
aabeb4218cf8ec97e4c13dcdcf6a6b09b27447c960e166f1b3e42e6acf740ac4  ResticBackuper-v0.1.0-alpha.1-windows-x64.zip
```

## Alpha warning

The project and its executables are not code-signed and have not yet been
validated across a broad range of Windows machines. Test with non-critical
data, keep another backup, store the recovery key separately, and complete an
independent restore drill before relying on this release.

This release does not automatically forget or prune snapshots. Removing a
folder from the dashboard affects future snapshots only and does not delete
historical backup data.
