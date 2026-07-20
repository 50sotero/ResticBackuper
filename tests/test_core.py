from __future__ import annotations

import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock


PROJECT = Path(__file__).resolve().parents[1]
SOURCE = PROJECT / "src"
sys.path.insert(0, str(SOURCE))

import backup
import restic_common
import restore
import secret_store


class CoreSafetyTests(unittest.TestCase):
    def _write_config(
        self,
        root: Path,
        *,
        exclude_text: str = "**/__pycache__\n",
        repository: Path | None = None,
        sources: list[Path] | None = None,
    ) -> Path:
        source = root / "source"
        source.mkdir(exist_ok=True)
        repository = repository or (root / "repository")
        restic = root / "restic.exe"
        restic.write_bytes(b"test fixture; not an executable")
        excludes = root / "excludes.txt"
        excludes.write_text(exclude_text, encoding="utf-8")
        canary = root / "canary.txt"
        canary.write_text("ResticBackuper test canary\n", encoding="utf-8")
        config_path = root / "backup-config.test.json"
        config_path.write_text(
            json.dumps(
                {
                    "schema_version": 1,
                    "repository": str(repository),
                    "repository_volume_serial": "00000000",
                    "restic_executable": str(restic),
                    "recovery_tools_directory": str(root / "recovery-tools"),
                    "python_executable": sys.executable,
                    "state_directory": str(root / "state"),
                    "secret_file": str(root / "state" / "password.test.json"),
                    "recovery_key_file": str(root / "recovery.test.txt"),
                    "exclude_file": str(excludes),
                    "canary_file": str(canary),
                    "hostname": "RESTICBACKUPER-TEST",
                    "scheduled_tag": "scheduled-test",
                    "sources": [str(item) for item in (sources or [source])],
                }
            ),
            encoding="utf-8",
        )
        return config_path

    def test_example_exclusions_pass_protected_tree_guard(self) -> None:
        patterns = [
            line.strip()
            for line in (SOURCE / "excludes.txt").read_text(encoding="utf-8").splitlines()
            if line.strip() and not line.lstrip().startswith("#")
        ]
        self.assertTrue(patterns)
        for pattern in patterns:
            with self.subTest(pattern=pattern):
                self.assertFalse(
                    restic_common._exclude_can_touch_protected_tree(pattern, ".git")
                )
                self.assertFalse(
                    restic_common._exclude_can_touch_protected_tree(
                        pattern, ".codex-worktrees"
                    )
                )

    def test_exclude_guard_rejects_patterns_that_can_hide_git_state(self) -> None:
        for pattern in ("**/.g*", "**/.*", "**/.g*/objects", "**"):
            with self.subTest(pattern=pattern):
                self.assertTrue(
                    restic_common._exclude_can_touch_protected_tree(pattern, ".git")
                )
        self.assertFalse(
            restic_common._exclude_can_touch_protected_tree(
                "**/.terraform/providers", ".git"
            )
        )
        with self.assertRaises(ValueError):
            restic_common._exclude_can_touch_protected_tree("!**/.git", ".git")

    def test_load_config_accepts_safe_isolated_configuration(self) -> None:
        with tempfile.TemporaryDirectory(prefix="resticbackuper-config-") as root_text:
            root = Path(root_text)
            config_path = self._write_config(root)

            config = restic_common.load_config(
                config_path, require_repository=False
            )

            self.assertEqual([str((root / "source").resolve())], config["sources"])
            self.assertEqual(
                str((root / "repository").resolve()), config["repository"]
            )

    def test_load_config_rejects_repository_source_overlap(self) -> None:
        with tempfile.TemporaryDirectory(prefix="resticbackuper-overlap-") as root_text:
            root = Path(root_text)
            source = root / "source"
            source.mkdir()
            config_path = self._write_config(
                root,
                repository=source / "repository",
                sources=[source],
            )

            with self.assertRaisesRegex(ValueError, "repository and source overlap"):
                restic_common.load_config(config_path, require_repository=False)

    def test_load_config_rejects_unsafe_exclusion(self) -> None:
        with tempfile.TemporaryDirectory(prefix="resticbackuper-exclude-") as root_text:
            root = Path(root_text)
            config_path = self._write_config(root, exclude_text="**/.g*\n")

            with self.assertRaisesRegex(ValueError, "protected repository state"):
                restic_common.load_config(config_path, require_repository=False)

    def test_atomic_json_write_retries_transient_replace_failure(self) -> None:
        with tempfile.TemporaryDirectory(prefix="resticbackuper-atomic-") as root_text:
            target = Path(root_text) / "status.json"
            real_replace = os.replace
            calls = 0

            def flaky_replace(source: Path, destination: Path) -> None:
                nonlocal calls
                calls += 1
                if calls < 3:
                    raise PermissionError(13, "simulated sharing violation")
                real_replace(source, destination)

            with (
                mock.patch("restic_common.os.replace", side_effect=flaky_replace),
                mock.patch("restic_common.time.sleep") as sleep,
            ):
                restic_common.atomic_write_json(target, {"state": "backing_up"})

            self.assertEqual(3, calls)
            self.assertEqual(2, sleep.call_count)
            self.assertEqual(
                {"state": "backing_up"},
                json.loads(target.read_text(encoding="utf-8")),
            )
            self.assertEqual([], list(target.parent.glob(".*.tmp")))

    def test_snapshot_selection_requires_exact_identity_and_uses_latest(self) -> None:
        config = {
            "hostname": "Backup-Host",
            "sources": [r"C:\BackupSource\Documents", r"D:\Projects"],
        }
        older = {
            "id": "older",
            "hostname": "BACKUP-HOST",
            "tags": ["scheduled"],
            "paths": [r"d:\projects", r"c:\backupsource\documents"],
            "time": "2026-01-01T01:00:00Z",
        }
        newer = {**older, "id": "newer", "time": "2026-01-02T01:00:00Z"}
        unrelated = {**newer, "id": "wrong-host", "hostname": "OTHER-HOST"}

        selected = backup.select_snapshot(
            config, "scheduled", [unrelated, older, newer]
        )

        self.assertEqual("newer", selected["id"])
        with self.assertRaisesRegex(RuntimeError, "no snapshot exactly matches"):
            backup.select_snapshot(config, "manual", [older, newer])

    def test_json_line_parser_only_accepts_objects(self) -> None:
        self.assertEqual({"message_type": "status"}, backup.parse_json_lines(
            '{"message_type":"status"}'
        ))
        for value in ("not json", "[]", '"text"', "null"):
            with self.subTest(value=value):
                self.assertIsNone(backup.parse_json_lines(value))

    def test_restore_cleanup_cannot_escape_its_dedicated_parent(self) -> None:
        with tempfile.TemporaryDirectory(prefix="resticbackuper-cleanup-") as root_text:
            root = Path(root_text)
            base = root / "restore-tests"
            target = base / "one-run"
            outside = root / "keep-me"
            target.mkdir(parents=True)
            outside.mkdir()

            backup.safe_remove_restore_test(target, base)
            self.assertFalse(target.exists())

            with self.assertRaisesRegex(RuntimeError, "unsafe restore-test cleanup"):
                backup.safe_remove_restore_test(outside, base)

    def test_restore_source_selector_uses_exact_configured_root(self) -> None:
        configured = [r"Q:\Profiles\Example\Documents", r"D:\Photos"]
        selected = restore.select_configured_source(
            Path(r"q:\profiles\example\documents"), configured
        )
        self.assertEqual(str(selected), configured[0])
        self.assertEqual(
            restore.windows_snapshot_path(selected),
            "/Q/Profiles/Example/Documents",
        )

    def test_restore_source_selector_rejects_unrecorded_child(self) -> None:
        with self.assertRaisesRegex(ValueError, "exactly match"):
            restore.select_configured_source(
                Path(r"Q:\Profiles\Example\Documents\Subset"),
                [r"Q:\Profiles\Example\Documents"],
            )
            self.assertTrue(outside.is_dir())

    def test_sanitized_command_redacts_password_helper(self) -> None:
        command = [
            "restic.exe",
            "--password-command",
            "python secret_store.py reveal --secret-file secret.json",
            "snapshots",
        ]
        sanitized = restic_common.sanitized_command(command)
        self.assertEqual("<DPAPI password command>", sanitized[2])
        self.assertIn("secret_store.py", command[2])
        self.assertNotIn("secret_store.py", sanitized[2])

    def test_recovery_password_parser_requires_one_strong_value(self) -> None:
        with tempfile.TemporaryDirectory(prefix="resticbackuper-recovery-") as root_text:
            key_file = Path(root_text) / "recovery.test.txt"
            password = "a" * 48
            key_file.write_text(
                f"RESTICBACKUPER TEST RECOVERY KEY\nPassword: {password}\n",
                encoding="utf-8",
            )
            self.assertEqual(password, restore.recovery_password(key_file))

            key_file.write_text("Password: too-short\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                restore.recovery_password(key_file)

    @unittest.skipUnless(os.name == "nt", "DPAPI is available only on Windows")
    def test_dpapi_round_trip_stays_in_current_user_context(self) -> None:
        value = b"ephemeral-unit-test-value"
        self.assertEqual(value, secret_store.unprotect(secret_store.protect(value)))

    def test_stream_command_stops_child_when_progress_callback_fails(self) -> None:
        class FakeJob:
            def __enter__(self):
                return self

            def assign(self, _process) -> None:
                return None

            def __exit__(self, _exc_type, _exc_value, _traceback) -> None:
                return None

        class FakeProcess:
            def __init__(self) -> None:
                self.stdout = io.StringIO("status line\n")
                self.terminated = False
                self.wait_calls: list[int | None] = []

            def poll(self):
                return 1 if self.terminated else None

            def terminate(self) -> None:
                self.terminated = True

            def kill(self) -> None:
                raise AssertionError("kill should not be needed")

            def wait(self, timeout=None) -> int:
                self.wait_calls.append(timeout)
                return 1

        process = FakeProcess()
        with (
            mock.patch("restic_common.subprocess.Popen", return_value=process),
            mock.patch("restic_common._WindowsKillOnCloseJob", FakeJob),
        ):
            with self.assertRaisesRegex(PermissionError, "simulated telemetry failure"):
                restic_common.stream_command(
                    ["restic"],
                    io.StringIO(),
                    lambda _line: (_ for _ in ()).throw(
                        PermissionError("simulated telemetry failure")
                    ),
                )

        self.assertTrue(process.terminated)
        self.assertEqual(
            [restic_common.PROCESS_STOP_TIMEOUT_SECONDS], process.wait_calls
        )


if __name__ == "__main__":
    unittest.main()
