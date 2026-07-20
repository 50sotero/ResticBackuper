from __future__ import annotations

import json
from pathlib import Path
import re
import subprocess
import unittest


PROJECT = Path(__file__).resolve().parents[1]


def repository_files() -> list[Path]:
    result = subprocess.run(
        [
            "git",
            "ls-files",
            "--cached",
            "--others",
            "--exclude-standard",
            "-z",
        ],
        cwd=PROJECT,
        check=True,
        capture_output=True,
    )
    return [
        PROJECT / item.decode("utf-8")
        for item in result.stdout.split(b"\0")
        if item
    ]


class RepositoryHygieneTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.files = repository_files()
        if not cls.files:
            raise AssertionError("repository file inventory is unexpectedly empty")

    def test_private_machine_identifiers_are_absent(self) -> None:
        # Build audit sentinels in pieces so this test does not contain the very
        # identifiers it is intended to detect.
        forbidden = [
            re.compile(
                rb"(?i)(?<![a-z0-9])"
                + re.escape(("vic" + "to").encode())
                + rb"(?![a-z0-9])"
            ),
            re.compile(
                rb"(?i)(?<![a-z0-9])"
                + re.escape(("vic" + "tor").encode())
                + rb"(?![a-z0-9])"
            ),
            re.compile(re.escape(("cc6c" + "e150").encode()), re.IGNORECASE),
            re.compile(
                rb"(?i)(?<![a-z0-9])"
                + re.escape(("cla" + "reio").encode())
                + rb"(?![a-z0-9])"
            ),
        ]
        findings: list[str] = []
        for path in self.files:
            relative = path.relative_to(PROJECT).as_posix()
            data = relative.encode("utf-8") + b"\0" + path.read_bytes()
            for marker in forbidden:
                if marker.search(data):
                    findings.append(f"{relative}: contains a private audit marker")
        self.assertEqual([], findings, "\n".join(findings))

    def test_no_literal_user_profile_paths_are_published(self) -> None:
        profile_pattern = re.compile(
            r"(?i)\b[a-z]:[\\/]+users[\\/]+([^\\/\s\"']+)"
        )
        allowed_placeholders = {
            "your-user",
            "you",
            "username",
            "<username>",
            "%username%",
            "$env:userprofile",
            "public",
            "default",
        }
        findings: list[str] = []
        for path in self.files:
            try:
                text = path.read_text(encoding="utf-8")
            except UnicodeDecodeError:
                continue
            for match in profile_pattern.finditer(text):
                component = match.group(1).rstrip(".,;:)").lower()
                if component not in allowed_placeholders:
                    findings.append(
                        f"{path.relative_to(PROJECT).as_posix()}: {match.group(0)}"
                    )
        self.assertEqual([], findings, "\n".join(findings))

    def test_no_token_or_private_key_signatures_are_present(self) -> None:
        signatures = {
            "private key": re.compile(
                rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"
            ),
            "GitHub token": re.compile(rb"gh[pousr]_[A-Za-z0-9]{20,}"),
            "OpenAI key": re.compile(rb"sk-(?:proj-)?[A-Za-z0-9_-]{20,}"),
            "AWS access key": re.compile(rb"AKIA[0-9A-Z]{16}"),
            "Google API key": re.compile(rb"AIza[0-9A-Za-z_-]{35}"),
            "recovery password": re.compile(
                rb"(?im)^Password:\s*[A-Za-z0-9_=-]{40,}\s*$"
            ),
        }
        findings: list[str] = []
        for path in self.files:
            data = path.read_bytes()
            for label, pattern in signatures.items():
                if pattern.search(data):
                    findings.append(
                        f"{path.relative_to(PROJECT).as_posix()}: possible {label}"
                    )
        self.assertEqual([], findings, "\n".join(findings))

    def test_json_sources_do_not_contain_secret_values(self) -> None:
        sensitive_keys = {
            "password",
            "token",
            "api_key",
            "private_key",
            "ciphertext_base64",
        }
        findings: list[str] = []

        def walk(value, location: str, relative: str) -> None:
            if isinstance(value, dict):
                for key, child in value.items():
                    child_location = f"{location}.{key}" if location else str(key)
                    if str(key).lower() in sensitive_keys and child not in (
                        None,
                        "",
                        "REPLACE-ME",
                    ):
                        findings.append(f"{relative}: nonempty {child_location}")
                    walk(child, child_location, relative)
            elif isinstance(value, list):
                for index, child in enumerate(value):
                    walk(child, f"{location}[{index}]", relative)

        for path in self.files:
            if path.suffix.lower() != ".json":
                continue
            relative = path.relative_to(PROJECT).as_posix()
            try:
                value = json.loads(path.read_text(encoding="utf-8"))
            except json.JSONDecodeError as error:
                self.fail(f"tracked JSON is invalid ({relative}): {error}")
            walk(value, "", relative)

        self.assertEqual([], findings, "\n".join(findings))

    def test_runtime_secrets_and_generated_artifacts_are_not_versioned(self) -> None:
        forbidden_suffixes = {".dll", ".exe", ".msi", ".msix", ".pdb", ".zip"}
        forbidden_exact_names = {
            "backup-config.json",
            "payload-manifest.json",
            "runtime-manifest.json",
            "scheduled-task.xml",
        }
        allowed_binary_assets = {
            "src/dashboard/assets/dashboard-icon.ico",
            "src/dashboard/assets/dashboard-icon.png",
        }
        findings: list[str] = []
        for path in self.files:
            relative = path.relative_to(PROJECT).as_posix()
            name = path.name.lower()
            if path.suffix.lower() in forbidden_suffixes:
                findings.append(f"{relative}: compiled or archive artifact")
            if name in forbidden_exact_names:
                findings.append(f"{relative}: runtime-generated file")
            if name.startswith("repository-password") and name.endswith(".json"):
                findings.append(f"{relative}: DPAPI secret envelope")
            if "recoverykey" in name and name.endswith(".txt"):
                findings.append(f"{relative}: recovery key material")
            if (
                path.suffix.lower() in {".ico", ".png"}
                and relative not in allowed_binary_assets
            ):
                findings.append(f"{relative}: unreviewed binary asset")
            if path.read_bytes()[:2] == b"MZ":
                findings.append(f"{relative}: Windows PE binary content")
        self.assertEqual([], findings, "\n".join(findings))

    def test_gitignore_blocks_machine_generated_material(self) -> None:
        ignored = [
            "backup-config.json",
            "src/backup-config.json",
            "state/repository-password.dpapi.json",
            "state/ResticBackuper-RecoveryKey.txt",
            "artifacts/release.zip",
            "src/dashboard/dist/ResticBackuperDashboard.exe",
        ]
        for sensitive_path in ignored:
            result = subprocess.run(
                ["git", "check-ignore", "--quiet", "--", sensitive_path],
                cwd=PROJECT,
                check=False,
            )
            self.assertEqual(
                0,
                result.returncode,
                f"sensitive path is not ignored: {sensitive_path}",
            )

        for public_source in (
            "src/backup-config.example.json",
            "src/excludes.txt",
            "src/dashboard/assets/dashboard-icon.ico",
        ):
            result = subprocess.run(
                ["git", "check-ignore", "--quiet", "--", public_source],
                cwd=PROJECT,
                check=False,
            )
            self.assertEqual(
                1,
                result.returncode,
                f"public source is unexpectedly ignored: {public_source}",
            )

    def test_dependency_pins_are_https_and_sha256_sized(self) -> None:
        dependencies = json.loads(
            (PROJECT / "dependencies.json").read_text(encoding="utf-8")
        )
        self.assertEqual(1, dependencies["schema_version"])
        for name in ("python", "restic"):
            with self.subTest(name=name):
                dependency = dependencies[name]
                self.assertRegex(dependency["version"], r"^\d+\.\d+\.\d+$")
                self.assertTrue(dependency["archive_url"].startswith("https://"))
                self.assertRegex(dependency["archive_sha256"], r"^[0-9a-f]{64}$")
        self.assertRegex(
            dependencies["restic"]["executable_sha256"], r"^[0-9a-f]{64}$"
        )

        restic_release = json.loads(
            (PROJECT / "src" / "restic-release.json").read_text(encoding="utf-8")
        )
        for key in ("version", "archive_url", "archive_sha256", "executable_sha256"):
            self.assertEqual(dependencies["restic"][key], restic_release[key])

    def test_license_and_version_files_are_nonempty(self) -> None:
        license_text = (PROJECT / "LICENSE").read_text(encoding="utf-8")
        self.assertTrue(license_text.startswith("MIT License\n"))
        self.assertIn("Permission is hereby granted", license_text)
        self.assertRegex(
            (PROJECT / "VERSION").read_text(encoding="utf-8").strip(),
            r"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$",
        )


if __name__ == "__main__":
    unittest.main()
