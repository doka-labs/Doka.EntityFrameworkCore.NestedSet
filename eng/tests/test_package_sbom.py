"""Adversarial standard-library checks for shipping-package SBOM verification."""

import base64
import hashlib
import importlib.util
import os
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("package_sbom", Path(__file__).parents[1] / "verify-package-sbom.py")
SBOM = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SBOM)


def record_error(action):
    """Keep the operation separate from assertions when testing rejected input."""
    try:
        action()
    except ValueError as error:
        return error

    return None


class PackageSbomTests(unittest.TestCase):
    """Keep hash, identity and closure failures independently observable."""

    def setUp(self):
        """Create an actual candidate archive and a minimal two-component SPDX/restore pair."""
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.name = "Doka.EntityFrameworkCore.NestedSet"
        self.version = "0.1.0-dev"
        self.archive = self.make_archive(self.name, self.version)
        self.assets = {
            "targets": {"net10.0": {
                f"{self.name}/{self.version}": {"type": "package", "dependencies": {"Doka.NestedSet": self.version}},
                f"Doka.NestedSet/{self.version}": {"type": "package"},
            }},
            "libraries": {
                f"{self.name}/{self.version}": {"type": "package"},
                f"Doka.NestedSet/{self.version}": {"type": "package"},
            },
            "project": {
                "frameworks": {"net10.0": {"dependencies": {
                    self.name: {"version": f"[{self.version}, {self.version}]"},
                }}},
                "restore": {"frameworks": {"net10.0": {"projectReferences": {}}}},
            },
        }

        self.closure = {(self.name.lower(), self.version), ("doka.nestedset", self.version)}
        self.manifest = {
            "spdxVersion": "SPDX-2.2",
            "name": f"{self.name} {self.version}",
            "documentDescribes": ["SPDXRef-RootPackage"],
            "files": [{"fileName": f"./{self.archive.name}", "SPDXID": "SPDXRef-Archive",
                       "checksums": [{"algorithm": "SHA256", "checksumValue": SBOM.sha256(self.archive)}]}],
            "packages": [
                {"name": self.name, "versionInfo": self.version, "SPDXID": "SPDXRef-RootPackage",
                 "hasFiles": ["SPDXRef-Archive"]},
                self.component(self.name), self.component("Doka.NestedSet"),
            ],
        }

        self.vendor = {"Result": "Success", "ValidationErrors": {"Count": 0}, "Summary": {
            "ValidationTelemetery": {"TotalFilesInManifest": 1, "FilesSuccessfulCount": 1,
                                     "FilesValidatedCount": 1, "FilesFailedCount": 0, "FilesSkippedCount": 0},
        }}

    def make_archive(self, name, version):
        """Build a real nuspec-bearing ZIP so identity checks read package metadata."""
        archive = self.directory / f"{name}.{version}.nupkg"
        dependencies = (f'<dependencies><group targetFramework="net10.0">'
                        f'<dependency id="Doka.NestedSet" version="{version}" />'
                        '</group></dependencies>') if name == self.name else ""

        with zipfile.ZipFile(archive, "w") as package:
            package.writestr(f"{name}.nuspec", "<package><metadata>"
                             f"<id>{name}</id><version>{version}</version>{dependencies}</metadata></package>")

        return archive

    def component(self, name):
        """Describe one NuGet component with a matching package URL."""
        return {"name": name, "versionInfo": self.version, "SPDXID": f"SPDXRef-{name}",
                "externalRefs": [{"referenceType": "purl", "referenceLocator": f"pkg:nuget/{name}@{self.version}"}]}

    def bound_archives(self):
        """Add actual candidate hashes to restore evidence for local-feed verification tests."""
        core = self.make_archive("Doka.NestedSet", self.version)
        archives = {self.name: self.archive, "Doka.NestedSet": core}

        for name, archive in archives.items():
            library = self.assets["libraries"][f"{name}/{self.version}"]
            library["sha512"] = base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()).decode("ascii")
            library["path"] = f"{name.lower()}/{self.version}"

        return archives

    def verify(self, manifest=None):
        """Run the independent verifier with the known restore closure."""
        return SBOM.verify_manifest(self.manifest if manifest is None else manifest, self.archive, self.closure,
                                    self.name, self.version)

    def test_exact_candidate_and_restore_closure_pass(self):
        """The valid archive is identified by its actual bytes and both restored components."""
        # Arrange
        closure = SBOM.dependency_closure(self.assets, self.name, self.version)

        # Act
        result = SBOM.verify_manifest(self.manifest, self.archive, closure, self.name, self.version)

        # Assert
        self.assertEqual(self.closure, closure)
        self.assertEqual(2, result["components"])
        self.assertEqual(SBOM.sha256(self.archive), result["sha256"])

    def test_tampered_archive_is_rejected(self):
        """Changed package bytes invalidate the manifest even when its identity remains unchanged."""
        # Arrange
        with self.archive.open("ab") as stream:
            stream.write(b"tampered")

        # Act
        error = record_error(self.verify)

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "SHA256")

    def test_omitted_dependency_is_rejected(self):
        """Presence of the shipping package cannot hide a missing transitive component."""
        # Arrange
        self.manifest["packages"].pop()

        # Act
        error = record_error(self.verify)

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "Dependency closure mismatch")

    def test_consistently_truncated_closure_cannot_override_nuspec(self):
        """Matching reduced restore/SPDX inventories cannot hide a dependency declared by the candidate itself."""
        # Arrange
        self.manifest["packages"].pop()
        self.closure.remove(("doka.nestedset", self.version))

        # Act
        error = record_error(self.verify)

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "nuspec")

    def test_wrong_dependency_version_with_consistent_purl_is_rejected(self):
        """A self-consistent SPDX component must still match the actual NuGet resolution."""
        # Arrange
        component = self.manifest["packages"][-1]
        component["versionInfo"] = "999.0.0"
        component["externalRefs"][0]["referenceLocator"] = "pkg:nuget/Doka.NestedSet@999.0.0"

        # Act
        error = record_error(self.verify)

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "Dependency closure mismatch")

    def test_injected_test_tool_is_rejected(self):
        """A test library outside the consumer closure cannot enter the shipping document."""
        # Arrange
        self.manifest["packages"].append(self.component("xunit"))

        # Act
        error = record_error(self.verify)

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "Dependency closure mismatch")

    def test_wrong_document_version_is_rejected(self):
        """The document cannot claim another release while preserving the correct component list."""
        # Arrange
        self.manifest["name"] = f"{self.name} 999.0.0"

        # Act
        error = record_error(self.verify)

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "identity/version")

    def test_restore_containing_test_tool_is_rejected(self):
        """Even a reachable test dependency in restore evidence is outside the shipping contract."""
        # Arrange
        self.assets["libraries"]["xunit/2.9.3"] = {"type": "package"}
        target = self.assets["targets"]["net10.0"]
        target["xunit/2.9.3"] = {"type": "package"}
        target[f"{self.name}/{self.version}"]["dependencies"]["xunit"] = "2.9.3"

        # Act
        error = record_error(lambda: SBOM.dependency_closure(self.assets, self.name, self.version))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "Test/build tooling")

    def test_unreachable_restore_component_is_rejected(self):
        """An otherwise valid package inventory must be reachable from the sole consumer reference."""
        # Arrange
        self.assets["libraries"]["Unexpected/1.0.0"] = {"type": "package"}
        self.assets["targets"]["net10.0"]["Unexpected/1.0.0"] = {"type": "package"}

        # Act
        error = record_error(lambda: SBOM.dependency_closure(self.assets, self.name, self.version))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "Unreachable")

    def test_extra_consumer_reference_is_rejected(self):
        """A full solution or multi-package consumer cannot replace per-package dependency resolution."""
        # Arrange
        self.assets["project"]["frameworks"]["net10.0"]["dependencies"]["Other"] = {"version": "[1.0.0]"}

        # Act
        error = record_error(lambda: SBOM.dependency_closure(self.assets, self.name, self.version))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "only its shipping package")

    def test_vendor_skipped_file_is_rejected(self):
        """Successful process/report status is insufficient if the archive was skipped."""
        # Arrange
        self.vendor["Summary"]["ValidationTelemetery"]["FilesSkippedCount"] = 1

        # Act
        error = record_error(lambda: SBOM.verify_vendor(self.vendor))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "one unmodified shipping archive")

    def test_nuspec_identity_controls_shipping_inventory(self):
        """Both actual candidate identities are required even when files have plausible names."""
        # Arrange
        core = self.make_archive("Doka.NestedSet", self.version)

        # Act
        result = SBOM.shipping_archives(self.directory, self.version)

        # Assert
        self.assertEqual({self.name, "Doka.NestedSet"}, set(result))
        self.assertEqual(core.resolve(), result["Doka.NestedSet"])

    def test_wrong_candidate_version_is_rejected(self):
        """Candidate identity inspection rejects a mismatching requested release."""
        # Arrange
        self.make_archive("Doka.NestedSet", self.version)

        # Act
        error = record_error(lambda: SBOM.shipping_archives(self.directory, "999.0.0"))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "Candidate version mismatch")

    def test_real_candidate_dependencies_and_restore_hashes_match(self):
        """Local-feed binding accepts the real nuspec declarations and actual SHA512 values."""
        # Arrange
        archives = self.bound_archives()

        # Act
        error = record_error(lambda: SBOM.verify_shipping_bindings(self.assets, archives))

        # Assert
        self.assertIsNone(error)

    def test_candidate_dependency_version_disagreement_is_rejected(self):
        """The actual nuspec, rather than edited restore declarations, defines direct dependency constraints."""
        # Arrange
        archives = self.bound_archives()
        self.assets["targets"]["net10.0"][f"{self.name}/{self.version}"]["dependencies"]["Doka.NestedSet"] = "999.0.0"

        # Act
        error = record_error(lambda: SBOM.verify_shipping_bindings(self.assets, archives))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "nuspec")

    def test_restored_candidate_sha512_disagreement_is_rejected(self):
        """A stale local package cannot masquerade as the candidate by retaining its ID and version."""
        # Arrange
        archives = self.bound_archives()
        self.assets["libraries"][f"{self.name}/{self.version}"]["sha512"] = "wrong-hash"

        # Act
        error = record_error(lambda: SBOM.verify_shipping_bindings(self.assets, archives))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "SHA512")

    def test_local_dependency_cannot_resolve_to_another_candidate_version(self):
        """A reachable local dependency cannot bypass byte binding by changing its resolved version."""
        # Arrange
        archives = self.bound_archives()
        original = f"Doka.NestedSet/{self.version}"
        replacement = "Doka.NestedSet/99.0.0"
        self.assets["libraries"][replacement] = self.assets["libraries"].pop(original)
        target = self.assets["targets"]["net10.0"]
        target[replacement] = target.pop(original)

        # Act
        error = record_error(lambda: SBOM.verify_shipping_bindings(self.assets, archives))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "candidate version")

    def test_different_restored_archive_bytes_are_rejected(self):
        """Fresh-cache verification reads actual restored package bytes instead of trusting hash metadata alone."""
        # Arrange
        archives = self.bound_archives()
        cache = self.directory / "cache"

        for name, archive in archives.items():
            restored = cache / name.lower() / self.version / archive.name.lower()
            restored.parent.mkdir(parents=True)
            restored.write_bytes(archive.read_bytes() + b"different-cache-bytes")

        # Act
        error = record_error(lambda: SBOM.verify_shipping_bindings(self.assets, archives, cache))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "archive bytes differ")

    def test_missing_file_is_rejected_by_vendor_gate(self):
        """A validation report with a missing archive cannot authorize the candidate SBOM."""
        # Arrange
        self.vendor["Result"] = "Failure"
        self.vendor["ValidationErrors"] = {"Count": 1, "Errors": [
            {"ErrorType": "MissingFile", "Path": f"./{self.archive.name}"},
        ]}

        # Act
        error = record_error(lambda: SBOM.verify_vendor(self.vendor))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "one unmodified shipping archive")

    def test_hash_failure_is_rejected_by_normal_vendor_gate(self):
        """The precise changed-file hash failure must also fail the normal success gate."""
        # Arrange
        self.vendor["Result"] = "Failure"
        self.vendor["ValidationErrors"] = {"Count": 1, "Errors": [
            {"ErrorType": "InvalidHash", "Path": f"./{self.archive.name}"},
        ]}

        # Act
        error = record_error(lambda: SBOM.verify_vendor(self.vendor))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "one unmodified shipping archive")

    def test_wrapper_cleans_vendor_and_nuget_scratch_files(self):
        """Native-tool scratch files must remain inside the wrapper's owned cleanup directory."""
        # Arrange
        tools = self.directory / "stub-tools"
        temporary = self.directory / "temporary"
        tools.mkdir()
        temporary.mkdir()
        stubs = {
            "uname": '#!/bin/sh\nif [ "$1" = "-s" ]; then echo Darwin; else echo arm64; fi\n',
            "curl": '#!/bin/sh\nwhile [ "$1" != "--output" ]; do shift; done\n: > "$2"\n',
            "python3": '#!/bin/sh\nif [ "$1" = "-" ]; then cat >/dev/null; exit 0; fi\n'
                       'mkdir -p "$TMPDIR/NuGetScratch"\n: > "$TMPDIR/ScanManifest.probe.json"\n',
        }

        for name, content in stubs.items():
            path = tools / name
            path.write_text(content, encoding="ascii")
            path.chmod(0o755)

        environment = os.environ.copy()
        environment["PATH"] = str(tools) + os.pathsep + environment["PATH"]
        environment["TMPDIR"] = str(temporary)
        command = ["bash", str(Path(__file__).parents[1] / "generate-sbom.sh"), "--package-dir", str(self.directory),
                   "--output", str(self.directory / "output"), "--version", self.version]

        # Act
        result = subprocess.run(command, env=environment, capture_output=True, text=True, check=False)

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual([], list(temporary.iterdir()))


if __name__ == "__main__":
    unittest.main()
