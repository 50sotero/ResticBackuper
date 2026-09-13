from pathlib import Path
import unittest


PROJECT = Path(__file__).resolve().parents[1]
DASHBOARD = PROJECT / "src" / "dashboard"


class DashboardAnomalyReviewTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.backend = (PROJECT / "src" / "anomaly_review.py").read_text(encoding="utf-8")
        cls.telemetry = (DASHBOARD / "Telemetry.cs").read_text(encoding="utf-8")
        cls.manager = (DASHBOARD / "RecoveryHealthManager.cs").read_text(
            encoding="utf-8"
        )
        cls.window = (DASHBOARD / "DashboardWindow.cs").read_text(encoding="utf-8")

    def test_backend_approval_is_exact_non_destructive_and_narrow(self) -> None:
        for evidence in (
            '"scope": ["anomaly_review_acknowledgement"]',
            '"maintenance_hold_remains": True',
            '"snapshots_modified": False',
            '"repository_modified": False',
            "sha256_file(evidence_path) != expected_evidence_sha256",
            'evidence.get("run_id") != expected_run_id',
            'evidence.get("snapshot_id") != expected_snapshot_id',
        ):
            self.assertIn(evidence, self.backend)
        self.assertNotRegex(
            self.backend,
            r'(?i)["\'](?:forget|prune|delete|key remove|unlock)["\']',
        )

    def test_dashboard_requires_uac_and_revalidates_protected_evidence(self) -> None:
        for evidence in (
            'start.Verb = "runas"',
            '"--expected-run-id", snapshot.RunId',
            '"--expected-snapshot-id", snapshot.SnapshotId',
            '"--expected-evidence-sha256", evidenceHash',
            "FileSha256(evidencePath) != evidenceHash",
            'ReadString(acknowledgement, "backup_evidence_sha256") == evidenceHash',
            'ReadBool(acknowledgement, "maintenance_hold_remains")',
            "!ReadBool(acknowledgement, \"snapshots_modified\")",
            "!ReadBool(acknowledgement, \"repository_modified\")",
        ):
            self.assertIn(evidence, self.manager)

    def test_telemetry_accepts_only_the_exact_current_evidence_hash(self) -> None:
        self.assertIn("public bool AnomalyReviewAcknowledged", self.telemetry)
        self.assertIn("public bool NeedsAnomalyReview", self.telemetry)
        self.assertIn("lastSuccessRead.Sha256", self.telemetry)
        self.assertIn(
            'GetString(acknowledgement, "backup_evidence_sha256", string.Empty)',
            self.telemetry,
        )
        self.assertIn(
            'GetString(evidence, "snapshot_id", string.Empty)', self.telemetry
        )
        self.assertIn("algorithm.ComputeHash(payload)", self.telemetry)

    def test_ui_makes_review_visible_and_states_remaining_hold(self) -> None:
        for evidence in (
            'CreateButton("Review changes")',
            '"ReviewBackupChangesButton"',
            "OnReviewChangesClick",
            "AnomalyReviewLauncher.AcknowledgeReview",
            "snapshot.NeedsAnomalyReview",
            "snapshot.AnomalyReviewAcknowledged",
            "leaves the maintenance hold in place for deletion and retention actions",
            "does not modify snapshots or repository data",
            "does not pause or gate Google Drive upload",
            'SetBadge("REVIEW ACKNOWLEDGED"',
        ):
            self.assertIn(evidence, self.window)
        self.assertNotIn("off-site promotion is paused", self.window.lower())

if __name__ == "__main__":
    unittest.main()
