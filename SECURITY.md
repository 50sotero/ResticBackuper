# Security policy

## Supported versions

This project is currently an alpha. Security fixes are applied to the newest
published release only.

## Reporting a vulnerability

Please do not open a public issue for vulnerabilities involving credential
exposure, privilege escalation, path traversal, unsafe ACLs, repository
corruption, or supply-chain verification. Use GitHub's private security
advisory flow for this repository instead.

Include the affected version, Windows version, reproduction steps, expected
impact, and whether any repository password or recovery key was exposed. Never
attach a real repository password, DPAPI envelope, recovery key, private file,
or backup repository.

## Security model

- The repository password is randomly generated and stored in a Windows
  CurrentUser DPAPI envelope.
- Restic receives it through `--password-command`; it is not placed in task
  arguments or normal logs.
- The scheduled task runs a manifest-hashed runtime from Program Files through
  a kill-on-close native supervisor.
- VSS requires a Highest-level scheduled task. The installer therefore
  restricts the runtime and state trees against normal-user writes.
- The limited dashboard never writes protected configuration directly. Source
  changes require same-user UAC elevation, the shared backup lock, and strict
  path/overlap validation.
- The plaintext recovery key is intentionally separate. Anyone with that key
  and the repository can decrypt the backup.

No backup tool can protect against every threat. Keep an offline recovery-key
copy, test restores, and maintain at least one repository copy that is not
writable by ordinary desktop applications.
