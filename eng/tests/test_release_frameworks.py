"""Exercise branch-owned framework selection without claiming another runtime was qualified."""

import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from test_package_sbom import SBOM
from test_release_packages import make_packages, packages, rewrite_archive


class ReleaseFrameworkTests(unittest.TestCase):
    """Keep framework choice source-owned while retaining archive and closure rejection boundaries."""

    def setUp(self):
        """Create independent future-framework source and archive fixtures without an SDK restore."""
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.repo = self.directory / "source"
        self.repo.mkdir()
        (self.repo / "Directory.Build.props").write_text(
            "<Project><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>")
        (self.repo / "LICENSE").write_bytes((packages.ROOT / "LICENSE").read_bytes())
        self.feed = self.directory / "feed"
        self.feed.mkdir()
        make_packages(self.feed, "11.0.0")
        for archive in self.feed.iterdir():
            rewrite_archive(archive, self.move_framework)

        source = patch.object(packages, "ROOT", self.repo)
        source.start()
        self.addCleanup(source.stop)

    @staticmethod
    def move_framework(entries):
        """Move independently constructed fixture payloads and nuspec groups to the selected target."""
        moved = {name.replace("lib/net10.0/", "lib/net11.0/"):
                 content.replace(b'targetFramework="net10.0"', b'targetFramework="net11.0"')
                 for name, content in entries.items()}
        entries.clear()
        entries.update(moved)

    def test_source_framework_is_read_from_its_own_branch(self):
        """The shared package readers must not silently retain main's previous target framework."""
        # Arrange
        source = self.repo

        # Act
        actual = packages.target_framework(source)

        # Assert
        self.assertEqual("net11.0", actual)

    def test_multiple_source_frameworks_are_rejected(self):
        """A single-line qualification cannot guess between ambiguous target declarations."""
        # Arrange
        (self.repo / "Directory.Build.props").write_text(
            "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
            "<TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>")

        # Act
        with self.assertRaisesRegex(ValueError, "one explicit"):
            packages.target_framework(self.repo)

    def test_conditional_source_framework_is_rejected(self):
        """The source contract cannot depend on an unevaluated producer environment condition."""
        # Arrange
        (self.repo / "Directory.Build.props").write_text(
            '<Project><PropertyGroup Condition="false"><TargetFramework>net11.0</TargetFramework>'
            '</PropertyGroup></Project>')

        # Act
        with self.assertRaisesRegex(ValueError, "one explicit"):
            packages.target_framework(self.repo)

    def test_multi_target_source_frameworks_are_rejected(self):
        """Another source model cannot be silently treated as a single qualified framework."""
        # Arrange
        (self.repo / "Directory.Build.props").write_text(
            '<Project><PropertyGroup><TargetFrameworks>net10.0;net11.0</TargetFrameworks>'
            '</PropertyGroup></Project>')

        # Act
        with self.assertRaisesRegex(ValueError, "one explicit"):
            packages.target_framework(self.repo)

    def test_selected_framework_archive_layout_and_group_are_accepted(self):
        """The same strict readers recognize primary and symbol payloads in another approved source target."""
        # Arrange
        name = packages.PACKAGE_IDS[0]
        primary = packages.archive_entries(self.feed / f"{name}.11.0.0.nupkg")
        symbols = packages.archive_entries(self.feed / f"{name}.11.0.0.snupkg")

        # Act
        packages.verify_layout(primary, name, False)
        packages.verify_layout(symbols, name, True)
        dependencies = packages.dependencies(packages.metadata(primary))

        # Assert
        self.assertEqual({}, dependencies)
        self.assertIn(f"lib/net11.0/{name}.dll", primary)

    def test_old_framework_payload_is_rejected_on_the_new_line(self):
        """A renamed archive or source version cannot authorize a previous framework's binary folder."""
        # Arrange
        name = packages.PACKAGE_IDS[0]
        entries = packages.archive_entries(self.feed / f"{name}.11.0.0.nupkg")
        entries[f"lib/net10.0/{name}.dll"] = entries.pop(f"lib/net11.0/{name}.dll")

        # Act
        with self.assertRaisesRegex(ValueError, "Unexpected package payload"):
            packages.verify_layout(entries, name, False)

    def test_old_framework_dependency_group_is_rejected(self):
        """A matching binary location does not excuse another target's declared dependency contract."""
        # Arrange
        name = packages.PACKAGE_IDS[0]
        entries = packages.archive_entries(self.feed / f"{name}.11.0.0.nupkg")
        entries[f"{name}.nuspec"] = entries[f"{name}.nuspec"].replace(
            b'targetFramework="net11.0"', b'targetFramework="net10.0"')

        # Act
        with self.assertRaisesRegex(ValueError, "net11.0 dependency group"):
            packages.dependencies(packages.metadata(entries))

    def test_sbom_closure_accepts_the_selected_source_framework(self):
        """The isolated restore closure follows the same approved source target as package inspection."""
        # Arrange
        name, version = packages.PACKAGE_IDS[0], "11.0.0"
        assets = self.assets(name, version)

        # Act
        closure = SBOM.dependency_closure(assets, name, version)

        # Assert
        self.assertEqual({(name.lower(), version)}, closure)

    def test_sbom_rejects_a_previous_framework_restore(self):
        """An otherwise matching package graph cannot substitute another line's consumer assets."""
        # Arrange
        name, version = packages.PACKAGE_IDS[0], "11.0.0"
        assets = self.assets(name, version)
        assets["targets"]["net10.0"] = assets["targets"].pop("net11.0")

        # Act
        with self.assertRaisesRegex(ValueError, "net11.0 consumer target"):
            SBOM.dependency_closure(assets, name, version)

    @staticmethod
    def assets(name, version):
        """Describe one exact isolated NuGet package without a project-reference or transitive substitute."""
        return {"targets": {"net11.0": {f"{name}/{version}": {"type": "package"}}},
                "libraries": {f"{name}/{version}": {"type": "package"}},
                "project": {"frameworks": {"net11.0": {"dependencies": {
                    name: {"version": f"[{version}, {version}]"}}}},
                    "restore": {"frameworks": {"net11.0": {"projectReferences": {}}}}}}
