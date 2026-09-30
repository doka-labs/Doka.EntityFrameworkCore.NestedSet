"""Adversarial archive and candidate identity checks without SDK or network execution."""

import hashlib
import sys
import tempfile
import unittest
import warnings
import zipfile
from pathlib import Path
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).parents[1] / "release"))
import packages


def make_packages(directory, version="0.1.0-dev", commit="a" * 40):
    """Create both actual ZIP shapes with injectable contents for isolated rejection probes."""
    for package_id in packages.PACKAGE_IDS:
        dependency = "" if package_id == packages.PACKAGE_IDS[0] else (
            f'<dependency id="Doka.NestedSet" version="{version}" />'
            '<dependency id="Microsoft.EntityFrameworkCore" version="[10.0.12, 11.0.0)" />'
            '<dependency id="Microsoft.EntityFrameworkCore.Relational" version="[10.0.12, 11.0.0)" />')

        for extension in ("nupkg", "snupkg"):
            spec = (f'<package><metadata><id>{package_id}</id><version>{version}</version>'
                    '<license type="expression">MIT</license><readme>README.md</readme>'
                    f'<repository type="git" url="{packages.REPOSITORY}" commit="{commit}" />'
                    f'<dependencies><group targetFramework="net10.0">{dependency}</group></dependencies>'
                    '<packageTypes><packageType name="SymbolsPackage" /></packageTypes></metadata></package>')

            with zipfile.ZipFile(directory / f"{package_id}.{version}.{extension}", "w") as archive:
                archive.writestr(package_id + ".nuspec", spec)
                archive.writestr("_rels/.rels", "<Relationships/>")
                archive.writestr("[Content_Types].xml", "<Types/>")
                archive.writestr("package/services/metadata/core-properties/nuget.psmdcp", "<coreProperties/>")

                if extension == "nupkg":
                    archive.writestr("LICENSE", (packages.ROOT / "LICENSE").read_bytes())
                    archive.writestr("README.md", "Package usage")
                    archive.writestr(f"lib/net10.0/{package_id}.dll", b"fixture PE")
                    archive.writestr(f"lib/net10.0/{package_id}.xml",
                                     f"<doc><assembly><name>{package_id}</name></assembly><members/></doc>")
                else:
                    archive.writestr(f"lib/net10.0/{package_id}.pdb", package_id.encode("ascii"))


def probe(primary, symbols, package_id):
    """Stand in for framework PE parsing; root runtime validation exercises the actual helper."""
    name = package_id + ".pdb"

    return [{"pdbName": name, "url": f"https://symbols.nuget.org/download/symbols/{name}/{'a' * 32}FFFFFFFF/{name}",
             "checksum": "SHA256:" + "b" * 64, "sha256": hashlib.sha256(package_id.encode("ascii")).hexdigest()}]


def rewrite_archive(path, transform):
    """Replace an archive with an adversarial entry set without adding accidental duplicates."""
    content = packages.archive_entries(path)
    transform(content)

    with zipfile.ZipFile(path, "w") as archive:
        for name, data in content.items():
            archive.writestr(name, data)


def capture(action):
    """Keep rejected operations in one Act section rather than mixing assertions with execution."""
    try:
        action()
    except (ValueError, OSError) as error:
        return error

    return None


class PackageTests(unittest.TestCase):
    """Validate package structure independently from helper/parser execution."""

    def setUp(self):
        """Prepare a fresh, complete candidate and scope the metadata-parser stub."""
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        make_packages(self.directory)
        self.parser = patch.object(packages, "inspect_symbols", side_effect=probe)
        self.parser.start()
        self.addCleanup(self.parser.stop)
        self.primary = self.directory / "Doka.NestedSet.0.1.0-dev.nupkg"

    def test_complete_candidate_is_deterministic(self):
        """The receipt carries both identities and exact primary/symbol hashes."""
        # Arrange
        expected = list(packages.PACKAGE_IDS)

        # Act
        result = packages.verify_packages(self.directory, "0.1.0-dev", "a" * 40)

        # Assert
        self.assertEqual(expected, [item["id"] for item in result["packages"]])
        self.assertEqual(packages.sha256(self.primary), result["packages"][0]["nupkgSha256"])
        self.assertEqual(1, result["schemaVersion"])

    def test_inspector_receives_informational_version_from_nuspec(self):
        """The actual assembly version gate must receive the selected package version even for an opaque path."""
        # Arrange
        primary = self.directory / "opaque.nupkg"
        self.primary.rename(primary)
        symbols = self.directory / "Doka.NestedSet.0.1.0-dev.snupkg"
        completed = Mock(returncode=0, stdout="{}", stderr="")

        # Act
        with patch.object(packages.subprocess, "run", return_value=completed) as command:
            packages.run_inspector(primary, symbols, "Doka.NestedSet")

        # Assert
        self.assertEqual([str(primary), str(symbols), "Doka.NestedSet", "0.1.0-dev"], command.call_args.args[0][-4:])

    def test_inspector_negative_probes_cover_checksum_and_release_version(self):
        """Qualification must execute both actual helper rejection paths and retain their distinct diagnostics."""
        # Arrange
        results = [
            Mock(returncode=1, stdout="", stderr="Portable PDB checksum does not match the candidate assembly.\n"),
            Mock(returncode=1, stdout="", stderr="Assembly informational version does not match its package.\n"),
        ]

        # Act
        with patch.object(packages, "run_inspector", side_effect=results) as inspector:
            receipt = packages.verify_symbol_rejection(self.directory, "0.1.0-dev")

        # Assert
        self.assertEqual(["corrupted-portable-pdb", "wrong-informational-version"],
                         [case["case"] for case in receipt["cases"]])
        self.assertEqual(2, inspector.call_count)
        self.assertEqual("0.1.0-dev-wrong-version-probe", inspector.call_args.kwargs["expected_version"])

    def test_wrong_version_probe_must_reject_for_the_version_guard(self):
        """An unrelated helper failure must not count as successful verification of the release version guard."""
        # Arrange
        results = [
            Mock(returncode=1, stdout="", stderr="Portable PDB checksum does not match the candidate assembly.\n"),
            Mock(returncode=1, stdout="", stderr="Portable PDB checksum does not match the candidate assembly.\n"),
        ]

        # Act
        with patch.object(packages, "run_inspector", side_effect=results):
            error = capture(lambda: packages.verify_symbol_rejection(self.directory, "0.1.0-dev"))

        # Assert
        self.assertRegex(str(error), "Wrong-version probe was accepted or did not report a controlled failure")

    def test_unexpected_archive_is_rejected(self):
        """Extra archives cannot hide behind a valid two-package subset."""
        # Arrange
        (self.directory / "unexpected.nupkg").write_bytes(b"other")

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev"))

        # Assert
        self.assertRegex(str(error), "exactly two primary")

    def test_missing_symbols_are_rejected(self):
        """A primary-only candidate is insufficient for publication."""
        # Arrange
        (self.directory / "Doka.NestedSet.0.1.0-dev.snupkg").unlink()

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev"))

        # Assert
        self.assertIsInstance(error, packages.PackageError)

    def test_wrong_source_commit_is_rejected(self):
        """Plausible IDs and bytes cannot substitute another source revision."""
        # Arrange
        other_commit = "b" * 40

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev", other_commit))

        # Assert
        self.assertRegex(str(error), "commit mismatch")

    def test_wrong_nuspec_identity_is_rejected(self):
        """Canonical archive filenames cannot conceal a different package ID."""
        # Arrange
        rewrite_archive(self.primary, lambda entries: entries.update({
            "Doka.NestedSet.nuspec": entries["Doka.NestedSet.nuspec"].replace(b"<id>Doka.NestedSet</id>", b"<id>Other</id>")}))

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev"))

        # Assert
        self.assertRegex(str(error), "identity mismatch")

    def test_wrong_dependency_binding_is_rejected(self):
        """The EF nuspec must resolve to the exact candidate Core version."""
        # Arrange
        for suffix in ("nupkg", "snupkg"):
            path = self.directory / f"Doka.EntityFrameworkCore.NestedSet.0.1.0-dev.{suffix}"
            rewrite_archive(path, lambda entries: entries.update({"Doka.EntityFrameworkCore.NestedSet.nuspec":
                entries["Doka.EntityFrameworkCore.NestedSet.nuspec"].replace(
                    b'id="Doka.NestedSet" version="0.1.0-dev"', b'id="Doka.NestedSet" version="9.0.0"')}))

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev"))

        # Assert
        self.assertRegex(str(error), "candidate Core")

    def change_ef_dependencies(self, before, after):
        """Alter both nuspecs consistently so a range defect is not hidden by symbol-pair mismatch rejection."""
        for suffix in ("nupkg", "snupkg"):
            path = self.directory / f"Doka.EntityFrameworkCore.NestedSet.0.1.0-dev.{suffix}"
            rewrite_archive(path, lambda entries: entries.update({"Doka.EntityFrameworkCore.NestedSet.nuspec":
                entries["Doka.EntityFrameworkCore.NestedSet.nuspec"].replace(before, after)}))

    def test_unbounded_ef_dependency_is_rejected(self):
        """An exact-looking minimum version must not remove the source-controlled upper EF major bound."""
        # Arrange
        self.change_ef_dependencies(b'version="[10.0.12, 11.0.0)"', b'version="10.0.12"')

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev"))

        # Assert
        self.assertRegex(str(error), "bounded inclusive-floor/exclusive-major range")

    def test_wrong_ef_major_bound_is_rejected(self):
        """A syntactically bounded range cannot broaden the supported EF major contract."""
        # Arrange
        self.change_ef_dependencies(b'version="[10.0.12, 11.0.0)"', b'version="[10.0.12, 12.0.0)"')

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev"))

        # Assert
        self.assertRegex(str(error), "range differs from the shipping contract")

    def test_wrong_ef_patch_floor_is_rejected(self):
        """The upper major boundary alone cannot authorize an unqualified lower EF patch."""
        # Arrange
        self.change_ef_dependencies(b'version="[10.0.12, 11.0.0)"', b'version="[10.0.11, 11.0.0)"')

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev"))

        # Assert
        self.assertRegex(str(error), "range differs from the shipping contract")

    def test_missing_direct_ef_dependency_is_rejected(self):
        """The package must preserve every direct shipping reference even if another dependency brings it transitively."""
        # Arrange
        self.change_ef_dependencies(b'<dependency id="Microsoft.EntityFrameworkCore" version="[10.0.12, 11.0.0)" />', b"")

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev"))

        # Assert
        self.assertRegex(str(error), "Unexpected EF dependency closure")

    def test_missing_relational_dependency_is_rejected(self):
        """The EF package cannot omit the relational API dependency declared by its shipping project."""
        # Arrange
        self.change_ef_dependencies(
            b'<dependency id="Microsoft.EntityFrameworkCore.Relational" version="[10.0.12, 11.0.0)" />', b"")

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev"))

        # Assert
        self.assertRegex(str(error), "Unexpected EF dependency closure")

    def test_missing_documentation_is_rejected(self):
        """Consumers need XML documentation in the actual published payload."""
        # Arrange
        rewrite_archive(self.primary, lambda entries: entries.pop("lib/net10.0/Doka.NestedSet.xml"))

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev"))

        # Assert
        self.assertRegex(str(error), "Missing primary package payload")

    def test_undeclared_root_payload_is_rejected(self):
        """Extra assets cannot bypass the strict inventory by avoiding a known executable folder."""
        # Arrange
        with zipfile.ZipFile(self.primary, "a") as archive:
            archive.writestr("unexpected.dll", b"unreviewed assembly")

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev"))

        # Assert
        self.assertRegex(str(error), "Unexpected package payload")

    def test_wrong_documentation_assembly_is_rejected(self):
        """A nonempty XML file must document the actual package assembly."""
        # Arrange
        rewrite_archive(self.primary, lambda entries: entries.update({"lib/net10.0/Doka.NestedSet.xml":
            b"<doc><assembly><name>Other</name></assembly><members/></doc>"}))

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev"))

        # Assert
        self.assertRegex(str(error), "documentation assembly identity")

    def test_wrong_packaged_license_is_rejected(self):
        """The license payload must agree with the source license even when the nuspec still says MIT."""
        # Arrange
        rewrite_archive(self.primary, lambda entries: entries.update({"LICENSE": b"Different terms"}))

        # Act
        error = capture(lambda: packages.verify_packages(self.directory, "0.1.0-dev"))

        # Assert
        self.assertRegex(str(error), "license differs")

    def test_path_traversal_is_rejected(self):
        """ZIP traversal is rejected before metadata is trusted."""
        # Arrange
        with zipfile.ZipFile(self.primary, "a") as archive:
            archive.writestr("../outside", b"bad")

        # Act
        error = capture(lambda: packages.archive_entries(self.primary))

        # Assert
        self.assertRegex(str(error), "Unsafe ZIP path")

    def test_case_collisions_are_rejected(self):
        """Ambiguous extraction names cannot differ by platform filesystem rules."""
        # Arrange
        with zipfile.ZipFile(self.primary, "a") as archive:
            archive.writestr("readme.md", b"bad")

        # Act
        error = capture(lambda: packages.archive_entries(self.primary))

        # Assert
        self.assertRegex(str(error), "case-colliding")

    def test_duplicate_paths_are_rejected(self):
        """A later duplicate cannot override the initially inspected payload."""
        # Arrange
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)

            with zipfile.ZipFile(self.primary, "a") as archive:
                archive.writestr("README.md", b"bad")

        # Act
        error = capture(lambda: packages.archive_entries(self.primary))

        # Assert
        self.assertRegex(str(error), "Duplicate")

    def test_signature_metadata_is_the_only_excluded_payload(self):
        """Repository signatures change raw archive bytes but preserve canonical package content."""
        # Arrange
        before = packages.canonical_payload(self.primary)

        with zipfile.ZipFile(self.primary, "a") as archive:
            archive.writestr(".signature.p7s", b"signature")

        # Act
        after = packages.canonical_payload(self.primary)

        # Assert
        self.assertEqual(before, after)

    def test_changed_payload_is_visible_in_canonical_comparison(self):
        """Signature exclusion must not conceal changed package documentation or assemblies."""
        # Arrange
        before = packages.canonical_payload(self.primary)
        rewrite_archive(self.primary, lambda entries: entries.update({"README.md": b"changed"}))

        # Act
        after = packages.canonical_payload(self.primary)

        # Assert
        self.assertNotEqual(before, after)

    def test_sealed_manifest_validates_without_executing_metadata_helper(self):
        """A hosted job may verify the qualified manifest and exact four bytes without rebuilding tooling."""
        # Arrange
        manifest = packages.verify_packages(self.directory, "0.1.0-dev")
        path = self.directory / "package-manifest.json"
        packages.write_json(path, manifest)

        # Act
        with patch.object(packages, "inspect_symbols", side_effect=AssertionError("Helper must not execute")):
            result = packages.verified_manifest(self.directory, "0.1.0-dev", path)

        # Assert
        self.assertEqual(manifest, result)

    def test_sealed_manifest_rejects_changed_archive_bytes(self):
        """A correctly shaped receipt does not authorize replacing any qualified archive."""
        # Arrange
        manifest = packages.verify_packages(self.directory, "0.1.0-dev")

        with self.primary.open("ab") as archive:
            archive.write(b"changed archive bytes")

        # Act
        error = capture(lambda: packages.verified_manifest(self.directory, "0.1.0-dev", manifest))

        # Assert
        self.assertRegex(str(error), "archive hash mismatch")

    def test_sealed_manifest_rejects_foreign_identity(self):
        """A second row cannot relabel a foreign package as another qualified release subject."""
        # Arrange
        manifest = packages.verify_packages(self.directory, "0.1.0-dev")
        manifest["packages"][1]["id"] = "Other.Package"

        # Act
        error = capture(lambda: packages.verified_manifest(self.directory, "0.1.0-dev", manifest))

        # Assert
        self.assertRegex(str(error), "identity/version mismatch")

    def test_sealed_manifest_rejects_other_version(self):
        """A manifest version cannot drift independently of the requested release."""
        # Arrange
        manifest = packages.verify_packages(self.directory, "0.1.0-dev")
        manifest["packages"][0]["version"] = "0.2.0"

        # Act
        error = capture(lambda: packages.verified_manifest(self.directory, "0.1.0-dev", manifest))

        # Assert
        self.assertRegex(str(error), "identity/version mismatch")

    def test_sealed_manifest_rejects_filename_traversal(self):
        """Only the exact qualified archive basename may select a manifest subject."""
        # Arrange
        manifest = packages.verify_packages(self.directory, "0.1.0-dev")
        manifest["packages"][0]["nupkg"] = "../Doka.NestedSet.0.1.0-dev.nupkg"

        # Act
        error = capture(lambda: packages.verified_manifest(self.directory, "0.1.0-dev", manifest))

        # Assert
        self.assertRegex(str(error), "filename mismatch")

    def test_sealed_manifest_rejects_wrong_symbol_hash(self):
        """Readback probes remain bound to the PDB bytes actually contained in the qualified symbols archive."""
        # Arrange
        manifest = packages.verify_packages(self.directory, "0.1.0-dev")
        manifest["packages"][0]["symbols"][0]["sha256"] = "0" * 64

        # Act
        error = capture(lambda: packages.verified_manifest(self.directory, "0.1.0-dev", manifest))

        # Assert
        self.assertRegex(str(error), "PDB probe hash mismatch")

    def test_sealed_manifest_rejects_foreign_symbol_endpoint(self):
        """A sealed probe may query only the official NuGet endpoint for its exact PDB name."""
        # Arrange
        manifest = packages.verify_packages(self.directory, "0.1.0-dev")
        manifest["packages"][0]["symbols"][0]["url"] = "https://other.invalid/symbol.pdb"

        # Act
        error = capture(lambda: packages.verified_manifest(self.directory, "0.1.0-dev", manifest))

        # Assert
        self.assertRegex(str(error), "Invalid Portable PDB probe")

    def test_sealed_manifest_rejects_missing_checksum(self):
        """Symbol readback must retain the checksum-validation request header derived during qualification."""
        # Arrange
        manifest = packages.verify_packages(self.directory, "0.1.0-dev")
        manifest["packages"][0]["symbols"][0]["checksum"] = "SHA256:"

        # Act
        error = capture(lambda: packages.verified_manifest(self.directory, "0.1.0-dev", manifest))

        # Assert
        self.assertRegex(str(error), "Invalid Portable PDB probe")

    def test_sealed_manifest_rejects_missing_package_row(self):
        """An incomplete manifest cannot authorize the remaining package subset."""
        # Arrange
        manifest = packages.verify_packages(self.directory, "0.1.0-dev")
        manifest["packages"].pop()

        # Act
        error = capture(lambda: packages.verified_manifest(self.directory, "0.1.0-dev", manifest))

        # Assert
        self.assertRegex(str(error), "Incomplete package manifest inventory")


if __name__ == "__main__":
    unittest.main()
