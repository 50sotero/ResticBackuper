"""Shared safety and process helpers for ResticBackuper."""

from __future__ import annotations

from contextlib import AbstractContextManager
from datetime import datetime, timezone
import errno
import fnmatch
import hashlib
import json
import msvcrt
import ntpath
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import sys
import time
from typing import Any, Callable


PROJECT_DIRECTORY = Path(__file__).resolve().parent
DEFAULT_CONFIG = PROJECT_DIRECTORY / "backup-config.json"
PROTECTED_CONFIG = Path(r"C:\Program Files\ResticBackuper\backup-config.json")
PROTECTED_STATE_DIRECTORY = Path(r"C:\ProgramData\ResticBackuper")
SOURCE_UPDATE_JOURNAL_NAME = "source-update.journal.json"
PROTECTED_SOURCE_UPDATE_JOURNAL = (
    PROTECTED_STATE_DIRECTORY / SOURCE_UPDATE_JOURNAL_NAME
)
CREATE_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)
ATOMIC_WRITE_ATTEMPTS = 20
ATOMIC_WRITE_MAX_DELAY_SECONDS = 0.5
PROCESS_STOP_TIMEOUT_SECONDS = 10
JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000
JOB_OBJECT_EXTENDED_LIMIT_INFORMATION = 9


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _is_retryable_atomic_write_error(error: OSError) -> bool:
    """Return whether a Windows file-sharing race may clear on retry."""
    return (
        isinstance(error, PermissionError)
        or getattr(error, "winerror", None) in {5, 32, 33, 1224}
        or error.errno in {errno.EACCES, errno.EBUSY, errno.EPERM}
    )


def atomic_write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    for attempt in range(ATOMIC_WRITE_ATTEMPTS):
        temporary = path.with_name(f".{path.name}.{secrets.token_hex(8)}.tmp")
        try:
            with temporary.open("w", encoding="utf-8", newline="\n") as handle:
                json.dump(value, handle, indent=2, sort_keys=True, ensure_ascii=False)
                handle.write("\n")
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temporary, path)
            return
        except OSError as error:
            if (
                attempt + 1 >= ATOMIC_WRITE_ATTEMPTS
                or not _is_retryable_atomic_write_error(error)
            ):
                raise
            delay = min(
                0.05 * (2**attempt),
                ATOMIC_WRITE_MAX_DELAY_SECONDS,
            )
            time.sleep(delay)
        finally:
            try:
                temporary.unlink(missing_ok=True)
            except OSError:
                # A stale telemetry temp is preferable to masking the original
                # write result. Every attempt uses a collision-resistant name.
                pass


def _is_within(candidate: Path, parent: Path) -> bool:
    child_text = ntpath.normcase(ntpath.abspath(str(candidate)))
    parent_text = ntpath.normcase(ntpath.abspath(str(parent)))
    try:
        return ntpath.commonpath([child_text, parent_text]) == parent_text
    except ValueError:
        return False


def _exclude_can_touch_protected_tree(pattern: str, protected: str) -> bool:
    normalized = pattern.replace("\\", "/").lower()
    if normalized.startswith("!"):
        raise ValueError(f"negated exclude patterns are not allowed: {pattern}")
    components = [part for part in normalized.split("/") if part]
    if components == ["**"]:
        return True
    return any(
        component != "**" and fnmatch.fnmatchcase(protected, component)
        for component in components
    ) or (components and components[-1] == "**")


def load_config(path: Path = DEFAULT_CONFIG, *, require_repository: bool = True) -> dict[str, Any]:
    path = path.resolve(strict=True)
    config = json.loads(path.read_text(encoding="utf-8"))
    required = {
        "repository",
        "repository_volume_serial",
        "restic_executable",
        "recovery_tools_directory",
        "python_executable",
        "state_directory",
        "secret_file",
        "recovery_key_file",
        "exclude_file",
        "canary_file",
        "hostname",
        "scheduled_tag",
        "sources",
    }
    missing = sorted(required - config.keys())
    if missing:
        raise ValueError(f"configuration is missing keys: {', '.join(missing)}")
    if config.get("schema_version") != 1:
        raise ValueError("unsupported backup configuration schema")

    for key in (
        "repository",
        "restic_executable",
        "recovery_tools_directory",
        "python_executable",
        "state_directory",
        "secret_file",
        "recovery_key_file",
        "exclude_file",
        "canary_file",
    ):
        config[key] = str(Path(config[key]).resolve(strict=False))

    restic = Path(config["restic_executable"])
    python = Path(config["python_executable"])
    excludes = Path(config["exclude_file"])
    canary = Path(config["canary_file"])
    if not restic.is_file():
        raise FileNotFoundError(f"Restic executable is missing: {restic}")
    if not python.is_file():
        raise FileNotFoundError(f"Python executable is missing: {python}")
    if not excludes.is_file():
        raise FileNotFoundError(f"exclude file is missing: {excludes}")
    if not canary.is_file():
        raise FileNotFoundError(f"restore canary is missing: {canary}")

    patterns = [
        line.strip()
        for line in excludes.read_text(encoding="utf-8").splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    ]
    if not patterns:
        raise ValueError("exclude file has no active patterns")
    for pattern in patterns:
        protected_matches = [
            protected
            for protected in (".git", ".codex-worktrees")
            if _exclude_can_touch_protected_tree(pattern, protected)
        ]
        if protected_matches:
            raise ValueError(
                "exclude pattern could omit protected repository state "
                f"({', '.join(protected_matches)}): {pattern}"
            )

    sources = [Path(item).resolve(strict=True) for item in config["sources"]]
    if not sources or any(not source.is_dir() for source in sources):
        raise ValueError("every configured source must be an existing directory")
    normalized_sources = [ntpath.normcase(ntpath.normpath(str(item))) for item in sources]
    if len(normalized_sources) != len(set(normalized_sources)):
        raise ValueError("duplicate source path in configuration")
    config["sources"] = [str(item) for item in sources]

    repository = Path(config["repository"])
    for source in sources:
        if _is_within(repository, source) or _is_within(source, repository):
            raise ValueError(f"repository and source overlap: {source}")
    if require_repository and not (repository / "config").is_file():
        raise FileNotFoundError(f"initialized Restic repository is missing: {repository}")
    return config


def volume_serial(path: Path) -> str:
    import ctypes
    from ctypes import wintypes

    root = ntpath.splitdrive(ntpath.abspath(str(path)))[0] + "\\"
    serial = wintypes.DWORD()
    maximum_component = wintypes.DWORD()
    flags = wintypes.DWORD()
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.GetVolumeInformationW.argtypes = [
        wintypes.LPCWSTR,
        wintypes.LPWSTR,
        wintypes.DWORD,
        ctypes.POINTER(wintypes.DWORD),
        ctypes.POINTER(wintypes.DWORD),
        ctypes.POINTER(wintypes.DWORD),
        wintypes.LPWSTR,
        wintypes.DWORD,
    ]
    kernel32.GetVolumeInformationW.restype = wintypes.BOOL
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


def validate_repository_volume(config: dict[str, Any]) -> None:
    actual = volume_serial(Path(config["repository"]))
    expected = str(config["repository_volume_serial"]).upper()
    if actual != expected:
        raise RuntimeError(
            f"repository volume mismatch: expected {expected}, observed {actual}"
        )


def ensure_free_space(config: dict[str, Any]) -> int:
    free = shutil.disk_usage(Path(config["repository"]).anchor).free
    minimum = int(float(config.get("minimum_free_gib", 0)) * 1024**3)
    if free < minimum:
        raise RuntimeError(
            f"repository volume has {free / 1024**3:.1f} GiB free; "
            f"at least {minimum / 1024**3:.1f} GiB is required"
        )
    return free


def password_command(config: dict[str, Any]) -> str:
    helper = PROJECT_DIRECTORY / "secret_store.py"
    # Restic parses --password-command with POSIX shellword rules even on
    # Windows. Forward slashes prevent drive paths such as C:\\Users from being
    # split at backslash escapes before CreateProcess receives them.
    arguments = [
        config["python_executable"].replace("\\", "/"),
        "-I",
        "-S",
        "-B",
        str(helper).replace("\\", "/"),
        "reveal",
        "--secret-file",
        config["secret_file"].replace("\\", "/"),
    ]
    return subprocess.list2cmdline(arguments)


def restic_base(config: dict[str, Any], *, json_output: bool = False) -> list[str]:
    cache = Path(config["state_directory"]) / "cache"
    cache.mkdir(parents=True, exist_ok=True)
    command = [
        config["restic_executable"],
        "--repo",
        config["repository"],
        "--password-command",
        password_command(config),
        "--cache-dir",
        str(cache),
        "--retry-lock",
        "5m",
    ]
    if json_output:
        command.append("--json")
    return command


def sanitized_command(command: list[str]) -> list[str]:
    result = list(command)
    try:
        index = result.index("--password-command")
        result[index + 1] = "<DPAPI password command>"
    except (ValueError, IndexError):
        pass
    return result


class _WindowsKillOnCloseJob(AbstractContextManager["_WindowsKillOnCloseJob"]):
    """Keep a process tree tied to the lifetime of its Python supervisor."""

    def __init__(self) -> None:
        self.handle = None
        self.kernel32 = None

    def __enter__(self) -> "_WindowsKillOnCloseJob":
        import ctypes
        from ctypes import wintypes

        class JobObjectBasicLimitInformation(ctypes.Structure):
            _fields_ = [
                ("PerProcessUserTimeLimit", ctypes.c_int64),
                ("PerJobUserTimeLimit", ctypes.c_int64),
                ("LimitFlags", wintypes.DWORD),
                ("MinimumWorkingSetSize", ctypes.c_size_t),
                ("MaximumWorkingSetSize", ctypes.c_size_t),
                ("ActiveProcessLimit", wintypes.DWORD),
                ("Affinity", ctypes.c_size_t),
                ("PriorityClass", wintypes.DWORD),
                ("SchedulingClass", wintypes.DWORD),
            ]

        class IoCounters(ctypes.Structure):
            _fields_ = [
                ("ReadOperationCount", ctypes.c_uint64),
                ("WriteOperationCount", ctypes.c_uint64),
                ("OtherOperationCount", ctypes.c_uint64),
                ("ReadTransferCount", ctypes.c_uint64),
                ("WriteTransferCount", ctypes.c_uint64),
                ("OtherTransferCount", ctypes.c_uint64),
            ]

        class JobObjectExtendedLimitInformation(ctypes.Structure):
            _fields_ = [
                ("BasicLimitInformation", JobObjectBasicLimitInformation),
                ("IoInfo", IoCounters),
                ("ProcessMemoryLimit", ctypes.c_size_t),
                ("JobMemoryLimit", ctypes.c_size_t),
                ("PeakProcessMemoryUsed", ctypes.c_size_t),
                ("PeakJobMemoryUsed", ctypes.c_size_t),
            ]

        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
        kernel32.CreateJobObjectW.restype = wintypes.HANDLE
        kernel32.SetInformationJobObject.argtypes = [
            wintypes.HANDLE,
            ctypes.c_int,
            ctypes.c_void_p,
            wintypes.DWORD,
        ]
        kernel32.SetInformationJobObject.restype = wintypes.BOOL
        kernel32.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
        kernel32.AssignProcessToJobObject.restype = wintypes.BOOL
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL

        handle = kernel32.CreateJobObjectW(None, None)
        if not handle:
            raise ctypes.WinError(ctypes.get_last_error())

        information = JobObjectExtendedLimitInformation()
        information.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not kernel32.SetInformationJobObject(
            handle,
            JOB_OBJECT_EXTENDED_LIMIT_INFORMATION,
            ctypes.byref(information),
            ctypes.sizeof(information),
        ):
            error = ctypes.get_last_error()
            kernel32.CloseHandle(handle)
            raise ctypes.WinError(error)

        self.handle = handle
        self.kernel32 = kernel32
        return self

    def assign(self, process: subprocess.Popen[str]) -> None:
        import ctypes
        from ctypes import wintypes

        if self.handle is None or self.kernel32 is None:
            raise RuntimeError("process job is not active")
        process_handle = getattr(process, "_handle", None)
        if process_handle is None:
            raise RuntimeError("Python did not expose the child process handle")
        if not self.kernel32.AssignProcessToJobObject(
            self.handle,
            wintypes.HANDLE(int(process_handle)),
        ):
            error = ctypes.get_last_error()
            if process.poll() is None:
                raise ctypes.WinError(error)

    def close(self) -> None:
        if self.handle is not None and self.kernel32 is not None:
            handle = self.handle
            self.handle = None
            self.kernel32.CloseHandle(handle)

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        self.close()


def _stop_process(process: subprocess.Popen[str]) -> None:
    """Best-effort child cleanup that never masks the triggering exception."""
    try:
        if process.poll() is not None:
            return
    except OSError:
        pass
    try:
        process.terminate()
    except OSError:
        pass
    try:
        process.wait(timeout=PROCESS_STOP_TIMEOUT_SECONDS)
        return
    except (OSError, subprocess.TimeoutExpired):
        pass
    try:
        process.kill()
    except OSError:
        pass
    try:
        process.wait(timeout=PROCESS_STOP_TIMEOUT_SECONDS)
    except (OSError, subprocess.TimeoutExpired):
        pass


def run_capture(
    command: list[str], *, cwd: Path = PROJECT_DIRECTORY
) -> subprocess.CompletedProcess[str]:
    with _WindowsKillOnCloseJob() as job:
        process = subprocess.Popen(
            command,
            cwd=cwd,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            errors="replace",
            shell=False,
            creationflags=CREATE_NO_WINDOW,
        )
        try:
            job.assign(process)
            stdout, stderr = process.communicate()
            return subprocess.CompletedProcess(
                command,
                process.returncode,
                stdout,
                stderr,
            )
        except BaseException:
            _stop_process(process)
            raise


def stream_command(
    command: list[str],
    log_handle,
    on_line: Callable[[str], None] | None = None,
    *,
    cwd: Path = PROJECT_DIRECTORY,
) -> int:
    with _WindowsKillOnCloseJob() as job:
        process = subprocess.Popen(
            command,
            cwd=cwd,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
            bufsize=1,
            shell=False,
            creationflags=CREATE_NO_WINDOW,
        )
        try:
            job.assign(process)
            assert process.stdout is not None
            for line in process.stdout:
                log_handle.write(line)
                log_handle.flush()
                if on_line is not None:
                    on_line(line.rstrip("\r\n"))
            return process.wait()
        except BaseException:
            # Never leave Restic writing to the repository after its supervising
            # wrapper can no longer consume output or publish protected status.
            _stop_process(process)
            raise
        finally:
            if process.stdout is not None:
                try:
                    process.stdout.close()
                except OSError:
                    pass


class RunLockUnavailable(RuntimeError):
    """Raised when another protected operation owns byte zero of run.lock."""


class SourceUpdateJournalPresent(RuntimeError):
    """Raised when source-manager crash recovery must finish before a run."""


class RunLock(AbstractContextManager["RunLock"]):
    def __init__(self, path: Path):
        self.path = path
        self.handle = None

    def __enter__(self) -> "RunLock":
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.handle = self.path.open("a+b", buffering=0)
        self.handle.seek(0, os.SEEK_END)
        if self.handle.tell() == 0:
            self.handle.write(b"\0")
        self.handle.seek(0)
        try:
            msvcrt.locking(self.handle.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError as error:
            self.handle.close()
            self.handle = None
            raise RunLockUnavailable(
                "another backup process already holds the run lock"
            ) from error
        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        if self.handle is not None:
            handle = self.handle
            self.handle = None
            try:
                handle.seek(0)
                msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            finally:
                # Closing a Windows file handle releases its byte locks even
                # if seeking or the explicit unlock operation itself fails.
                handle.close()


def _same_windows_path(left: Path, right: Path) -> bool:
    return ntpath.normcase(ntpath.abspath(str(left))) == ntpath.normcase(
        ntpath.abspath(str(right))
    )


def _preliminary_state_directory(config_path: Path) -> Path:
    """Choose only the lock location; authoritative config is loaded later."""
    if _same_windows_path(config_path, PROTECTED_CONFIG):
        return PROTECTED_STATE_DIRECTORY
    preliminary = json.loads(config_path.read_text(encoding="utf-8"))
    if not isinstance(preliminary, dict):
        raise ValueError("backup configuration must be one JSON object")
    state_value = preliminary.get("state_directory")
    if not isinstance(state_value, str) or not state_value:
        raise ValueError("configuration has no valid state_directory for its run lock")
    return Path(state_value).resolve(strict=False)


def source_update_journal_path(state_directory: Path) -> Path:
    """Return the fixed journal name below the already selected lock state."""
    if _same_windows_path(state_directory, PROTECTED_STATE_DIRECTORY):
        return PROTECTED_SOURCE_UPDATE_JOURNAL
    return state_directory / SOURCE_UPDATE_JOURNAL_NAME


def load_config_under_lock(
    path: Path = DEFAULT_CONFIG,
    *,
    require_repository: bool = True,
    before_lock: Callable[[], None] | None = None,
) -> tuple[dict[str, Any], RunLock]:
    """Acquire the shared byte lock, then load and validate current config.

    Protected scheduled runs choose the fixed ProgramData lock without reading
    configuration first. Disposable/manual configurations read only their lock
    directory before locking, then reload the full configuration and reject a
    state-directory change. Thus a process paused before locking cannot later
    run source paths that a completed source-manager transaction replaced.
    """
    config_path = path.resolve(strict=True)
    lock_state = _preliminary_state_directory(config_path)
    if before_lock is not None:
        before_lock()
    run_lock = RunLock(lock_state / "run.lock")
    run_lock.__enter__()
    try:
        journal_path = source_update_journal_path(lock_state)
        if os.path.lexists(journal_path):
            raise SourceUpdateJournalPresent(
                "source-update recovery is pending; run the protected source "
                "manager to reconcile its journal before backup"
            )
        config = load_config(config_path, require_repository=require_repository)
        authoritative_state = Path(config["state_directory"]).resolve(strict=False)
        if not _same_windows_path(authoritative_state, lock_state):
            raise RuntimeError(
                "configuration state_directory changed while waiting for the run lock"
            )
        return config, run_lock
    except BaseException:
        run_lock.__exit__(None, None, None)
        raise


def windows_snapshot_path(path: Path) -> str:
    absolute = ntpath.abspath(str(path))
    drive, tail = ntpath.splitdrive(absolute)
    if not drive or drive.startswith("\\"):
        raise ValueError(f"unsupported canary path form: {absolute}")
    components = [part for part in tail.replace("\\", "/").split("/") if part]
    return "/" + "/".join([drive.rstrip(":"), *components])
