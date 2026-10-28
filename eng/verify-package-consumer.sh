#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 - "$repo_root" "$@" <<'PYTHON'
"""Consume every exact candidate through an independent, selected-package-only cache."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET


root = Path(sys.argv[1])
sys.path.insert(0, str(root / "eng/release"))
from common import CONSUMERS, PROVIDER_PACKAGES
import packages

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--package-dir", type=Path)
parser.add_argument("--version")
parser.add_argument("--output", type=Path)
args = parser.parse_args(sys.argv[2:])
explicit = any(value is not None for value in (args.package_dir, args.version, args.output))

if explicit and not all(value is not None for value in (args.package_dir, args.version, args.output)):
    parser.error("--package-dir, --version, and --output are required together")

output = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix="doka-nestedset-consumer."))
if output.exists() and any(output.iterdir()):
    parser.error("Consumer output must be a new or empty directory; caches cannot be reused")

output.mkdir(parents=True, exist_ok=True)
scratch = tempfile.TemporaryDirectory(prefix="doka-nestedset-consumer-runtime.")
receipt = {"schemaVersion": 1, "mode": "candidate" if explicit else "local-pack", "success": False, "consumers": []}


def save():
    """Keep failed and successful evidence visible without deleting test output."""
    (output / "result.json").write_text(json.dumps(receipt, sort_keys=True, indent=2) + "\n", encoding="ascii")


def run(command, directory, name, environment=None):
    """Bound SDK execution and retain its output even when the process fails."""
    with (directory / (name + ".log")).open("w", encoding="utf-8") as log:
        result = subprocess.run(command, cwd=directory, env=environment, stdout=log, stderr=subprocess.STDOUT,
                                check=False, timeout=900)

    if result.returncode:
        raise ValueError(f"{name} failed ({result.returncode}); see {directory / (name + '.log')}")


def digest(path):
    """Bind restore caches to actual archive bytes rather than declared identity alone."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

try:
    # WHY: The no-argument developer convenience may pack; an explicit RC always uses the supplied sealed bytes.
    if not explicit:
        result = subprocess.run(["dotnet", "msbuild", str(root / "src/Doka.NestedSet/Doka.NestedSet.csproj"),
                                 "-getProperty:PackageVersion", "-verbosity:quiet"], cwd=root,
                                capture_output=True, text=True, check=True, timeout=60)
        args.version = result.stdout.strip()
        args.package_dir = output / "feed"

        for name in packages.PACKAGE_IDS:
            run(["dotnet", "pack", str(root / "src" / name / (name + ".csproj")), "--configuration", "Release",
                 "--no-restore", "--output", str(args.package_dir)], output, "pack-" + name)

    import re

    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?", args.version):
        raise ValueError("Invalid package version")

    package_dir = args.package_dir.resolve()
    archives = {name: package_dir / f"{name}.{args.version}.nupkg" for name in packages.PACKAGE_IDS}
    initial_hashes = {name: digest(path) for name, path in archives.items()}

    for name, archive in archives.items():
        record = packages.metadata(packages.archive_entries(archive))

        if record.findtext("{*}id") != name or record.findtext("{*}version") != args.version:
            raise ValueError("Consumer candidate nuspec identity mismatch")

    receipt["version"] = args.version
    receipt["candidateSha256"] = initial_hashes
    sdk = json.loads((root / "global.json").read_text())["sdk"]["version"]

    # WHY: These are ordinary reviewed C# projects. Copying keeps restore evidence and generated
    # outputs separate from source, while linked FileSystem code remains the actual maintained sample.
    consumers = root / "eng/consumers"
    for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"):
        shutil.copyfile(consumers / name, output / name)

    # WHY: Keep generated projects independently runnable in Rider without producer MSBuild imports or flags.
    properties = ET.parse(output / "Directory.Build.props")
    framework = properties.find(".//TargetFramework")
    if framework is None:
        raise ValueError("Consumer template must declare its source-owned target framework")
    framework.text = packages.target_framework(root)
    receipt["targetFramework"] = framework.text
    expected_framework = framework.text
    properties.write(output / "Directory.Build.props", encoding="unicode")
    shutil.copyfile(root / "global.json", output / "global.json")
    shutil.copytree(consumers / "Shared", output / "Shared")

    for kind, direct_packages in CONSUMERS:
        directory = output / kind
        shutil.copytree(consumers / kind, directory)
        runtime = Path(scratch.name) / kind
        cache = runtime / "packages"
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        ET.SubElement(sources, "add", key="candidate", value=str(package_dir))
        ET.SubElement(sources, "add", key="nuget", value="https://api.nuget.org/v3/index.json")
        mapping = ET.SubElement(config, "packageSourceMapping")
        local = ET.SubElement(mapping, "packageSource", key="candidate")

        for name in packages.PACKAGE_IDS:
            ET.SubElement(local, "package", pattern=name)

        remote = ET.SubElement(mapping, "packageSource", key="nuget")
        ET.SubElement(remote, "package", pattern="*")
        ET.ElementTree(config).write(directory / "NuGet.Config", encoding="unicode")
        environment = os.environ.copy()
        # WHY: Producer-local MSBuild overrides must not redirect this consumer to a shared output/cache
        # or replace its intentionally independent package policy with the repository's central props.
        for name in ("ArtifactsPath", "UseArtifactsOutput", "DirectoryPackagesPropsPath", "RestorePackagesPath",
                     "DirectoryBuildPropsPath", "DirectoryBuildTargetsPath", "CentralPackageTransitivePinningEnabled",
                     "NestedSetReleaseLockRoot", "NuGetLockFilePath", "RestoreLockedMode"):
            environment.pop(name, None)

        environment["NUGET_PACKAGES"] = str(cache)
        environment["NUGET_HTTP_CACHE_PATH"] = str(runtime / "http-cache")
        environment["DOTNET_CLI_HOME"] = str(runtime / "dotnet-home")
        environment["BaseIntermediateOutputPath"] = str(runtime / "obj") + os.sep
        environment["BaseOutputPath"] = str(runtime / "bin") + os.sep
        properties = [f"-p:CandidateVersion={args.version}", f"-p:RepositoryRoot={root}"]
        command = ["dotnet", "restore", "Consumer.csproj", *properties,
                   "--configfile", "NuGet.Config", "--packages", str(cache)]
        run(command, directory, "restore", environment)
        run(command + ["--locked-mode"], directory, "restore-locked", environment)
        assets_path = runtime / "obj/project.assets.json"
        # WHY: Keep restore evidence even when cache binding or subsequent execution rejects the consumer.
        retained_assets = directory / "project.assets.json"
        shutil.copyfile(assets_path, retained_assets)
        assets = json.loads(assets_path.read_text())
        if (set(assets.get("targets", {})) != {expected_framework}
                or set(assets.get("project", {}).get("frameworks", {})) != {expected_framework}):
            raise ValueError("Consumer restored another source line's target framework")
        libraries = assets["libraries"]
        expected_ids = {"Doka.NestedSet"}
        if kind != "core":
            expected_ids.add("Doka.EntityFrameworkCore.NestedSet")
        expected_ids.update(direct_packages)

        # WHY: The isolated roots install one adapter; FileSystem explicitly needs two. No undeclared adapter
        # may hide a missing dependency or accidentally make another provider's registration available.
        restored_candidates = {name for name in packages.PACKAGE_IDS
                               if any(key.rsplit("/", 1)[0].lower() == name.lower() for key in libraries)}
        if restored_candidates != expected_ids:
            raise ValueError("Consumer restored missing or foreign candidate packages")

        restored_native = {name for name in PROVIDER_PACKAGES.values()
                           if any(key.rsplit("/", 1)[0].lower() == name.lower() for key in libraries)}
        expected_native = {PROVIDER_PACKAGES[name] for name in direct_packages if name in PROVIDER_PACKAGES}
        if restored_native != expected_native:
            raise ValueError("Consumer restored missing or foreign native providers")
        restored = {}

        for name in expected_ids:
            identity = f"{name}/{args.version}"
            matches = [key for key in libraries if key.rsplit("/", 1)[0].lower() == name.lower()]

            if matches != [identity]:
                raise ValueError(f"Wrong restored candidate identity for {name}: {matches}")

            cached = cache / name.lower() / args.version.lower() / f"{name}.{args.version}.nupkg".lower()

            if digest(cached) != initial_hashes[name]:
                raise ValueError(f"Restored archive bytes differ for {name}")

            restored[name] = initial_hashes[name]

        if kind == "core" and set(libraries) != {f"Doka.NestedSet/{args.version}"}:
            raise ValueError("Core-only consumer unexpectedly restored external dependencies")

        run_arguments = ["--", "--provider", "sqlite", "--reset"] if kind == "filesystem" else []
        environment["NESTEDSET_SAMPLE_SQLITE_DIRECTORY"] = str(runtime / "sample-data")
        run(["dotnet", "run", "--project", "Consumer.csproj", "--configuration", "Release", "--no-restore",
             *properties, *run_arguments],
            directory, "run", environment)
        lock = directory / "packages.lock.json"

        if not lock.is_file() or not (directory / "run.log").read_text().strip():
            raise ValueError("Consumer execution or lock graph evidence is empty")

        receipt["consumers"].append({"name": kind, "success": True, "sdk": sdk, "candidateSha256": restored,
                                     "assetsSha256": digest(retained_assets), "lockSha256": digest(lock),
                                     "runLogSha256": digest(directory / "run.log")})
        save()

    if {name: digest(path) for name, path in archives.items()} != initial_hashes:
        raise ValueError("Candidate archives changed during consumer verification")

    receipt["success"] = len(receipt["consumers"]) == len(CONSUMERS)
    save()
    print(f"[PASS] All exact-package consumers verified; evidence: {output}")
except (OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
    receipt["error"] = str(error)
    save()
    print(f"[FAIL] {error}; evidence: {output}", file=sys.stderr)
    sys.exit(1)
finally:
    scratch.cleanup()
PYTHON
