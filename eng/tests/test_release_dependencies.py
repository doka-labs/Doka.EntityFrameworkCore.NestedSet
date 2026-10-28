"""Behavioral controls for exact active-line update targets and retained policies."""

import copy
import unittest
from pathlib import Path

from eng.release import maintenance
from eng.release.common import REPOSITORY, ReleaseError


def policy():
    """Keep the real policy shape while allowing independently activated majors."""
    return {"schemaVersion": 1, "revision": "2026-10-28.1", "repository": REPOSITORY,
            "authorityRef": "refs/heads/main", "lines": [
                {"sourceRef": "refs/heads/main", "role": "main", "packageMajor": 11,
                 "channel": "stable-and-prerelease", "upstreamStage": "ga", "state": "active",
                 "workflowPath": ".github/workflows/release-candidate.yml", "publisherEnvironment": "nuget"},
                {"sourceRef": "refs/heads/maintenance/10.x", "role": "maintenance", "packageMajor": 10,
                 "channel": "stable-and-prerelease", "upstreamStage": "ga", "state": "active",
                 "workflowPath": ".github/workflows/release-candidate.yml", "publisherEnvironment": "nuget"}]}


class DependencyTargetsTests(unittest.TestCase):
    """All active graphs retain the same reviewed grouping and qualified database lines."""

    def setUp(self):
        """Read the explicit retained template rather than reconstructing its groups in tests."""
        self.policy = policy()
        self.template = (Path(__file__).resolve().parents[2] / ".github/dependabot-template.yml").read_text()

    def test_each_active_line_receives_all_four_ecosystems(self):
        """Both maintenance and main keep Docker as well as SDK, NuGet and Action updates."""
        # Arrange
        expected = {(ecosystem, branch) for ecosystem in maintenance.ECOSYSTEMS
                    for branch in ("main", "maintenance/10.x")}

        # Act
        rendered = maintenance.render_dependencies(self.policy, self.template)
        actual = {(ecosystem, branch) for ecosystem, branch, _ in maintenance.dependency_entries(rendered)}

        # Assert
        self.assertEqual(expected, actual)
        self.assertEqual(2, rendered.count("- dependency-name: mysql"))
        self.assertEqual(2, rendered.count("- dependency-name: mariadb"))
        self.assertEqual(2, rendered.count("- dependency-name: postgres"))
        self.assertEqual(4, rendered.count("Doka.EntityFrameworkCore.SafeMigrations.*"))
        self.assertEqual(2, rendered.count("      testcontainers:"))

    def test_coupled_packages_exclude_next_major_including_prereleases(self):
        """Bare EF and both canonical/nonnumeric prerelease floors are retained for every coupled graph."""
        # Arrange
        packages = ("Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.*",
                    "Doka.EntityFrameworkCore.MySql", "Doka.EntityFrameworkCore.SafeMigrations",
                    "Doka.EntityFrameworkCore.SafeMigrations.*", "Npgsql.EntityFrameworkCore.PostgreSQL",
                    "Microsoft.Extensions.*")

        # Act
        rendered = maintenance.render_dependencies(self.policy, self.template)
        entries = maintenance.dependency_entries(rendered)

        # Assert
        for ecosystem, branch, body in entries:
            if ecosystem != "nuget":
                continue

            next_major = 12 if branch == "main" else 11
            for package in packages:
                self.assertIn(f'- dependency-name: "{package}"', body)

            self.assertEqual(len(packages), body.count(f'>={next_major}.0.0-0'))
            self.assertEqual(len(packages), body.count(f'>={next_major}.0.0--'))
            self.assertNotIn('dependency-name: "Microsoft.Data.SqlClient"', body)
            self.assertNotIn('dependency-name: "Microsoft.NET.Test.Sdk"', body)
            self.assertNotIn('dependency-name: "BenchmarkDotNet"', body)

    def test_inactive_and_prepared_lines_receive_no_update_targets(self):
        """Preparation states cannot silently start ongoing dependency work."""
        # Arrange
        self.policy["lines"][0]["state"] = "prepared"
        self.policy["lines"][1]["state"] = "inactive"

        # Act
        rendered = maintenance.render_dependencies(self.policy, self.template)

        # Assert
        self.assertEqual("version: 2\nupdates: []\n", rendered)

    def test_retirement_and_resumption_preserve_template(self):
        """Zero active lines do not delete the canonical grouping/LTS policy."""
        # Arrange
        inactive = copy.deepcopy(self.policy)
        for line in inactive["lines"]:
            line["state"] = "inactive"

        # Act
        retired = maintenance.render_dependencies(inactive, self.template)
        resumed = maintenance.render_dependencies(self.policy, self.template)

        # Assert
        self.assertEqual([], maintenance.dependency_entries(retired))
        self.assertIn("version-update:semver-minor", resumed)
        self.assertIn("      entity-framework:", resumed)
        self.assertEqual(8, len(maintenance.dependency_entries(resumed)))

    def test_changed_generated_groups_are_rejected(self):
        """The check compares policy contents, not merely the presence of branch names."""
        # Arrange
        rendered = maintenance.render_dependencies(self.policy, self.template)
        changed = rendered.replace("interval: weekly", "interval: daily", 1)

        # Act
        with self.assertRaises(ReleaseError) as caught:
            maintenance.check_dependencies(self.policy, changed, self.template)

        # Assert
        self.assertEqual("dependency-targets", caught.exception.code)

    def test_duplicate_or_unknown_template_ecosystem_is_rejected(self):
        """An unrecognized source template cannot silently drop the qualified Docker policy."""
        # Arrange
        changed = self.template.replace("package-ecosystem: docker", "package-ecosystem: nuget")

        # Act
        with self.assertRaises(ReleaseError):
            maintenance.render_dependencies(self.policy, changed)

        # Assert
        self.assertEqual(1, self.template.count("package-ecosystem: docker"))


if __name__ == "__main__":
    unittest.main()
