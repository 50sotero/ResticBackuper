# ResticBackuper v0.1.0-alpha.5

Alpha.5 adds an optional, verified Google Drive for desktop mirror while keeping
the live Restic repository on local storage. This remains an unsigned Windows
x64 alpha; keep another backup and perform representative restore drills.

## Highlights

- The optional post-install sync task targets a proven, resident Google Drive
  **Computers** mirror root. It does not place the actively written Restic
  repository in a streamed Drive folder.
- Missing immutable repository files are copied into private staging, hashed
  with SHA-256 and MD5, and promoted atomically. Existing non-identical files
  fail closed; no overwrite, mirror-delete, prune, or purge mode is used.
- The limited-user task coordinates with the protected backup through a
  read-only byte-range lock. The protected ACL is unchanged and user-writable
  scripts are not elevated.
- A compressed exact inventory and immutable commit proof are published only
  after repository identity, Restic check, canary restore, byte totals, file
  hashes, and Google Drive upload metadata all agree across stable samples.
- The dashboard distinguishes local verification from provider confirmation
  and restore evidence instead of treating a copied folder as an off-site
  success.
- Repository-ID-scoped public destinations prevent different repositories from
  colliding in the same provider root.
- DriveFS nullable in-flight metadata, stability timing, canary-path traversal,
  and long-running task/deadline behavior have dedicated fail-closed guards and
  regression coverage.

## Important limitations

- Google Drive for desktop enrollment is not automated. The operator must
  configure and prove the resident Computers mirror root before installing the
  optional task.
- DriveFS verification is compatibility-pinned and must be reviewed after
  provider schema/version changes.
- Provider queue checks are account-wide and can delay confirmation while
  unrelated Drive activity is pending.
- The mirror is append-only by design. Restic snapshot pruning and repository
  deletion are not included.
- The executables and scripts are not Authenticode-signed, and there is no
  automatic updater or in-place alpha upgrade.

## Download and verify

Download both `ResticBackuper-v0.1.0-alpha.5-windows-x64.zip` and the adjacent
`.sha256` file from the GitHub release. Compare the SHA-256 locally before
extracting, then test an independent restore before relying on the installation.
