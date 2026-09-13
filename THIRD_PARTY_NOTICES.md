# Third-party notices

Rewindle is an independent community project and is not affiliated with or
endorsed by the Restic project, the Python Software Foundation, Beautiful UI,
Motion, or any other upstream project listed here.

Release bundles redistribute these pinned upstream components:

- **Restic 0.19.1**, licensed under the BSD 2-Clause License. The full license
  is in [`licenses/RESTIC.txt`](licenses/RESTIC.txt).
- **Python 3.14.6 embeddable distribution**, licensed under the Python Software
  Foundation License and other notices included by Python. The build copies
  Python's complete `LICENSE.txt` into every release bundle as
  `licenses/PYTHON.txt`.

Exact upstream URLs and SHA-256 values are recorded in
[`dependencies.json`](dependencies.json). Neither dependency binary is stored
in this Git repository; the release builder downloads immutable versioned
archives and rejects any checksum mismatch.

## Shared dashboard

The desktop dashboard uses the following source and packages. Their license
texts and provenance are kept with the source when the release bundle includes
that source or runtime:

- **Beautiful UI**, pinned at commit
  `ff0f74d62d8be9d89bcb735b3632e31a6ccf88dc` from
  [slev12397/beautiful-ui](https://github.com/slev12397/beautiful-ui), is used
  for the navigation, task-row, filter, loading, insight, context, and motion
  primitives. The reference site is [beautifului.dev](https://www.beautifului.dev/).
  The copied source and its MIT license are documented in
  [`src/dashboard/web/vendor/UPSTREAM.md`](src/dashboard/web/vendor/UPSTREAM.md)
  and `src/dashboard/web/vendor/BEAUTIFULUI-MIT-LICENSE.txt`.
- **Motion 13.2.0**, an MIT-licensed React animation library, supplies page,
  stagger, and interaction motion. Its version is pinned by the web lockfile.
- **Liveline 0.0.7**, an MIT-licensed React chart library, renders the activity
  trend. Its version is pinned by the web lockfile.
- **Inter** and **JetBrains Mono** from Fontsource 5.3.0 are distributed under
  the SIL Open Font License 1.1. Their font notices remain in the web asset
  package and the generated brand assets.

The web UI preserves the upstream component structure and keyframes while
connecting the components to Rewindle state. Decorative motion does not
provide backup evidence and is disabled or reduced when the operating system
requests reduced motion.

## macOS runtime

The macOS desktop app uses Electron 43.7.0 and electron-builder 26.16.1. The
Electron distribution contains its own Chromium, Node.js, and transitive
dependency notices; the macOS packaging step copies those notices into the
application or release bundle as required by the upstream packages. The exact
versions are pinned in the root `package-lock.json`, and the release workflow
does not claim Developer ID signing or notarization without explicitly
configured Apple credentials.
