"""Generate and independently verify SPDX 2.2 for isolated shipping-package consumers."""

import argparse
import base64
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path
from urllib.parse import unquote

SHIPPING_PACKAGES = ("Doka.NestedSet", "Doka.EntityFrameworkCore.NestedSet")
TOOL_VERSION = "4.1.5"
MANIFEST_PATH = Path("_manifest/spdx_2.2/manifest.spdx.json")
FORBIDDEN_PREFIXES = ("xunit", "nunit", "mstest", "testcontainers", "coverlet", "benchmarkdotnet")
FORBIDDEN_PACKAGES = {"microsoft.net.test.sdk", "microsoft.sbom.dotnettool", "sbom.tool"}


def require(condition, message):
    """Reject incomplete evidence with a stable diagnostic instead of a partial success."""
    if not condition:
        raise ValueError(message)


def read_json(path):
    """Read a UTF-8 evidence document."""
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path, value):
    """Persist deterministic, ASCII evidence without suppressing write errors."""
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="ascii")


def sha256(path):
    """Hash actual archive bytes without loading the archive into memory."""
    digest = hashlib.sha256()

    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)

    return digest.hexdigest()


def package_identity(archive):
    """Read the NuGet identity from its nuspec rather than trusting an archive filename."""
    metadata = package_metadata(archive)

    return metadata.findtext("{*}id"), metadata.findtext("{*}version")


def package_metadata(archive):
    """Read the sole embedded nuspec without extracting untrusted archive paths."""
    with zipfile.ZipFile(archive) as package:
        specs = [name for name in package.namelist() if name.endswith(".nuspec") and "/" not in name]
        require(len(specs) == 1, f"Expected one root nuspec in {archive.name}")
        root = ET.fromstring(package.read(specs[0]))

    metadata = root.find("{*}metadata")
    require(metadata is not None, f"Missing nuspec metadata in {archive.name}")

    return metadata


def package_dependencies(archive):
    """Read the shipping package's net10.0 dependency declarations from the actual candidate bytes."""
    dependencies = package_metadata(archive).find("{*}dependencies")
    if dependencies is None:
        return {}

    groups = dependencies.findall("{*}group")
    require(not groups or len(groups) == 1 and groups[0].get("targetFramework") == "net10.0",
            "Shipping nuspec must declare the supported net10.0 dependency group")
    declarations = (groups[0] if groups else dependencies).findall("{*}dependency")
    result = {item.attrib["id"].lower(): item.attrib["version"] for item in declarations}
    require(len(result) == len(declarations), "Duplicate dependency in shipping nuspec")

    return result


def verify_shipping_bindings(assets, archives, cache=None):
    """Bind each restored local shipping identity, dependency declaration and SHA512 to its actual candidate."""
    targets = assets["targets"]["net10.0"]
    libraries = assets["libraries"]

    for name, archive in archives.items():
        identity_name, version = package_identity(archive)
        identities = [key for key in libraries if key.rsplit("/", 1)[0].lower() == name.lower()]
        if not identities:
            continue

        require(len(identities) == 1 and identities[0].rsplit("/", 1)[1] == version,
                f"Restored local package must use the candidate version: {name}")
        identity = identities[0]
        require(identity_name == name, "Local candidate nuspec identity mismatch")
        declared = package_dependencies(archive)
        restored = {key.lower(): value for key, value in targets[identity].get("dependencies", {}).items()}
        require(restored == declared, f"Restored dependency declarations disagree with candidate nuspec: {name}")
        digest = hashlib.sha512()

        with archive.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)

        expected = base64.b64encode(digest.digest()).decode("ascii")
        require(libraries[identity]["sha512"] == expected, f"Restored SHA512 disagrees with candidate bytes: {name}")

        if cache is not None:
            restored_dir = (cache / libraries[identity]["path"]).resolve()
            require(restored_dir.is_relative_to(cache.resolve()), "Restored package path escapes the isolated cache")
            restored_archives = list(restored_dir.glob("*.nupkg"))
            require(len(restored_archives) == 1, f"Missing restored local archive: {name}")
            restored_archive = restored_archives[0]
            require(package_identity(restored_archive) == (name, version), "Restored local nuspec identity mismatch")
            require(sha256(restored_archive) == sha256(archive), f"Restored local archive bytes differ: {name}")


def shipping_archives(package_dir, version):
    """Require exactly the two candidate shipping identities at the requested version."""
    result = {}

    for archive in sorted(package_dir.glob("*.nupkg")):
        name, actual_version = package_identity(archive)
        require(name in SHIPPING_PACKAGES, f"Unexpected shipping archive: {name}")
        require(name not in result, f"Duplicate shipping archive: {name}")
        require(actual_version == version, f"Candidate version mismatch for {name}: {actual_version}")
        result[name] = archive.resolve()

    require(set(result) == set(SHIPPING_PACKAGES), "Both shipping NuGet archives are required")

    return result


def dependency_closure(assets, name, version):
    """Resolve the actual package closure from one isolated consumer's NuGet assets file."""
    require(set(assets["targets"]) == {"net10.0"}, "Expected exactly the net10.0 consumer target")
    dependencies = assets["project"]["frameworks"]["net10.0"]["dependencies"]
    require(set(dependencies) == {name}, "Consumer must reference only its shipping package")
    require(dependencies[name]["version"] == f"[{version}, {version}]", "Consumer version must be exact")
    require(not assets["project"]["restore"]["frameworks"]["net10.0"]["projectReferences"],
            "Consumer must not contain project references")

    target = assets["targets"]["net10.0"]
    libraries = assets["libraries"]
    require(set(target) == set(libraries), "Restore target and package library inventory differ")
    resolved = {}

    for identity, library in libraries.items():
        require(library["type"] == "package", f"Non-package component in consumer: {identity}")
        package, package_version = identity.rsplit("/", 1)
        package = package.lower()
        require(package not in resolved, f"Multiple resolved versions of {package}")
        require(package not in FORBIDDEN_PACKAGES and not package.startswith(FORBIDDEN_PREFIXES),
                f"Test/build tooling leaked into the shipping dependency graph: {package}")
        resolved[package] = (package_version, identity)

    require(resolved.get(name.lower(), (None,))[0] == version, "Shipping package is missing from its own closure")
    pending = [name.lower()]
    visited = set()

    while pending:
        package = pending.pop()
        if package in visited:
            continue

        require(package in resolved, f"Unresolved dependency: {package}")
        visited.add(package)
        pending.extend(key.lower() for key in target[resolved[package][1]].get("dependencies", {}))

    require(visited == set(resolved), "Unreachable components contaminated the isolated consumer")

    return {(package, details[0]) for package, details in resolved.items()}


def verify_manifest(manifest, archive, closure, name, version):
    """Bind SPDX identity, exact NuGet components and file SHA256 to the candidate archive."""
    require(set(package_dependencies(archive)) <= {key for key, _ in closure},
            "Dependency closure omits a dependency declared in the candidate nuspec")
    require(manifest["spdxVersion"] == "SPDX-2.2", "Expected SPDX 2.2")
    require(manifest["name"] == f"{name} {version}", "SPDX document identity/version mismatch")
    roots = [item for item in manifest["packages"] if item["SPDXID"] == "SPDXRef-RootPackage"]
    require(len(roots) == 1, "Expected one SPDX root package")
    root = roots[0]
    require((root["name"], root["versionInfo"]) == (name, version), "SPDX root identity/version mismatch")
    require(manifest["documentDescribes"] == [root["SPDXID"]], "SPDX document describes another root")
    files = manifest["files"]
    require(len(files) == 1, "SPDX must describe exactly one shipping archive")
    require(files[0]["fileName"] == f"./{archive.name}", "SPDX archive filename mismatch")
    hashes = [item["checksumValue"].lower() for item in files[0]["checksums"] if item["algorithm"] == "SHA256"]
    require(hashes == [sha256(archive)], "Shipping archive SHA256 does not match SPDX")
    require(root["hasFiles"] == [files[0]["SPDXID"]], "Root does not contain the shipping archive")
    actual = set()

    for item in manifest["packages"]:
        if item is root:
            continue

        identity = (item["name"].lower(), item["versionInfo"])
        require(identity not in actual, f"Duplicate SPDX component: {identity}")
        expected_purl = f"pkg:nuget/{item['name']}@{item['versionInfo']}".lower()
        refs = [unquote(ref["referenceLocator"]).lower() for ref in item.get("externalRefs", [])
                if ref["referenceType"] == "purl"]
        require(refs == [expected_purl], f"NuGet identity disagrees with purl: {identity}")
        actual.add(identity)

    require(actual == closure,
            f"Dependency closure mismatch; missing={sorted(closure - actual)}, extra={sorted(actual - closure)}")

    return {"package": name, "version": version, "sha256": sha256(archive), "components": len(actual)}


def verify_vendor(report):
    """Require actual hash validation of exactly one file, not only a successful process exit."""
    telemetry = report["Summary"]["ValidationTelemetery"]
    require(report["Result"] == "Success" and report["ValidationErrors"]["Count"] == 0
            and telemetry["TotalFilesInManifest"] == 1 and telemetry["FilesSuccessfulCount"] == 1
            and telemetry["FilesValidatedCount"] == 1 and telemetry["FilesFailedCount"] == 0
            and telemetry["FilesSkippedCount"] == 0,
            "Vendor report did not validate exactly one unmodified shipping archive")


def run_logged(arguments, log, cwd):
    """Keep complete native-tool output and fail visibly when a command fails."""
    with log.open("w", encoding="utf-8") as stream:
        result = subprocess.run(arguments, cwd=cwd, stdout=stream, stderr=subprocess.STDOUT, check=False)

    if result.returncode:
        raise RuntimeError(f"Command failed ({result.returncode}); see {log}")


def prepare_consumer(work, archive, name, version, feed, repo):
    """Restore one exact candidate in a fresh directory and package cache using the repository's pinned SDK."""
    consumer = work / "consumer"
    consumer.mkdir()
    shutil.copyfile(repo / "global.json", consumer / "global.json")
    project = ET.Element("Project", {"Sdk": "Microsoft.NET.Sdk"})
    properties = ET.SubElement(project, "PropertyGroup")

    for key, value in {"TargetFramework": "net10.0", "TreatWarningsAsErrors": "true", "NuGetAudit": "true",
                       "NuGetAuditMode": "all", "NuGetAuditLevel": "low"}.items():
        ET.SubElement(properties, key).text = value

    ET.SubElement(ET.SubElement(project, "ItemGroup"), "PackageReference", {"Include": name, "Version": f"[{version}]"})
    ET.ElementTree(project).write(consumer / "Consumer.csproj", encoding="utf-8", xml_declaration=True)
    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", {"key": "local", "value": str(feed)})
    ET.SubElement(sources, "add", {"key": "nuget", "value": "https://api.nuget.org/v3/index.json"})
    mapping = ET.SubElement(config, "packageSourceMapping")
    local = ET.SubElement(mapping, "packageSource", {"key": "local"})

    for package in SHIPPING_PACKAGES:
        ET.SubElement(local, "package", {"pattern": package})

    ET.SubElement(ET.SubElement(mapping, "packageSource", {"key": "nuget"}), "package", {"pattern": "*"})
    ET.ElementTree(config).write(consumer / "NuGet.Config", encoding="utf-8", xml_declaration=True)
    run_logged(["dotnet", "restore", "Consumer.csproj", "--configfile", "NuGet.Config", "--packages",
                str(work / "packages"), "--disable-parallel", "--disable-build-servers", "-m:1", "/nodeReuse:false",
                "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false",
                "-p:ManagePackageVersionsCentrally=false"], archive / "restore.log", consumer)

    return consumer


def generate(package_dir, output, version, tool, repo):
    """Produce independently validated manifests using isolated consumer restores, never the solution graph."""
    archives = shipping_archives(package_dir, version)
    output.mkdir(parents=True, exist_ok=True)
    require(not any(output.iterdir()), "SBOM output directory must be empty")
    summaries = []

    with tempfile.TemporaryDirectory(prefix="doka-nestedset-sbom-consumers-") as temporary:
        work = Path(temporary)
        feed = work / "feed"
        feed.mkdir()

        for archive in archives.values():
            shutil.copyfile(archive, feed / archive.name)

        for name, source in archives.items():
            package_output = output / name
            drop = package_output / "drop"
            drop.mkdir(parents=True)
            archive = drop / source.name
            shutil.copyfile(source, archive)
            package_work = work / name
            package_work.mkdir()
            consumer = prepare_consumer(package_work, package_output, name, version, feed, repo)
            assets = read_json(consumer / "obj/project.assets.json")
            closure = dependency_closure(assets, name, version)
            verify_shipping_bindings(assets, archives, package_work / "packages")
            write_json(package_output / "restore-assets.json", assets)
            shutil.copyfile(consumer / "Consumer.csproj", package_output / "consumer.csproj")
            write_json(package_output / "closure.json", {"package": name, "version": version,
                       "packages": [{"id": key, "version": value} for key, value in sorted(closure)]})
            run_logged([str(tool), "generate", "-b", str(drop), "-bc", str(consumer), "-m", str(package_output),
                        "-pn", name, "-pv", version, "-ps", "Doka Labs", "-nsb",
                        "https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/sbom", "-mi", "SPDX:2.2"],
                       package_output / "generation.log", package_work)
            run_logged([str(tool), "validate", "-b", str(drop), "-m", str(package_output / "_manifest"),
                        "-o", str(package_output / "validation.json"), "-mi", "SPDX:2.2", "-n"],
                       package_output / "validation.log", package_work)
            verify_vendor(read_json(package_output / "validation.json"))
            manifest = read_json(package_output / MANIFEST_PATH)
            summaries.append(verify_manifest(manifest, archive, closure, name, version))

    summary = {"tool": "Microsoft SBOM Tool", "toolVersion": TOOL_VERSION, "packages": summaries}
    write_json(output / "summary.json", summary)
    lines = [f"{sha256(path)}  {path.relative_to(output).as_posix()}" for path in sorted(output.rglob("*"))
             if path.is_file() and path.name != "SHA256SUMS"]
    (output / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="ascii")

    return summary


def verify(package_dir, output, version):
    """Recheck retained SBOM evidence against the actual archives without invoking external tools."""
    summaries = []
    archives = shipping_archives(package_dir, version)

    for name, archive in archives.items():
        package_output = output / name
        assets = read_json(package_output / "restore-assets.json")
        closure = dependency_closure(assets, name, version)
        verify_shipping_bindings(assets, archives)
        verify_vendor(read_json(package_output / "validation.json"))
        summaries.append(verify_manifest(read_json(package_output / MANIFEST_PATH), archive, closure, name, version))

    return {"packages": summaries}


def main():
    """Expose generation with a verified binary and offline independent verification as separate operations."""
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)

    for command in ("generate", "verify"):
        child = commands.add_parser(command)
        child.add_argument("--package-dir", required=True, type=Path)
        child.add_argument("--output", required=True, type=Path)
        child.add_argument("--version", required=True)
        if command == "generate":
            child.add_argument("--tool", required=True, type=Path)

    args = parser.parse_args()

    try:
        if args.command == "generate":
            result = generate(args.package_dir.resolve(), args.output.resolve(), args.version, args.tool.resolve(),
                              Path(__file__).resolve().parent.parent)
        else:
            result = verify(args.package_dir.resolve(), args.output.resolve(), args.version)

        print(json.dumps(result, indent=2, sort_keys=True))
    except (ValueError, KeyError, OSError, RuntimeError, zipfile.BadZipFile, ET.ParseError) as error:
        print(f"SBOM verification failed: {error}", file=sys.stderr)

        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
