# Proofhold v0.2.0-alpha.1

Proofhold is the new desktop identity for the ResticBackuper backup runtime:

> **Backup you can verify.**

This early public alpha brings the shared animated dashboard to Windows and
macOS, adds a real macOS Restic host, and ships a reviewable Windows setup
bootstrapper alongside the transparent ZIP. The product name is new; existing
Windows executable, task, install-path, and protected-state identifiers keep
their `ResticBackuper` names so the operational contracts remain stable.

## What changed

- The dashboard now uses the literal Beautiful UI source elements pinned in
  `src/dashboard/web/vendor/UPSTREAM.md`, including the navigation rail, task
  rows, filter table, loading state, insight cards, and context cards.
- Motion is part of the product surface: page transitions, staggered content,
  expandable rows, loading animations, and the activity trend animate from real
  state. Reduced-motion preferences are respected, and the UI contains no
  default remote demo video or fabricated backup data.
- The Proofhold brand is applied to the shared desktop presentation while the
  Windows runtime keeps its `ResticBackuper` compatibility names.
- Windows release output now includes:
  `Proofhold-v0.2.0-alpha.1-windows-x64.zip`, the matching
  `Proofhold-v0.2.0-alpha.1-windows-x64-setup.exe`, and SHA-256 checksum files.
  The setup executable embeds the same ZIP and launches the existing reviewed
  PowerShell installer. It is not a second installer implementation.
- The Windows setup flow checks for WebView2 and obtains Microsoft's signed
  bootstrapper when the runtime is missing. The release does not bundle an
  unpinned WebView2 runtime inside the application.
- The macOS release provides arm64 and Intel x64 Electron builds. The real
  macOS service supports local folder repository setup, encrypted password
  storage through safe storage, actual Restic backup and repository checks,
  canary restore with hash verification, snapshot browsing, verified restore to
  a new or empty destination, cancellation, and a per-user daily launchd
  schedule.
- macOS release builds stage the official Restic 0.19.1 binary only after
  checking its architecture-specific SHA-256 value. Electron and web runtime
  notices are included with the macOS bundle.

## Platform boundaries

Windows retains the existing protected workflow: PowerShell 5.1 and .NET
Framework 4.8, UAC-approved managers, Task Scheduler, optional VSS capture,
CurrentUser DPAPI credentials, and the explicit DriveFS verification path.
Windows remains x64 in this alpha.

macOS supports local source folders and a local folder repository in the
packaged arm64 or x64 app. It does not claim Windows VSS, UAC, DPAPI, DriveFS
cloud proof, repository migration, or the advanced Windows repair workflows.
Its schedule is a per-user launchd job and requires a logged-in user session.

Both platforms require an independent restore test before the installation is
trusted with irreplaceable data. Proofhold does not automatically prune or
delete repository snapshots.

## Download and verify

For Windows, download the ZIP, setup executable, and adjacent checksum files
from the GitHub release. Verify each downloaded file before opening it. The
ZIP remains the easiest path to inspect the payload and run `Install.cmd`; the
setup executable is a convenience bootstrapper around that same payload.

For macOS, choose the arm64 DMG or ZIP on Apple Silicon and the x64 DMG or ZIP
on an Intel Mac:

```text
Proofhold-0.2.0-alpha.1-arm64.dmg
Proofhold-0.2.0-alpha.1-arm64.zip
Proofhold-0.2.0-alpha.1-x64.dmg
Proofhold-0.2.0-alpha.1-x64.zip
```

## Signing status

This alpha is not Authenticode-signed on Windows and is not Developer ID
signed or notarized on macOS. SmartScreen, antivirus, or Gatekeeper may warn.
The published checksums provide artifact integrity evidence; they do not prove
publisher identity.

## Validation

The Windows release test extracts the published ZIP, verifies its payload
manifest, performs two Restic backups with a temporary fixture, runs the
recovery-key representative drill, and restores changed data independently.
The macOS smoke test builds the shared web app, starts the real Electron host,
and exercises the real service and preload bridge against temporary data. A
full real Restic backup and restore integration test is enabled on macOS CI
when the runner has the pinned Restic binary available.

