import json
import os
from pathlib import Path
import subprocess
import tempfile
import textwrap
import unittest


PROJECT = Path(__file__).resolve().parents[1]
DASHBOARD = PROJECT / "src" / "dashboard"
NAMESPACE = "ResticBackuper.Dashboard"
TELEMETRY_SOURCE = DASHBOARD / "Telemetry.cs"
WINDOW_SOURCE = DASHBOARD / "DashboardWindow.cs"
DIAGNOSTIC_SOURCE = DASHBOARD / "DiagnosticExporter.cs"
PLAN_ID = "d1183ad9-9253-4bd7-90a7-8edad470658c"
REPOSITORY_ID = "8" * 64
SNAPSHOT_ID = "4" * 64
REPOSITORY_PATH = r"G:\My Drive\ResticBackups\Personal"


HARNESS_SOURCE = """
namespace {namespace}
{{
    using System;
    using System.Collections.Generic;
    using System.Web.Script.Serialization;

    internal static class OffsiteStatusHarness
    {{
        public static int Main(string[] args)
        {{
            TelemetryReader reader = new TelemetryReader(args[0], false, args[1]);
            TelemetrySnapshot snapshot = reader.Load();
            OffsiteStatusView status = snapshot.OffsiteStatus;
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["kind"] = status.Kind.ToString();
            result["label"] = status.StatusLabel;
            result["detail"] = status.StatusDetail;
            result["plan_id"] = status.PlanId;
            result["config_generation"] = status.ConfigGeneration;
            result["repository_id"] = status.RepositoryId;
            result["repository_path"] = status.RepositoryPath;
            result["snapshot_id"] = status.SnapshotId;
            result["snapshot_short"] = status.SnapshotShort;
            result["inventory_fingerprint"] = status.InventoryFingerprintSha256;
            result["files"] = status.FileCount;
            result["bytes"] = status.ByteCount;
            result["provider_confirmed"] = status.ProviderUploadConfirmed;
            result["restore_verified"] = status.RestoreVerified;
            result["has_updated"] = status.LastUpdatedLocal.HasValue;
            Console.WriteLine(new JavaScriptSerializer().Serialize(result));
            return 0;
        }}
    }}
}}
"""


class DashboardOffsiteStatusTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        framework = (
            Path(os.environ.get("SystemRoot", r"C:\Windows"))
            / "Microsoft.NET"
            / "Framework64"
            / "v4.0.30319"
        )
        compiler = framework / "csc.exe"
        if not compiler.is_file():
            raise unittest.SkipTest("The .NET Framework C# compiler is unavailable.")

        # Keep the compiled harness beneath the reviewed source tree. Some
        # Windows Application Control policies reject newly compiled binaries
        # from the per-user temporary directory even though the same compiler
        # output is allowed from the repository workspace.
        cls.temp_directory = tempfile.TemporaryDirectory(
            prefix=".offsite-status-harness-",
            dir=PROJECT,
        )
        temp = Path(cls.temp_directory.name)
        cls.executable = temp / "OffsiteStatusHarness.exe"
        harness = temp / "OffsiteStatusHarness.cs"
        harness.write_text(
            textwrap.dedent(HARNESS_SOURCE.format(namespace=NAMESPACE)),
            encoding="utf-8",
        )
        compile_result = subprocess.run(
            [
                str(compiler),
                "/nologo",
                "/target:exe",
                "/warn:4",
                f"/out:{cls.executable}",
                f"/main:{NAMESPACE}.OffsiteStatusHarness",
                f"/reference:{framework / 'System.dll'}",
                f"/reference:{framework / 'System.Core.dll'}",
                f"/reference:{framework / 'System.Web.Extensions.dll'}",
                str(TELEMETRY_SOURCE),
                str(harness),
            ],
            capture_output=True,
            text=True,
            check=False,
        )
        if compile_result.returncode != 0:
            raise AssertionError(
                "Off-site status harness compilation failed:\n"
                + compile_result.stdout
                + compile_result.stderr
            )

    @classmethod
    def tearDownClass(cls) -> None:
        if hasattr(cls, "temp_directory"):
            cls.temp_directory.cleanup()

    @staticmethod
    def backup_evidence(**overrides) -> dict:
        result = {
            "schema_version": 1,
            "state": "success",
            "verification_complete": True,
            "started_utc": "2026-07-28T23:45:00Z",
            "finished_utc": "2026-07-29T00:05:00Z",
            "run_id": "20260729T014500-a1b2c3d4",
            "plan_id": PLAN_ID,
            "config_generation": 2,
            "repository": REPOSITORY_PATH,
            "repository_storage_mode": "google_drivefs_stream",
            "repository_id": REPOSITORY_ID,
            "snapshot_id": SNAPSHOT_ID,
        }
        result.update(overrides)
        return result

    @staticmethod
    def direct_proof(**overrides) -> dict:
        result = {
            "schema_version": 2,
            "proof_kind": "direct_my_drive_cloud_repository_verification",
            "run_id": "20260729T001500000Z-a1b2c3d4",
            "state": "verified",
            "verified_utc": "2026-07-29T00:15:00Z",
            "verification_mode": "google_drive_api_readonly_rclone_backend",
            "verification_phase": "post_activation",
            "plan_id": PLAN_ID,
            "config_generation": 2,
            "repository_storage_mode": "google_drivefs_stream",
            "local_repository": REPOSITORY_PATH,
            "my_drive_root": r"G:\My Drive",
            "cloud_root_folder_id": "root",
            "cloud_repository_path": "ResticBackups/Personal",
            "repository_identity_verified": True,
            "repository_id": REPOSITORY_ID,
            "local_repository_id": REPOSITORY_ID,
            "cloud_repository_id": REPOSITORY_ID,
            "repository_version": 2,
            "exact_file_inventory_verified": True,
            "path_case_size_md5_sha256_verified": True,
            "files": 5109,
            "bytes": 89957824603,
            "missing_files": 0,
            "extra_files": 0,
            "mismatched_files": 0,
            "inventory_fingerprint_sha256": "1" * 64,
            "cloud_inventory_document_sha256": "2" * 64,
            "cloud_objects_with_ids": 5109,
            "snapshot_id": SNAPSHOT_ID,
            "canary_bytes": 126,
            "canary_sha256": "3" * 64,
            "direct_cloud_restore_verified": True,
            "provider_upload_state": "fully_synced",
            "restore_verification_state": "verified",
            "backup_config_sha256": "5" * 64,
            "immutable_proof_sha256": "6" * 64,
            "cloud_verification_assets_manifest_sha256": "7" * 64,
            "native_capture_mode": "binary_stream_copy",
        }
        result.update(overrides)
        return result

    @staticmethod
    def legacy_mirror_status() -> dict:
        return {
            "schema_version": 1,
            "state": "success",
            "snapshot_id": SNAPSHOT_ID,
            "verified_files": 5109,
            "verified_bytes": 89957824603,
            "provider_upload_state": "synced",
            "restore_verified": True,
        }

    def invoke(
        self,
        proof=None,
        proof_raw: str = None,
        protected=None,
        last_success=None,
        current=None,
    ) -> dict:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            state = root / "state"
            state.mkdir()
            proof_path = root / "latest-verification.json"
            if proof_raw is not None:
                proof_path.write_text(proof_raw, encoding="utf-8")
            elif proof is not None:
                proof_path.write_text(json.dumps(proof), encoding="utf-8")
            if protected is not None:
                (state / "google-drive-sync-status.json").write_text(
                    json.dumps(protected),
                    encoding="utf-8",
                )
            if last_success is None:
                last_success = self.backup_evidence()
            if last_success is not False:
                (state / "last-success.json").write_text(
                    json.dumps(last_success),
                    encoding="utf-8",
                )
            if current is None:
                current = dict(last_success) if isinstance(last_success, dict) else None
            if current is not False and current is not None:
                (state / "status.json").write_text(
                    json.dumps(current),
                    encoding="utf-8",
                )
            run = subprocess.run(
                [str(self.executable), str(state), str(proof_path)],
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(0, run.returncode, run.stdout + run.stderr)
            return json.loads(run.stdout)

    def test_complete_direct_cloud_proof_is_bound_and_displayed(self) -> None:
        result = self.invoke(proof=self.direct_proof())
        self.assertEqual("RestoreVerified", result["kind"])
        self.assertEqual("Google Drive repository verified", result["label"])
        self.assertTrue(result["provider_confirmed"])
        self.assertTrue(result["restore_verified"])
        self.assertEqual(PLAN_ID, result["plan_id"])
        self.assertEqual(2, result["config_generation"])
        self.assertEqual(REPOSITORY_ID, result["repository_id"])
        self.assertEqual(REPOSITORY_PATH, result["repository_path"])
        self.assertEqual(SNAPSHOT_ID, result["snapshot_id"])
        self.assertEqual(SNAPSHOT_ID[:8], result["snapshot_short"])
        self.assertEqual("1" * 64, result["inventory_fingerprint"])
        self.assertEqual(5109, result["files"])
        self.assertEqual(89957824603, result["bytes"])
        self.assertTrue(result["has_updated"])

    def test_legacy_mirror_evidence_is_explicitly_rejected(self) -> None:
        result = self.invoke(protected=self.legacy_mirror_status())
        self.assertEqual("StatusUnavailable", result["kind"])
        self.assertIn("Legacy local-mirror evidence is retired", result["detail"])
        self.assertFalse(result["provider_confirmed"])

    def test_plan_generation_repository_snapshot_and_phase_mismatch_fail(self) -> None:
        mutations = {
            "plan": {"plan_id": "f2d73a58-895a-4bc9-b49a-92ec42eb7b2c"},
            "generation": {"config_generation": 1},
            "path": {"local_repository": r"H:\My Drive\Other\Repository"},
            "repository": {
                "repository_id": "5" * 64,
                "local_repository_id": "5" * 64,
                "cloud_repository_id": "5" * 64,
            },
            "snapshot": {"snapshot_id": "6" * 64},
            "phase": {"verification_phase": "pre_activation"},
        }
        for label, mutation in mutations.items():
            with self.subTest(label=label):
                result = self.invoke(proof=self.direct_proof(**mutation))
                self.assertEqual("StatusUnavailable", result["kind"])
                self.assertFalse(result["provider_confirmed"])

    def test_newer_backup_attempt_invalidates_an_older_proof(self) -> None:
        current = self.backup_evidence(
            state="backing_up",
            verification_complete=False,
            started_utc="2026-07-29T00:20:00Z",
            finished_utc=None,
            snapshot_id=None,
        )
        result = self.invoke(proof=self.direct_proof(), current=current)
        self.assertEqual("StatusUnavailable", result["kind"])
        self.assertIn("repository-changing backup attempt", result["detail"])

    def test_inventory_restore_and_types_must_be_complete(self) -> None:
        mutations = {
            "missing": {"missing_files": 1},
            "extra": {"extra_files": 1},
            "mismatch": {"mismatched_files": 1},
            "ids": {"cloud_objects_with_ids": 5108},
            "hash": {"inventory_fingerprint_sha256": ""},
            "immutable": {"immutable_proof_sha256": ""},
            "asset_manifest": {
                "cloud_verification_assets_manifest_sha256": ""
            },
            "capture": {"native_capture_mode": "powershell_redirection"},
            "inventory_flag": {"exact_file_inventory_verified": False},
            "restore": {"direct_cloud_restore_verified": False},
            "typed_generation": {"config_generation": "2"},
            "typed_files": {"files": True},
        }
        for label, mutation in mutations.items():
            with self.subTest(label=label):
                result = self.invoke(proof=self.direct_proof(**mutation))
                self.assertEqual("StatusUnavailable", result["kind"])

    def test_proof_timestamp_must_follow_the_bound_backup(self) -> None:
        result = self.invoke(
            proof=self.direct_proof(verified_utc="2026-07-28T23:00:00Z")
        )
        self.assertEqual("StatusUnavailable", result["kind"])
        self.assertIn("predates", result["detail"])

    def test_direct_proof_is_preferred_and_malformed_proof_fails_closed(self) -> None:
        result = self.invoke(
            proof=self.direct_proof(),
            protected={"schema_version": 1, "state": "failed"},
        )
        self.assertEqual("RestoreVerified", result["kind"])

        invalid = self.invoke(
            proof_raw="{not-json",
            protected=self.legacy_mirror_status(),
        )
        self.assertEqual("StatusUnavailable", invalid["kind"])

    def test_credential_like_failure_is_bounded(self) -> None:
        failed = self.direct_proof(
            state="failed",
            error="RESTIC_PASSWORD_COMMAND and DPAPI details must stay private.",
        )
        result = self.invoke(proof=failed)
        self.assertEqual("Failed", result["kind"])
        self.assertNotIn("RESTIC_PASSWORD", result["detail"])
        self.assertNotIn("DPAPI", result["detail"])

    def test_settings_and_diagnostics_use_direct_cloud_evidence(self) -> None:
        window = WINDOW_SOURCE.read_text(encoding="utf-8")
        diagnostic = DIAGNOSTIC_SOURCE.read_text(encoding="utf-8")
        telemetry = TELEMETRY_SOURCE.read_text(encoding="utf-8")
        self.assertIn("independent direct-cloud restore", window)
        self.assertNotIn(
            "Separates local mirror verification from Google Drive upload",
            window,
        )
        self.assertIn("ResticBackuperCloudVerification", telemetry)
        self.assertIn('"evidence"', telemetry)
        self.assertIn("PostActivationVerificationPhase", telemetry)
        self.assertIn("directCloudVerification", diagnostic)
        self.assertIn("my-drive-latest-verification.json", diagnostic)


if __name__ == "__main__":
    unittest.main()
