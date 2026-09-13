# ResticBackuper v0.1.0-alpha.8

Alpha.8 makes the Recovery Readiness restore-drill warning actionable inside
the Windows dashboard. This remains an unsigned Windows x64 alpha. Keep the
recovery key separately, maintain another backup appropriate to your threat
model, and inspect representative restores before relying on it.

## What changed

- Recovery Readiness now offers **Run restore drill** when the repository,
  recovery bundle, recovery key, last backup verification, transaction state,
  and lock state make a drill safe.
- One explicit same-user Windows approval launches the protected restore
  manager. The normal-user dashboard never reads a credential or invokes
  Restic directly.
- The manager selects the exact latest verified snapshot for the current plan
  and generation. It creates a new nonce-bound protected destination and uses
  the independent recovery key to restore the canary plus a bounded ordinary-
  data sample with no overwrite and full Restic verification.
- Passing evidence additionally requires the expected canary length and
  SHA-256, bounded sample sizes, and an exact restored-file inventory. The
  protected history is replaced atomically only after every check succeeds.
- A failed, partial, or verification-failed drill retains any recovered output
  for inspection but does not create a passing history entry. If evidence
  recording itself fails, the restored sample remains available and Readiness
  continues to request review.
- The Readiness evaluator accepts only schema-2 representative-drill evidence
  bound to the current plan, generation, repository identity, exact plan-bound
  snapshot, recovery-key credential source, and protected read-only ACL.

## Safety boundaries

The drill never backs up, initializes, forgets, prunes, deletes, unlocks, or
rewrites repository objects. It never overwrites an existing restore target and
does not change sources, keys, snapshots, or scheduled tasks. Its retained
sample is deliberately bounded; it proves a representative recovery path but
is not a full disaster-recovery exercise.

## Important limitations

- Google Drive for desktop must be signed in and healthy for a live streamed
  repository to be available.
- The project is not Authenticode-signed and automatic alpha upgrades are not
  supported.
- A streamed repository is not an offline or immutable second copy.

## Download and verify

Download `ResticBackuper-v0.1.0-alpha.8-windows-x64.zip` and its adjacent
`.sha256` file. Compare the SHA-256 values before extracting, then run and
inspect a representative recovery drill before relying on the installation.
