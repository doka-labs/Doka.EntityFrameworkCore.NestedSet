"""Exercise exact-candidate shell orchestration with a local SDK process fixture."""

import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from test_release_packages import make_packages

SDK_FIXTURE = r'''#!/usr/bin/env python3
import json
import os
from pathlib import Path
import shutil
import sys
import xml.etree.ElementTree as ET

args = sys.argv[1:]
directory = Path.cwd()
with Path(os.environ["CONSUMER_COMMAND_LOG"]).open("a") as stream:
    stream.write(json.dumps(args) + "\n")

if args[0] == "restore":
    project = ET.parse(directory / "Consumer.csproj")
    reference = project.find(".//PackageVersion")
    name = reference.get("Include")
    version = reference.get("Version").strip("[]")
    feed = Path(ET.parse(directory / "NuGet.Config").find(".//add[@key='candidate']").get("value"))
    ids = ["Doka.NestedSet"] if name == "Doka.NestedSet" else ["Doka.NestedSet", name]
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

    intermediate = Path(os.environ["BaseIntermediateOutputPath"])
    intermediate.mkdir(parents=True, exist_ok=True)
    (intermediate / "project.assets.json").write_text(json.dumps({"libraries": libraries}))
    (directory / "packages.lock.json").write_text(json.dumps({"version": 1, "dependencies": libraries}))
elif args[0] == "run":
    if os.environ.get("CONSUMER_RUN_FAILURE"):
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
        self.command = ["bash", str(Path(__file__).parents[1] / "verify-package-consumer.sh"),
                        "--package-dir", str(self.feed), "--version", "0.1.0-dev", "--output", str(self.output)]

    def execute(self):
        """Run the actual wrapper against only the local process fixture."""
        return subprocess.run(self.command, env=self.environment, capture_output=True, text=True,
                              check=False, timeout=30)

    def test_explicit_candidate_never_repacks_and_uses_two_independent_caches(self):
        """Both consumer roots execute separately against exactly the supplied candidate archives."""
        # Arrange
        expected_commands = ["restore", "restore", "run"] * 2

        # Act
        result = self.execute()

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)
        commands = [json.loads(line)[0] for line in self.command_log.read_text().splitlines()]
        self.assertEqual(expected_commands, commands)
        receipt = json.loads((self.output / "result.json").read_text())
        self.assertTrue(receipt["success"])
        self.assertEqual(["core", "ef"], [row["name"] for row in receipt["consumers"]])
        self.assertTrue((self.output / "core/project.assets.json").is_file())
        self.assertTrue((self.output / "ef/project.assets.json").is_file())
        self.assertEqual([], list(self.output.rglob("*.nupkg")))
        restores = [json.loads(line) for line in self.command_log.read_text().splitlines()
                    if json.loads(line)[0] == "restore"]
        caches = {command[command.index("--packages") + 1] for command in restores}
        self.assertEqual(2, len(caches))
        self.assertTrue(all(not Path(cache).exists() for cache in caches))
        ef_project = (self.output / "ef/Consumer.csproj").read_text()
        self.assertNotIn('PackageReference Include="Doka.NestedSet"', ef_project)

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
