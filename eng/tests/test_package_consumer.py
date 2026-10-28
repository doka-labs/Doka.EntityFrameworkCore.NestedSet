"""Exercise exact-candidate shell orchestration with a local SDK process fixture."""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from test_release_packages import make_packages
from eng.release.common import CONSUMERS

SDK_FIXTURE = r'''#!/usr/bin/env python3
import json
import os
from pathlib import Path
import shutil
import sys
import xml.etree.ElementTree as ET

args = sys.argv[1:]
directory = Path.cwd()
for name in ("ArtifactsPath", "UseArtifactsOutput", "DirectoryPackagesPropsPath", "RestorePackagesPath",
             "DirectoryBuildPropsPath", "DirectoryBuildTargetsPath", "CentralPackageTransitivePinningEnabled",
             "NestedSetReleaseLockRoot", "NuGetLockFilePath", "RestoreLockedMode"):
    if name in os.environ:
        print("Producer override leaked into the consumer", name)
        sys.exit(92)

with Path(os.environ["CONSUMER_COMMAND_LOG"]).open("a") as stream:
    stream.write(json.dumps(args) + "\n")

if args[0] == "restore":
    project = ET.parse(directory / "Consumer.csproj")
    references = project.findall(".//PackageReference")
    reference = references[0]
    name = reference.get("Include")
    version = next(value.split("=", 1)[1] for value in args if value.startswith("-p:CandidateVersion="))
    feed = Path(ET.parse(directory / "NuGet.Config").find(".//add[@key='candidate']").get("value"))
    ids = ["Doka.NestedSet"]
    if name != "Doka.NestedSet":
        ids.append("Doka.EntityFrameworkCore.NestedSet")
    for item in references:
        if item.get("Include") not in ids:
            ids.append(item.get("Include"))
    if directory.name == "mysql" and os.environ.get("CONSUMER_FOREIGN_ADAPTER"):
        ids.append("Doka.EntityFrameworkCore.NestedSet.Postgres")
    libraries = {}

    for package in ids:
        source = feed / f"{package}.{version}.nupkg"
        target = Path(os.environ["NUGET_PACKAGES"]) / package.lower() / version / source.name.lower()
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)

        if os.environ.get("CONSUMER_TAMPER"):
            with target.open("ab") as stream:
                stream.write(b"stale bytes")

        libraries[f"{package}/{version}"] = {"type": "package"}

    providers = {
        "mysql": "Doka.EntityFrameworkCore.MySql",
        "postgres": "Npgsql.EntityFrameworkCore.PostgreSQL",
        "sqlite": "Microsoft.EntityFrameworkCore.Sqlite",
        "sqlserver": "Microsoft.EntityFrameworkCore.SqlServer",
    }
    if directory.name in providers:
        libraries[providers[directory.name] + "/10.0.0"] = {"type": "package"}
    if directory.name == "filesystem":
        libraries["Doka.EntityFrameworkCore.MySql/10.0.0"] = {"type": "package"}
        libraries["Microsoft.EntityFrameworkCore.Sqlite/10.0.0"] = {"type": "package"}
    if directory.name == "mysql" and os.environ.get("CONSUMER_FOREIGN_PROVIDER"):
        libraries["Npgsql.EntityFrameworkCore.PostgreSQL/10.0.0"] = {"type": "package"}

    intermediate = Path(os.environ["BaseIntermediateOutputPath"])
    intermediate.mkdir(parents=True, exist_ok=True)
    framework = ET.parse(directory.parent / "Directory.Build.props").findtext(".//TargetFramework")
    if os.environ.get("CONSUMER_WRONG_FRAMEWORK"):
        framework = "net999.0"
    (intermediate / "project.assets.json").write_text(json.dumps({"libraries": libraries,
        "targets": {framework: {}}, "project": {"frameworks": {framework: {}}}}))
    (directory / "packages.lock.json").write_text(json.dumps({"version": 1, "dependencies": libraries}))
elif args[0] == "run":
    if os.environ.get("CONSUMER_RUN_FAILURE") or os.environ.get("CONSUMER_RUN_FAILURE_KIND") == directory.name:
        print("Consumer fixture failed")
        sys.exit(42)

    print("Consumer fixture completed")
else:
    print("Unexpected SDK command", args)
    sys.exit(91)
'''


class ConsumerTests(unittest.TestCase):
    """Check orchestration and failure evidence without restoring or running .NET."""

    def setUp(self):
        """Create real candidate ZIPs and a hermetic executable SDK fixture."""
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.feed = self.directory / "feed"
        self.feed.mkdir()
        make_packages(self.feed)
        self.output = self.directory / "result"
        tools = self.directory / "tools"
        tools.mkdir()
        sdk = tools / "dotnet"
        sdk.write_text(SDK_FIXTURE, encoding="ascii")
        sdk.chmod(0o755)
        self.command_log = self.directory / "commands.jsonl"
        self.environment = os.environ.copy()
        self.environment["PATH"] = str(tools) + os.pathsep + self.environment["PATH"]
        self.environment["CONSUMER_COMMAND_LOG"] = str(self.command_log)
        self.environment["DirectoryPackagesPropsPath"] = str(self.directory / "wrong-production-policy.props")
        self.environment["RestorePackagesPath"] = str(self.directory / "stale-shared-cache")
        self.environment["DirectoryBuildPropsPath"] = str(self.directory / "wrong-producer.props")
        self.environment["DirectoryBuildTargetsPath"] = str(self.directory / "wrong-producer.targets")
        self.environment["CentralPackageTransitivePinningEnabled"] = "true"
        self.command = ["bash", str(Path(__file__).parents[1] / "verify-package-consumer.sh"),
                        "--package-dir", str(self.feed), "--version", "0.1.0-dev", "--output", str(self.output)]

    def execute(self):
        """Run the actual wrapper against only the local process fixture."""
        return subprocess.run(self.command, env=self.environment, capture_output=True, text=True,
                              check=False, timeout=30)

    def test_candidate_templates_are_not_unparameterized_restore_projects(self):
        """Automatic project discovery must not restore inputs with deliberately unbound candidate properties."""
        # Arrange
        source = Path(__file__).parents[1] / "consumers"
        expected = {source / name / "Consumer.csproj.template" for name, _ in CONSUMERS}

        # Act
        projects = set(source.rglob("*.csproj"))
        templates = set(source.rglob("*.csproj.template"))

        # Assert
        self.assertEqual(set(), projects)
        self.assertEqual(expected, templates)

    def test_materialized_projects_preserve_reviewed_candidate_templates(self):
        """Only the output filename changes; exact candidate references and guards are not rewritten."""
        # Arrange
        source = Path(__file__).parents[1] / "consumers"

        # Act
        result = self.execute()

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)
        for name, _ in CONSUMERS:
            self.assertEqual((source / name / "Consumer.csproj.template").read_bytes(),
                             (self.output / name / "Consumer.csproj").read_bytes())

        self.assertEqual([], list(self.output.rglob("*.template")))
        self.assertEqual((source / "Directory.Build.targets").read_bytes(),
                         (self.output / "Directory.Build.targets").read_bytes())

    def test_explicit_candidate_never_repacks_and_uses_independent_selected_package_caches(self):
        """Every shipping package executes separately against its own exact candidate dependency closure."""
        # Arrange
        expected_commands = ["restore", "restore", "run"] * len(CONSUMERS)

        # Act
        result = self.execute()

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)
        commands = [json.loads(line)[0] for line in self.command_log.read_text().splitlines()]
        self.assertEqual(expected_commands, commands)
        receipt = json.loads((self.output / "result.json").read_text())
        self.assertTrue(receipt["success"])
        self.assertEqual([name for name, _ in CONSUMERS], [row["name"] for row in receipt["consumers"]])
        for name, package_ids in CONSUMERS:
            self.assertTrue((self.output / name / "project.assets.json").is_file())
            project = ET.parse(self.output / name / "Consumer.csproj")
            references = project.findall(".//PackageReference")
            self.assertEqual([(package_id, "[$(CandidateVersion)]") for package_id in package_ids],
                             [(item.get("Include"), item.get("Version")) for item in references])
            self.assertEqual([], project.findall(".//ProjectReference"))

        properties = (self.output / "Directory.Packages.props").read_text()
        self.assertIn("<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>", properties)
        self.assertNotIn("TransitivePinning", properties)
        self.assertTrue(all("-p:CandidateVersion=0.1.0-dev" in json.loads(line)
                            for line in self.command_log.read_text().splitlines()))

        self.assertEqual([], list(self.output.rglob("*.nupkg")))
        restores = [json.loads(line) for line in self.command_log.read_text().splitlines()
                    if json.loads(line)[0] == "restore"]
        caches = {command[command.index("--packages") + 1] for command in restores}
        self.assertEqual(len(CONSUMERS), len(caches))
        self.assertTrue(all(not Path(cache).exists() for cache in caches))
        ef_project = (self.output / "ef/Consumer.csproj").read_text()
        self.assertNotIn('PackageReference Include="Doka.NestedSet"', ef_project)

    def test_existing_sample_is_linked_without_duplicate_source_or_project_dependencies(self):
        """The full packed sample compiles maintained source through an ordinary consumer project."""
        # Arrange
        root = Path(__file__).parents[2]

        # Act
        result = self.execute()

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)
        project = ET.parse(self.output / "filesystem/Consumer.csproj")
        self.assertEqual([], project.findall(".//ProjectReference"))
        self.assertEqual(["$(RepositoryRoot)/samples/FileSystem/**/*.cs", "$(RepositoryRoot)/samples/Shared/*.cs"],
                         [node.get("Include") for node in project.findall(".//Compile")])
        self.assertEqual([], list((self.output / "filesystem").rglob("*.cs")))
        self.assertIn(f"-p:RepositoryRoot={root}",
                      json.loads(self.command_log.read_text().splitlines()[-1]))

    def test_consumer_uses_its_selected_source_framework(self):
        """The real wrapper renders another branch's target without requiring producer property overrides."""
        # Arrange
        root = Path(__file__).parents[2]
        source = self.directory / "source"
        (source / "eng").mkdir(parents=True)
        shutil.copyfile(root / "eng/verify-package-consumer.sh", source / "eng/verify-package-consumer.sh")
        shutil.copytree(root / "eng/release", source / "eng/release", ignore=shutil.ignore_patterns("__pycache__"))
        shutil.copytree(root / "eng/consumers", source / "eng/consumers")
        shutil.copyfile(root / "global.json", source / "global.json")
        (source / "Directory.Build.props").write_text(
            '<Project><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>')
        self.command[1] = str(source / "eng/verify-package-consumer.sh")

        # Act
        result = self.execute()

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("net11.0", ET.parse(self.output / "Directory.Build.props").findtext(".//TargetFramework"))
        receipt = json.loads((self.output / "result.json").read_text())
        self.assertEqual("net11.0", receipt["targetFramework"])
        self.assertTrue(receipt["success"])

    def test_consumer_rejects_another_framework_restore_before_execution(self):
        """Matching package identities cannot hide assets restored for another line's target."""
        # Arrange
        self.environment["CONSUMER_WRONG_FRAMEWORK"] = "1"

        # Act
        result = self.execute()

        # Assert
        self.assertNotEqual(0, result.returncode)
        receipt = json.loads((self.output / "result.json").read_text())
        self.assertFalse(receipt["success"])
        self.assertIn("another source line's target framework", receipt["error"])
        commands = [json.loads(line)[0] for line in self.command_log.read_text().splitlines()]
        self.assertEqual(["restore", "restore"], commands)

    def test_invalid_candidate_version_is_rejected_before_sdk_execution(self):
        """Ordinary MSBuild properties never turn untrusted input into a floating package selection."""
        # Arrange
        self.command[self.command.index("--version") + 1] = "10.*"

        # Act
        result = self.execute()

        # Assert
        self.assertNotEqual(0, result.returncode)
        self.assertFalse(self.command_log.exists())
        self.assertIn("Invalid package version", json.loads((self.output / "result.json").read_text())["error"])

    def test_foreign_adapter_cannot_hide_in_selected_provider_consumer(self):
        """A passing all-provider build cannot replace the selected adapter's isolated dependency closure."""
        # Arrange
        self.environment["CONSUMER_FOREIGN_ADAPTER"] = "1"

        # Act
        result = self.execute()

        # Assert
        receipt = json.loads((self.output / "result.json").read_text())
        self.assertNotEqual(0, result.returncode)
        self.assertFalse(receipt["success"])
        self.assertIn("foreign candidate packages", receipt["error"])
        self.assertEqual(["core", "ef"], [row["name"] for row in receipt["consumers"]])

    def test_foreign_native_provider_is_rejected_before_execution(self):
        """An adapter consumer cannot obtain its configuration from a second provider's package."""
        # Arrange
        self.environment["CONSUMER_FOREIGN_PROVIDER"] = "1"

        # Act
        result = self.execute()

        # Assert
        receipt = json.loads((self.output / "result.json").read_text())
        self.assertNotEqual(0, result.returncode)
        self.assertFalse(receipt["success"])
        self.assertIn("foreign native providers", receipt["error"])

    def test_packed_sample_failure_does_not_report_partial_success(self):
        """The actual packed sample remains required after all six isolated package probes passed."""
        # Arrange
        self.environment["CONSUMER_RUN_FAILURE_KIND"] = "filesystem"

        # Act
        result = self.execute()

        # Assert
        receipt = json.loads((self.output / "result.json").read_text())
        self.assertNotEqual(0, result.returncode)
        self.assertFalse(receipt["success"])
        self.assertEqual([name for name, _ in CONSUMERS[:-1]], [row["name"] for row in receipt["consumers"]])
        self.assertIn("run failed", receipt["error"])

    def test_restored_cache_byte_mismatch_fails_before_execution(self):
        """A stale same-version cache cannot produce a passing package consumer receipt."""
        # Arrange
        self.environment["CONSUMER_TAMPER"] = "1"

        # Act
        result = self.execute()

        # Assert
        self.assertNotEqual(0, result.returncode)
        receipt = json.loads((self.output / "result.json").read_text())
        self.assertFalse(receipt["success"])
        self.assertIn("archive bytes differ", receipt["error"])
        self.assertEqual([], receipt["consumers"])

    def test_failed_execution_retains_failure_receipt_and_log(self):
        """A failed runtime sample leaves evidence and never emits a vacuous success."""
        # Arrange
        self.environment["CONSUMER_RUN_FAILURE"] = "1"

        # Act
        result = self.execute()

        # Assert
        self.assertNotEqual(0, result.returncode)
        receipt = json.loads((self.output / "result.json").read_text())
        self.assertFalse(receipt["success"])
        self.assertIn("run failed", receipt["error"])
        self.assertIn("fixture failed", (self.output / "core/run.log").read_text())

    def test_existing_output_cannot_reuse_a_previous_cache(self):
        """Reusing an output root must fail before any restore can consult existing package bytes."""
        # Arrange
        self.output.mkdir()
        (self.output / "stale").write_text("previous execution")

        # Act
        result = self.execute()

        # Assert
        self.assertNotEqual(0, result.returncode)
        self.assertIn("caches cannot be reused", result.stderr)
        self.assertFalse(self.command_log.exists())


if __name__ == "__main__":
    unittest.main()
