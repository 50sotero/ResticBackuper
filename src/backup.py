"""Run and verify an encrypted incremental Restic snapshot."""

from __future__ import annotations

import argparse
from datetime import datetime
import json
import ntpath
import os
from pathlib import Path
import secrets
import shutil
import sys
import time
import traceback
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))

from restic_common import (
    DEFAULT_CONFIG,
    RunLockUnavailable,
    SourceUpdateJournalPresent,
    atomic_write_json,
    ensure_free_space,
    load_config_under_lock,
    restic_base,
    run_capture,
    sanitized_command,
    sha256_file,
    stream_command,
    utc_now,
    validate_repository_volume,
    windows_snapshot_path,
)


PROGRESS_PERSIST_INTERVAL_SECONDS = 0.5


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--tag", help="snapshot tag; defaults to scheduled_tag")
    parser.add_argument("--scheduled", action="store_true")
    return parser.parse_args(argv)


def normalized_paths(paths: list[str]) -> list[str]:
    return sorted(ntpath.normcase(ntpath.normpath(path)) for path in paths)


def snapshot_matches(
    config: dict[str, Any], tag: str, snapshot: dict[str, Any]
) -> bool:
    return (
        snapshot.get("hostname", "").casefold() == config["hostname"].casefold()
        and tag in (snapshot.get("tags") or [])
        and normalized_paths(snapshot.get("paths") or [])
        == normalized_paths(config["sources"])
    )


def select_snapshot(
    config: dict[str, Any], tag: str, snapshots: list[dict[str, Any]]
) -> dict[str, Any]:
    matches = [
        snapshot
        for snapshot in snapshots
        if snapshot_matches(config, tag, snapshot)
    ]
    if not matches:
        raise RuntimeError(
            "no snapshot exactly matches the configured host, tag, and source roots"
        )
    return max(matches, key=lambda item: item.get("time", ""))


def parse_json_lines(line: str) -> dict[str, Any] | None:
    try:
        value = json.loads(line)
    except (json.JSONDecodeError, TypeError):
        return None
    return value if isinstance(value, dict) else None


def safe_remove_restore_test(target: Path, base: Path) -> None:
    target_resolved = target.resolve(strict=True)
    base_resolved = base.resolve(strict=True)
    if target_resolved.parent != base_resolved:
        raise RuntimeError(f"unsafe restore-test cleanup target: {target_resolved}")
    shutil.rmtree(target_resolved)


def verify_canary(
    config: dict[str, Any], snapshot_id: str, run_id: str, log_handle
) -> dict[str, Any]:
    canary = Path(config["canary_file"])
    expected_hash = sha256_file(canary)
    restore_base = Path(config["state_directory"]) / "restore-tests"
    restore_base.mkdir(parents=True, exist_ok=True)
    target = restore_base / run_id
    if target.exists():
        raise FileExistsError(f"restore-test target already exists: {target}")
    target.mkdir()

    snapshot_path = windows_snapshot_path(canary)
    # Restic's snapshotID:subfolder selector accepts directories, not files.
    # Restoring the canary's small project directory avoids recreating the
    # source volume's parent ACL hierarchy in the temporary verification tree.
    snapshot_selector = f"{snapshot_id}:{windows_snapshot_path(canary.parent)}"
    command = restic_base(config) + [
        "restore",
        snapshot_selector,
        "--target",
        str(target),
        "--overwrite",
        "never",
        "--verify",
    ]
    log_handle.write(
        json.dumps(
            {"wrapper_event": "canary_restore", "command": sanitized_command(command)}
        )
        + "\n"
    )
    log_handle.flush()
    return_code = stream_command(command, log_handle)
    if return_code != 0:
        raise RuntimeError(f"canary restore failed with exit {return_code}")

    candidates = list(target.rglob(canary.name))
    if len(candidates) != 1:
        raise RuntimeError(
            f"canary restore produced {len(candidates)} matching files, expected one"
        )
    restored = candidates[0]
    actual_hash = sha256_file(restored)
    if actual_hash != expected_hash or restored.stat().st_size != canary.stat().st_size:
        raise RuntimeError("restored canary content does not match its source")

    result = {
        "snapshot_path": snapshot_path,
        "bytes": restored.stat().st_size,
        "sha256": actual_hash,
        "verified": True,
    }
    safe_remove_restore_test(target, restore_base)
    return result


def _run_locked(args: argparse.Namespace, config: dict[str, Any]) -> int:
    tag = args.tag or config["scheduled_tag"]
    state_directory = Path(config["state_directory"])
    logs_directory = state_directory / "logs"
    logs_directory.mkdir(parents=True, exist_ok=True)
    status_path = state_directory / "status.json"
    last_success_path = state_directory / "last-success.json"
    prior_success_exists = last_success_path.is_file()

    run_id = datetime.now().strftime("%Y%m%dT%H%M%S") + "-" + secrets.token_hex(4)
    log_path = logs_directory / f"backup-{run_id}.jsonl.log"
    status: dict[str, Any] = {
        "schema_version": 1,
        "run_id": run_id,
        "state": "starting",
        "started_utc": utc_now(),
        "finished_utc": None,
        "wrapper_pid": os.getpid(),
        "scheduled": bool(args.scheduled),
        "repository": config["repository"],
        "repository_volume_serial": config["repository_volume_serial"],
        "hostname": config["hostname"],
        "tag": tag,
        "sources": config["sources"],
        "exclude_file": config["exclude_file"],
        "exclude_file_sha256": sha256_file(Path(config["exclude_file"])),
        "restic_executable": config["restic_executable"],
        "restic_executable_sha256": sha256_file(Path(config["restic_executable"])),
        "canary_source_sha256": sha256_file(Path(config["canary_file"])),
        "log_file": str(log_path),
        "exit_code": None,
        "snapshot_id": None,
        "progress": None,
        "summary": None,
        "errors": [],
        "verification": {},
    }
    try:
        atomic_write_json(status_path, status)
        with log_path.open(
            "a", encoding="utf-8", newline="\n", buffering=1
        ) as log_handle:
            validate_repository_volume(config)
            status["free_bytes_before"] = ensure_free_space(config)

            prior_snapshots_result = run_capture(
                restic_base(config, json_output=True) + ["snapshots"]
            )
            if prior_snapshots_result.returncode != 0:
                raise RuntimeError(
                    "could not query snapshots before backup: "
                    + prior_snapshots_result.stderr.strip()
                )
            prior_snapshots = json.loads(prior_snapshots_result.stdout)
            prior_matching_snapshot_ids = {
                snapshot["id"]
                for snapshot in prior_snapshots
                if snapshot_matches(config, tag, snapshot)
            }
            status["prior_matching_snapshot_count"] = len(
                prior_matching_snapshot_ids
            )

            canary = Path(config["canary_file"])
            if not any(
                ntpath.commonpath(
                    [
                        ntpath.normcase(ntpath.abspath(str(canary))),
                        ntpath.normcase(ntpath.abspath(source)),
                    ]
                )
                == ntpath.normcase(ntpath.abspath(source))
                for source in config["sources"]
                if ntpath.splitdrive(str(canary))[0].casefold()
                == ntpath.splitdrive(source)[0].casefold()
            ):
                raise RuntimeError("the restore canary is not inside a configured source")

            command = restic_base(config, json_output=True) + [
                "backup",
                *config["sources"],
                "--iexclude-file",
                config["exclude_file"],
                "--exclude-caches",
                "--skip-if-unchanged",
                "--no-scan",
                "--read-concurrency",
                str(int(config.get("read_concurrency", 2))),
                "--group-by",
                "host,paths,tags",
                "--host",
                config["hostname"],
                "--tag",
                tag,
            ]
            if config.get("use_vss"):
                command.append("--use-fs-snapshot")

            status["state"] = "backing_up"
            status["command"] = sanitized_command(command)
            atomic_write_json(status_path, status)
            log_handle.write(
                json.dumps(
                    {
                        "wrapper_event": "backup_started",
                        "run_id": run_id,
                        "utc": utc_now(),
                        "command": sanitized_command(command),
                    }
                )
                + "\n"
            )

            last_progress_persisted = float("-inf")

            def on_backup_line(line: str) -> None:
                nonlocal last_progress_persisted
                event = parse_json_lines(line)
                if event is None:
                    return
                message_type = event.get("message_type")
                if message_type == "status":
                    status["progress"] = event
                    now = time.monotonic()
                    if (
                        now - last_progress_persisted
                        >= PROGRESS_PERSIST_INTERVAL_SECONDS
                    ):
                        atomic_write_json(status_path, status)
                        last_progress_persisted = now
                elif message_type == "summary":
                    status["summary"] = event
                elif message_type == "error":
                    if len(status["errors"]) < 100:
                        status["errors"].append(event)

            backup_return_code = stream_command(command, log_handle, on_backup_line)
            status["backup_exit_code"] = backup_return_code
            if backup_return_code == 3:
                status["state"] = "partial"
                status["exit_code"] = 3
                raise RuntimeError(
                    "Restic created an incomplete snapshot because some source data was unreadable"
                )
            if backup_return_code != 0:
                status["exit_code"] = backup_return_code
                raise RuntimeError(f"Restic backup failed with exit {backup_return_code}")

            status["state"] = "verifying_snapshot"
            atomic_write_json(status_path, status)
            snapshots_result = run_capture(restic_base(config, json_output=True) + ["snapshots"])
            log_handle.write(snapshots_result.stderr)
            if snapshots_result.returncode != 0:
                raise RuntimeError(
                    f"could not query snapshots: {snapshots_result.stderr.strip()}"
                )
            snapshots = json.loads(snapshots_result.stdout)
            summary = status.get("summary")
            if not isinstance(summary, dict) or summary.get("message_type") != "summary":
                raise RuntimeError(
                    "Restic exited successfully but emitted no parseable JSON summary"
                )
            summary_snapshot_id = summary.get("snapshot_id")
            if summary_snapshot_id:
                bound = [
                    item
                    for item in snapshots
                    if item.get("id") == summary_snapshot_id
                    and snapshot_matches(config, tag, item)
                ]
                if len(bound) != 1:
                    raise RuntimeError(
                        "backup summary snapshot is missing or does not match host/tag/paths"
                    )
                snapshot = bound[0]
            else:
                if not prior_matching_snapshot_ids:
                    raise RuntimeError(
                        "Restic reported no new snapshot and no matching parent exists"
                    )
                prior_snapshot = select_snapshot(config, tag, prior_snapshots)
                bound = [
                    item
                    for item in snapshots
                    if item.get("id") == prior_snapshot.get("id")
                    and snapshot_matches(config, tag, item)
                ]
                if len(bound) != 1:
                    raise RuntimeError(
                        "unchanged backup parent is no longer present after the run"
                    )
                snapshot = bound[0]
            snapshot_id = snapshot["id"]
            status["snapshot_id"] = snapshot_id
            status["snapshot"] = snapshot

            if config.get("structural_check_after_backup", True):
                status["state"] = "checking_repository"
                atomic_write_json(status_path, status)
                check_command = restic_base(config) + ["check"]
                log_handle.write(
                    json.dumps(
                        {
                            "wrapper_event": "structural_check",
                            "command": sanitized_command(check_command),
                        }
                    )
                    + "\n"
                )
                if stream_command(check_command, log_handle) != 0:
                    raise RuntimeError("repository structural check failed")
                status["verification"]["repository_structure"] = True

            status["state"] = "restoring_canary"
            atomic_write_json(status_path, status)
            status["verification"]["canary"] = verify_canary(
                config, snapshot_id, run_id, log_handle
            )

            weekday = datetime.now().strftime("%A")
            subset_parts = int(config.get("read_data_subset_parts", 0))
            if (
                args.scheduled
                and prior_success_exists
                and subset_parts > 0
                and weekday.casefold()
                == str(config.get("read_data_subset_weekday", "")).casefold()
            ):
                subset = datetime.now().timetuple().tm_yday % subset_parts + 1
                subset_command = restic_base(config) + [
                    "check",
                    f"--read-data-subset={subset}/{subset_parts}",
                ]
                status["state"] = "checking_data_subset"
                status["verification"]["data_subset"] = f"{subset}/{subset_parts}"
                atomic_write_json(status_path, status)
                if stream_command(subset_command, log_handle) != 0:
                    raise RuntimeError("repository data-subset check failed")

            status["state"] = (
                "success_unchanged"
                if not summary_snapshot_id
                or snapshot_id in prior_matching_snapshot_ids
                else "success"
            )
            status["exit_code"] = 0
            status["finished_utc"] = utc_now()
            status["free_bytes_after"] = shutil.disk_usage(
                Path(config["repository"]).anchor
            ).free
            atomic_write_json(status_path, status)
            atomic_write_json(last_success_path, status)
            log_handle.write(
                json.dumps(
                    {
                        "wrapper_event": "backup_verified",
                        "run_id": run_id,
                        "snapshot_id": snapshot_id,
                        "utc": status["finished_utc"],
                    }
                )
                + "\n"
            )
            return 0
    except BaseException as error:
        if status.get("state") != "partial":
            status["state"] = "failed"
        if status.get("exit_code") is None:
            status["exit_code"] = 130 if isinstance(error, KeyboardInterrupt) else 1
        status["finished_utc"] = utc_now()
        status["failure"] = str(error)
        status_write_error = None
        try:
            atomic_write_json(status_path, status)
        except Exception as telemetry_error:
            status_write_error = str(telemetry_error)
        try:
            with log_path.open("a", encoding="utf-8", newline="\n") as log_handle:
                failure_event = {
                    "wrapper_event": "failure",
                    "utc": status["finished_utc"],
                    "error": str(error),
                    "traceback": traceback.format_exc(),
                }
                if status_write_error is not None:
                    failure_event["status_write_error"] = status_write_error
                log_handle.write(json.dumps(failure_event) + "\n")
                log_handle.flush()
                os.fsync(log_handle.fileno())
        except OSError:
            pass
        try:
            print(f"BACKUP FAILED: {error}", file=sys.stderr)
        except (AttributeError, OSError):
            pass
        return int(status["exit_code"] or 1)


def run(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        config, run_lock = load_config_under_lock(
            args.config, require_repository=True
        )
    except (RunLockUnavailable, SourceUpdateJournalPresent) as error:
        print(f"BACKUP NOT STARTED: {error}", file=sys.stderr)
        return 75
    try:
        return _run_locked(args, config)
    finally:
        run_lock.__exit__(None, None, None)


if __name__ == "__main__":
    raise SystemExit(run())
