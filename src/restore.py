"""Safe recovery entrypoint that also works from the standalone D: bundle."""

from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
import json
import ntpath
import os
from pathlib import Path
import re
import subprocess
import sys


SCRIPT_DIRECTORY = Path(__file__).resolve().parent
DEFAULT_CONFIG = SCRIPT_DIRECTORY / "backup-config.json"
CREATE_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--repository", type=Path, help="override repository path")
    parser.add_argument("--restic", type=Path, help="override Restic executable")
    parser.add_argument(
        "--recovery-key-file",
        type=Path,
        help="restricted recovery text file containing a 'Password:' line",
    )
    parser.add_argument("--list", action="store_true", help="list snapshots")
    parser.add_argument("--snapshot", default="latest")
    parser.add_argument("--target", type=Path)
    parser.add_argument(
        "--source",
        type=Path,
        help="restore one exact configured source without recreating absolute-path ancestors",
    )
    parser.add_argument(
        "--include",
        action="append",
        default=[],
        help="snapshot path/pattern to restore; repeatable",
    )
    return parser.parse_args(argv)


def load_restore_config(path: Path) -> dict:
    value = json.loads(path.resolve(strict=True).read_text(encoding="utf-8"))
    required = {"repository", "repository_volume_serial", "sources"}
    missing = sorted(required - value.keys())
    if missing:
        raise ValueError(f"restore configuration is missing: {', '.join(missing)}")
    return value


def choose_existing_path(candidates: list[Path | None], label: str) -> Path:
    for candidate in candidates:
        if candidate is not None and candidate.is_file():
            return candidate.resolve(strict=True)
    shown = ", ".join(str(item) for item in candidates if item is not None)
    raise FileNotFoundError(f"no usable {label} found; checked: {shown}")


def volume_serial(path: Path) -> str:
    root = ntpath.splitdrive(ntpath.abspath(str(path)))[0] + "\\"
    serial = wintypes.DWORD()
    maximum_component = wintypes.DWORD()
    flags = wintypes.DWORD()
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    if not kernel32.GetVolumeInformationW(
        root,
        None,
        0,
        ctypes.byref(serial),
        ctypes.byref(maximum_component),
        ctypes.byref(flags),
        None,
        0,
    ):
        raise ctypes.WinError(ctypes.get_last_error())
    return f"{serial.value:08X}"


def is_within(candidate: Path, parent: Path) -> bool:
    child = ntpath.normcase(ntpath.abspath(str(candidate)))
    root = ntpath.normcase(ntpath.abspath(str(parent)))
    try:
        return ntpath.commonpath([child, root]) == root
    except ValueError:
        return False


def windows_snapshot_path(path: Path) -> str:
    absolute = ntpath.abspath(str(path))
    drive, tail = ntpath.splitdrive(absolute)
    if not drive or drive.startswith("\\"):
        raise ValueError(f"unsupported configured source path: {absolute}")
    components = [part for part in tail.replace("\\", "/").split("/") if part]
    return "/" + "/".join([drive.rstrip(":"), *components])


def select_configured_source(requested: Path, configured: list[str]) -> Path:
    requested_text = ntpath.normcase(ntpath.abspath(str(requested)))
    matches = [
        Path(source)
        for source in configured
        if ntpath.normcase(ntpath.abspath(source)) == requested_text
    ]
    if len(matches) != 1:
        raise ValueError("--source must exactly match one source recorded in backup-config.json")
    return matches[0]


def recovery_password(path: Path) -> str:
    text = path.resolve(strict=True).read_text(encoding="utf-8-sig")
    matches = re.findall(r"^Password:\s*(\S+)\s*$", text, flags=re.MULTILINE)
    if len(matches) != 1 or len(matches[0]) < 40:
        raise ValueError("recovery key file must contain exactly one valid Password line")
    return matches[0]


def password_command(config: dict) -> str | None:
    python = Path(config.get("python_executable", ""))
    secret = Path(config.get("secret_file", ""))
    helper = choose_existing_path(
        [
            SCRIPT_DIRECTORY / "secret_store.py",
            Path(config.get("restic_executable", "")).parent.parent / "secret_store.py",
        ],
        "DPAPI helper",
    ) if secret.is_file() and python.is_file() else None
    if helper is None:
        return None
    arguments = [
        str(python).replace("\\", "/"),
        "-I",
        str(helper).replace("\\", "/"),
        "reveal",
        "--secret-file",
        str(secret).replace("\\", "/"),
    ]
    return subprocess.list2cmdline(arguments)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    config = load_restore_config(args.config)
    configured_repository = Path(config["repository"])
    repository = (args.repository or configured_repository).resolve(strict=True)
    if not (repository / "config").is_file():
        raise FileNotFoundError(f"not an initialized Restic repository: {repository}")
    if args.repository is None:
        actual_serial = volume_serial(repository)
        expected_serial = str(config["repository_volume_serial"]).upper()
        if actual_serial != expected_serial:
            raise RuntimeError(
                f"repository volume mismatch: expected {expected_serial}, got {actual_serial}; "
                "use --repository only after verifying a deliberately moved copy"
            )

    restic = choose_existing_path(
        [
            args.restic,
            Path(config.get("restic_executable", "")),
            SCRIPT_DIRECTORY / "restic.exe",
            Path(config.get("recovery_tools_directory", "")) / "restic.exe",
        ],
        "Restic executable",
    )
    command = [str(restic), "--repo", str(repository), "--retry-lock", "5m"]
    environment = os.environ.copy()
    interactive_password = True
    if args.recovery_key_file:
        environment["RESTIC_PASSWORD"] = recovery_password(args.recovery_key_file)
        interactive_password = False
    else:
        dpapi_command = password_command(config)
        if dpapi_command:
            command.extend(["--password-command", dpapi_command])
            interactive_password = False

    if args.list or args.target is None:
        command.append("snapshots")
    else:
        target = args.target.resolve(strict=False)
        forbidden = [repository, *[Path(item) for item in config.get("sources", [])]]
        if any(is_within(target, path) or is_within(path, target) for path in forbidden):
            raise RuntimeError("restore target must not overlap a source or the repository")
        if target.exists() and (not target.is_dir() or any(target.iterdir())):
            raise RuntimeError("restore target must be absent or an empty directory")
        target.mkdir(parents=True, exist_ok=True)
        snapshot_selector = args.snapshot
        if args.source is not None:
            selected_source = select_configured_source(
                args.source, list(config.get("sources", []))
            )
            snapshot_selector = f"{args.snapshot}:{windows_snapshot_path(selected_source)}"
        command.extend(
            [
                "restore",
                snapshot_selector,
                "--target",
                str(target),
                "--overwrite",
                "never",
                "--verify",
            ]
        )
        for pattern in args.include:
            command.extend(["--include", pattern])

    try:
        return subprocess.call(
            command,
            cwd=SCRIPT_DIRECTORY,
            stdin=None if interactive_password else subprocess.DEVNULL,
            env=environment,
            shell=False,
            creationflags=CREATE_NO_WINDOW,
        )
    finally:
        environment.pop("RESTIC_PASSWORD", None)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"RESTORE FAILED: {error}", file=sys.stderr)
        raise SystemExit(1)
