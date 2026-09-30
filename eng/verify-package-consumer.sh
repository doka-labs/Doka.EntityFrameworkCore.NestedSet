#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 - "$repo_root" "$@" <<'PYTHON'
"""Consume exact candidate archives through two independent fresh package caches."""
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

        for name in ("Doka.NestedSet", "Doka.EntityFrameworkCore.NestedSet"):
            run(["dotnet", "pack", str(root / "src" / name / (name + ".csproj")), "--configuration", "Release",
                 "--no-restore", "--output", str(args.package_dir)], output, "pack-" + name)

    sys.path.insert(0, str(root / "eng/release"))
    import packages
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

    for kind, package_id in (("core", "Doka.NestedSet"), ("ef", "Doka.EntityFrameworkCore.NestedSet")):
        directory = output / kind
        directory.mkdir()
        runtime = Path(scratch.name) / kind
        cache = runtime / "packages"
        shutil.copyfile(root / "global.json", directory / "global.json")
        shutil.copyfile(root / "Directory.Packages.props", directory / "Directory.Packages.props")

        # WHY: An output beneath this checkout must not inherit the repository's project/artifact settings.
        (directory / "Directory.Build.props").write_text("<Project />\n", encoding="ascii")
        (directory / "Directory.Build.targets").write_text("<Project />\n", encoding="ascii")
        project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
        properties = ET.SubElement(project, "PropertyGroup")

        for name, value in {"OutputType": "Exe", "TargetFramework": "net10.0", "Nullable": "enable",
                            "ImplicitUsings": "disable", "TreatWarningsAsErrors": "true", "NuGetAudit": "true",
                            "NuGetAuditMode": "all", "NuGetAuditLevel": "low",
                            "RestorePackagesWithLockFile": "true"}.items():
            ET.SubElement(properties, name).text = value

        items = ET.SubElement(project, "ItemGroup")
        ET.SubElement(items, "PackageVersion", Include=package_id, Version=f"[{args.version}]")
        ET.SubElement(items, "PackageReference", Include=package_id)

        if kind == "ef":
            ET.SubElement(items, "PackageReference", Include="Microsoft.EntityFrameworkCore.Sqlite")
            ET.SubElement(items, "PackageReference", Include="Doka.EntityFrameworkCore.MySql")

            # WHY: Compile the actual sample and its provider migrations against the candidate package, not a source reference.
            for relative in ("samples/FileSystem", "samples/Shared"):
                sample_root = root / relative

                for source in sample_root.rglob("*.cs"):
                    target = directory / sample_root.name / source.relative_to(sample_root)
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(source, target)
        else:
            (directory / "Program.cs").write_text(
                'var root = new Doka.NestedSet.NestedSetBounds(1, 6);\n'
                'var child = new Doka.NestedSet.NestedSetBounds(2, 3);\n\n'
                'if (!root.Contains(child) || root.DescendantCount != 2 || !child.IsLeaf)\n'
                '{\n    throw new System.InvalidOperationException("Core package bounds contract failed.");\n}\n\n'
                'await System.Console.Out.WriteLineAsync(\n'
                '    System.MemoryExtensions.AsMemory("Core package bounds and containment verified."),\n'
                '    System.Threading.CancellationToken.None);\n', encoding="ascii")

        ET.ElementTree(project).write(directory / "Consumer.csproj", encoding="unicode")
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
        environment["NUGET_PACKAGES"] = str(cache)
        environment["NUGET_HTTP_CACHE_PATH"] = str(runtime / "http-cache")
        environment["DOTNET_CLI_HOME"] = str(runtime / "dotnet-home")
        environment["BaseIntermediateOutputPath"] = str(runtime / "obj") + os.sep
        environment["BaseOutputPath"] = str(runtime / "bin") + os.sep
        command = ["dotnet", "restore", "Consumer.csproj", "--configfile", "NuGet.Config", "--packages", str(cache)]
        run(command, directory, "restore", environment)
        run(command + ["--locked-mode"], directory, "restore-locked", environment)
        assets_path = runtime / "obj/project.assets.json"
        # WHY: Keep restore evidence even when cache binding or subsequent execution rejects the consumer.
        retained_assets = directory / "project.assets.json"
        shutil.copyfile(assets_path, retained_assets)
        assets = json.loads(assets_path.read_text())
        libraries = assets["libraries"]
        expected_ids = {"Doka.NestedSet"} if kind == "core" else set(packages.PACKAGE_IDS)
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

        run_arguments = ["--", "--provider", "sqlite", "--reset"] if kind == "ef" else []
        environment["NESTEDSET_SAMPLE_SQLITE_DIRECTORY"] = str(runtime / "sample-data")
        run(["dotnet", "run", "--project", "Consumer.csproj", "--configuration", "Release", "--no-restore",
             *run_arguments],
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

    receipt["success"] = len(receipt["consumers"]) == 2
    save()
    print(f"[PASS] Both exact-package consumers verified; evidence: {output}")
except (OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
    receipt["error"] = str(error)
    save()
    print(f"[FAIL] {error}; evidence: {output}", file=sys.stderr)
    sys.exit(1)
finally:
    scratch.cleanup()
PYTHON
