# Third-party notices

ResticBackuper is an independent community project and is not affiliated with
or endorsed by the Restic project or the Python Software Foundation.

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
