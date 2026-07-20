"""Current-user DPAPI storage for the Restic repository password.

The only command that emits the password is ``reveal``. It exists solely for
Restic's --password-command integration and must never be logged.
"""

from __future__ import annotations

import argparse
import base64
import csv
import ctypes
from ctypes import wintypes
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
import tempfile
from typing import Final


SCHEMA_VERSION: Final = 1
CRYPTPROTECT_UI_FORBIDDEN: Final = 0x1


class DATA_BLOB(ctypes.Structure):
    _fields_ = [
        ("cbData", wintypes.DWORD),
        ("pbData", ctypes.POINTER(ctypes.c_ubyte)),
    ]


def _windows_libraries():
    if os.name != "nt":
        raise RuntimeError("DPAPI secret storage is available only on Windows")

    crypt32 = ctypes.WinDLL("crypt32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

    crypt32.CryptProtectData.argtypes = [
        ctypes.POINTER(DATA_BLOB),
        wintypes.LPCWSTR,
        ctypes.POINTER(DATA_BLOB),
        ctypes.c_void_p,
        ctypes.c_void_p,
        wintypes.DWORD,
        ctypes.POINTER(DATA_BLOB),
    ]
    crypt32.CryptProtectData.restype = wintypes.BOOL
    crypt32.CryptUnprotectData.argtypes = [
        ctypes.POINTER(DATA_BLOB),
        ctypes.c_void_p,
        ctypes.POINTER(DATA_BLOB),
        ctypes.c_void_p,
        ctypes.c_void_p,
        wintypes.DWORD,
        ctypes.POINTER(DATA_BLOB),
    ]
    crypt32.CryptUnprotectData.restype = wintypes.BOOL
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p
    return crypt32, kernel32


def _system_executable(name: str) -> str:
    """Resolve a Windows utility without consulting CWD, PATH, or env vars."""
    if os.name != "nt" or not name.lower().endswith(".exe"):
        raise RuntimeError("trusted Windows utility resolution is unavailable")
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.GetSystemDirectoryW.argtypes = [wintypes.LPWSTR, wintypes.UINT]
    kernel32.GetSystemDirectoryW.restype = wintypes.UINT
    buffer = ctypes.create_unicode_buffer(32768)
    length = kernel32.GetSystemDirectoryW(buffer, len(buffer))
    if length == 0 or length >= len(buffer):
        raise ctypes.WinError(ctypes.get_last_error())
    executable = Path(buffer.value) / name
    if not executable.is_file():
        raise FileNotFoundError(f"required Windows utility is missing: {executable}")
    return str(executable)


def _input_blob(data: bytes) -> tuple[DATA_BLOB, ctypes.Array]:
    buffer = ctypes.create_string_buffer(data)
    blob = DATA_BLOB(
        len(data), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_ubyte))
    )
    return blob, buffer


def protect(secret: bytes) -> bytes:
    crypt32, kernel32 = _windows_libraries()
    input_blob, input_buffer = _input_blob(secret)
    output_blob = DATA_BLOB()
    if not crypt32.CryptProtectData(
        ctypes.byref(input_blob),
        "ResticBackuper repository password",
        None,
        None,
        None,
        CRYPTPROTECT_UI_FORBIDDEN,
        ctypes.byref(output_blob),
    ):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        return ctypes.string_at(output_blob.pbData, output_blob.cbData)
    finally:
        kernel32.LocalFree(output_blob.pbData)


def unprotect(ciphertext: bytes) -> bytes:
    crypt32, kernel32 = _windows_libraries()
    input_blob, input_buffer = _input_blob(ciphertext)
    output_blob = DATA_BLOB()
    if not crypt32.CryptUnprotectData(
        ctypes.byref(input_blob),
        None,
        None,
        None,
        None,
        CRYPTPROTECT_UI_FORBIDDEN,
        ctypes.byref(output_blob),
    ):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        return ctypes.string_at(output_blob.pbData, output_blob.cbData)
    finally:
        kernel32.LocalFree(output_blob.pbData)


def _atomic_create(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.{secrets.token_hex(8)}.tmp")
    descriptor: int | None = None
    try:
        descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        # Apply and verify the final private DACL while the file is still empty.
        # This prevents plaintext recovery material from ever occupying an
        # inherited group-readable temporary file.
        restrict_acl(temporary)
        with os.fdopen(descriptor, "wb") as handle:
            descriptor = None
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
        if path.exists():
            raise FileExistsError(f"refusing to replace existing secret material: {path}")
        os.rename(temporary, path)
    finally:
        if descriptor is not None:
            os.close(descriptor)
        if temporary.exists():
            temporary.unlink()


def _current_user_sid() -> str:
    result = subprocess.run(
        [_system_executable("whoami.exe"), "/user", "/fo", "csv", "/nh"],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )
    row = next(csv.reader([result.stdout.strip()]))
    if len(row) < 2 or not row[1].startswith("S-1-"):
        raise RuntimeError("could not determine the current Windows user SID")
    return row[1]


def restrict_acl(path: Path, *, directory: bool = False) -> None:
    user_sid = _current_user_sid()
    suffix = "(OI)(CI)F" if directory else "F"
    grants = [
        f"*{user_sid}:{suffix}",
        f"*S-1-5-18:{suffix}",
        f"*S-1-5-32-544:{suffix}",
    ]
    result = subprocess.run(
        [
            _system_executable("icacls.exe"),
            str(path),
            "/inheritance:r",
            "/grant:r",
            *grants,
        ],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )
    if result.returncode != 0:
        raise RuntimeError(f"could not restrict ACL on {path}: {result.stderr.strip()}")
    verify_restricted_acl(path, user_sid)


def _dacl_sddl(path: Path) -> str:
    advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p
    security_descriptor = ctypes.c_void_p()
    dacl = ctypes.c_void_p()
    get_named = advapi32.GetNamedSecurityInfoW
    get_named.argtypes = [
        wintypes.LPWSTR,
        wintypes.DWORD,
        wintypes.DWORD,
        ctypes.c_void_p,
        ctypes.c_void_p,
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.c_void_p,
        ctypes.POINTER(ctypes.c_void_p),
    ]
    get_named.restype = wintypes.DWORD
    error = get_named(
        str(path),
        1,  # SE_FILE_OBJECT
        0x4,  # DACL_SECURITY_INFORMATION
        None,
        None,
        ctypes.byref(dacl),
        None,
        ctypes.byref(security_descriptor),
    )
    if error:
        raise ctypes.WinError(error)
    text_pointer = wintypes.LPWSTR()
    text_length = wintypes.ULONG()
    convert = advapi32.ConvertSecurityDescriptorToStringSecurityDescriptorW
    convert.argtypes = [
        ctypes.c_void_p,
        wintypes.DWORD,
        wintypes.DWORD,
        ctypes.POINTER(wintypes.LPWSTR),
        ctypes.POINTER(wintypes.ULONG),
    ]
    convert.restype = wintypes.BOOL
    try:
        if not convert(
            security_descriptor,
            1,
            0x4,
            ctypes.byref(text_pointer),
            ctypes.byref(text_length),
        ):
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            return text_pointer.value
        finally:
            kernel32.LocalFree(text_pointer)
    finally:
        kernel32.LocalFree(security_descriptor)


def verify_restricted_acl(path: Path, user_sid: str | None = None) -> None:
    current_sid = user_sid or _current_user_sid()
    sddl = _dacl_sddl(path)
    prefix = sddl.split("(", 1)[0]
    if not prefix.startswith("D:") or "P" not in prefix[2:]:
        raise RuntimeError(f"ACL inheritance is not protected on {path}: {sddl}")
    allowed_sids = {current_sid, "SY", "BA", "S-1-5-18", "S-1-5-32-544"}
    entries = re.findall(r"\(([^)]*)\)", sddl)
    if not entries:
        raise RuntimeError(f"ACL has no access entries on {path}: {sddl}")
    for entry in entries:
        fields = entry.split(";")
        if len(fields) < 6 or fields[0] != "A" or fields[2] != "FA":
            raise RuntimeError(f"ACL has a non-full-control allow entry on {path}: {entry}")
        if fields[5] not in allowed_sids:
            raise RuntimeError(f"ACL grants an unexpected principal on {path}: {entry}")


def secure_directory(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)
    restrict_acl(path, directory=True)


def create_secret(path: Path) -> str:
    if path.exists():
        raise FileExistsError(f"secret already exists: {path}")
    password = secrets.token_urlsafe(48)
    envelope = {
        "schema_version": SCHEMA_VERSION,
        "protection": "Windows DPAPI CurrentUser",
        "ciphertext_base64": base64.b64encode(
            protect(password.encode("ascii"))
        ).decode("ascii"),
    }
    _atomic_create(
        path,
        (json.dumps(envelope, indent=2, sort_keys=True) + "\n").encode("utf-8"),
    )
    return password


def load_secret(path: Path) -> str:
    envelope = json.loads(path.read_text(encoding="utf-8"))
    if envelope.get("schema_version") != SCHEMA_VERSION:
        raise ValueError(f"unsupported secret envelope schema: {path}")
    if envelope.get("protection") != "Windows DPAPI CurrentUser":
        raise ValueError(f"unexpected secret protection scope: {path}")
    ciphertext = base64.b64decode(envelope["ciphertext_base64"], validate=True)
    password = unprotect(ciphertext).decode("ascii")
    if len(password) < 40:
        raise ValueError("decrypted repository password is unexpectedly short")
    return password


def write_recovery_key(path: Path, repository: Path, password: str) -> None:
    content = (
        "RESTICBACKUPER RECOVERY KEY\r\n"
        "===================================\r\n\r\n"
        f"Repository: {repository}\r\n"
        f"Password: {password}\r\n\r\n"
        "Store a printed or password-manager copy away from this computer.\r\n"
        "Anyone with this password and the repository can read the backup.\r\n"
    ).encode("utf-8")
    _atomic_create(path, content)


def _parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)
    reveal = subparsers.add_parser("reveal")
    reveal.add_argument("--secret-file", type=Path, required=True)
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = _parse_args(argv)
    if args.command == "reveal":
        password = load_secret(args.secret_file)
        sys.stdout.buffer.write(password.encode("ascii") + b"\n")
        sys.stdout.buffer.flush()
        return 0
    raise AssertionError("unreachable")


if __name__ == "__main__":
    raise SystemExit(main())
