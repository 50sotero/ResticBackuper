# Contributing

Issues, documentation improvements, Windows compatibility reports, and focused
pull requests are welcome.

Before submitting code:

1. Do not include real paths, usernames, hostnames, volume serials, logs,
   repository data, DPAPI envelopes, or recovery keys.
2. Run `python -m unittest discover -s tests -v` on Windows.
3. Parse every PowerShell file with the PowerShell AST parser.
4. Build both C# applications and the release ZIP.
5. Explain safety implications for changes touching ACLs, elevation, tasks,
   password handling, restore behavior, or repository writes.

Keep changes narrow. Destructive retention, pruning, repository deletion, and
state purging must never be enabled by default.
