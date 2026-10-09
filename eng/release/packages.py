"""Verify candidate package identity, payload, dependency binding, and Portable PDBs."""

import argparse
import hashlib
import io
import json
import os
import re
import stat
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path, PurePosixPath

try:
    from .common import PACKAGES as PACKAGE_IDS
    from .common import read_json, sha256, write_json
except ImportError:
    from common import PACKAGES as PACKAGE_IDS
    from common import read_json, sha256, write_json

REPOSITORY = "https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet"
ROOT = Path(__file__).resolve().parents[2]
HELPER = ROOT / "eng/tools/Doka.NestedSet.PackageInspection/Doka.NestedSet.PackageInspection.csproj"


class PackageError(ValueError):
    """Reject incomplete or conflicting package evidence."""


def require(condition, message):
    """Keep failures explicit at release trust boundaries."""
    if not condition:
        raise PackageError(message)


def archive_entries(source):
    """Read safe, unique regular ZIP payloads without extracting filesystem paths."""
    entries = {}
    seen = set()

    try:
        with zipfile.ZipFile(io.BytesIO(source) if isinstance(source, bytes) else source) as archive:
            for entry in archive.infolist():
                name = entry.filename
                path = PurePosixPath(name)
                require(name and not path.is_absolute() and "\\" not in name and ":" not in name
                        and all(part not in ("", ".", "..") for part in name.rstrip("/").split("/"))
                        and all(ord(character) >= 32 for character in name), f"Unsafe ZIP path: {name!r}")
                key = name.rstrip("/").casefold()
                require(key not in seen, f"Duplicate or case-colliding ZIP path: {name}")
                seen.add(key)
                mode = entry.external_attr >> 16
                require(stat.S_IFMT(mode) in (0, stat.S_IFREG, stat.S_IFDIR), f"Nonregular ZIP entry: {name}")
                require(not entry.flag_bits & 1, f"Encrypted ZIP entry: {name}")

                if entry.is_dir():
                    continue

                entries[name] = archive.read(entry)
    except (zipfile.BadZipFile, RuntimeError, OSError) as error:
        raise PackageError(f"Unreadable package archive: {error}") from error

    return entries


def canonical_payload(source):
    """Compare every uncompressed byte except NuGet's added signature entry."""
    entries = archive_entries(source)

    # WHY: NuGet repository signing changes ZIP metadata and adds this single entry.
    # Every other name and content remains part of the candidate identity.
    return {name: (len(content), hashlib.sha256(content).hexdigest())
            for name, content in sorted(entries.items()) if name != ".signature.p7s"}


def metadata(entries):
    """Read exactly one root nuspec, rejecting duplicate identity declarations."""
    specs = [name for name in entries if name.endswith(".nuspec") and "/" not in name]
    require(len(specs) == 1, "Expected exactly one root nuspec")

    try:
        root = ET.fromstring(entries[specs[0]])
    except ET.ParseError as error:
        raise PackageError(f"Invalid nuspec: {error}") from error

    records = root.findall("{*}metadata")
    require(len(records) == 1, "Expected one nuspec metadata element")
    result = records[0]

    for field in ("id", "version", "repository", "dependencies"):
        require(len(result.findall("{*}" + field)) == 1, f"Expected one nuspec {field}")

    return result


def dependencies(record):
    """Require one net10.0 dependency group and unambiguous declared dependencies."""
    groups = record.find("{*}dependencies").findall("{*}group")
    require(len(groups) == 1 and groups[0].get("targetFramework") == "net10.0",
            "Expected exactly one net10.0 dependency group")
    items = groups[0].findall("{*}dependency")
    result = {item.get("id"): item.get("version") for item in items}
    require(len(result) == len(items) and None not in result, "Duplicate or missing dependency identity")

    return result


def bounded_dependency_range(value):
    """Normalize only NuGet's comma spacing while retaining exact inclusive/exclusive version bounds."""
    match = re.fullmatch(r"\[([0-9]+\.[0-9]+\.[0-9]+),\s*([0-9]+\.[0-9]+\.[0-9]+)\)", value or "")
    require(match is not None, "EF dependency must declare a bounded inclusive-floor/exclusive-major range")

    return match.groups()


def ef_dependency_contract():
    """Read the shipping project's direct EF references and its source-controlled central version overrides."""
    project = ET.parse(ROOT / f"src/{PACKAGE_IDS[1]}/{PACKAGE_IDS[1]}.csproj")
    direct = {item.get("Include") for item in project.findall(".//PackageReference")}
    configuration = ET.parse(ROOT / "Directory.Packages.props")
    condition = f"'$(MSBuildProjectName)' == '{PACKAGE_IDS[1]}'"
    groups = [group for group in configuration.findall("ItemGroup") if group.get("Condition") == condition]
    require(len(groups) == 1, "Missing unambiguous shipping EF version override group")
    items = groups[0].findall("PackageVersion")
    overrides = {item.get("Update"): item.get("Version") for item in items}
    require(len(overrides) == len(items) and set(overrides) == direct,
            "Shipping EF dependency references and version overrides disagree")

    # WHY: Source builds restore the reviewed EF patch, while published packages allow the validated EF major.
    # Compare the actual packaging overrides, not the development Include versions used by test projects.
    return {name: bounded_dependency_range(version) for name, version in overrides.items()}


def verify_layout(entries, package_id, symbol_package):
    """Allow only the declared framework payload and the ordinary NuGet container metadata."""
    metadata_names = {"_rels/.rels", "[Content_Types].xml", package_id + ".nuspec"}
    core_properties = {name for name in entries if re.fullmatch(
        r"package/services/metadata/core-properties/[a-zA-Z0-9._-]+\.psmdcp", name)}
    require(metadata_names <= entries.keys() and len(core_properties) == 1, "Missing NuGet container metadata")

    if symbol_package:
        payload_names = {f"lib/net10.0/{package_id}.pdb"}
    else:
        payload_names = {"LICENSE", "README.md", f"lib/net10.0/{package_id}.dll", f"lib/net10.0/{package_id}.xml"}

    require(set(entries) <= metadata_names | core_properties | payload_names | {".signature.p7s"},
            "Unexpected package payload")
    require(payload_names <= entries.keys() and all(entries[name] for name in payload_names),
            "Missing primary package payload or symbols")

    if not symbol_package:
        require(entries["LICENSE"] == (ROOT / "LICENSE").read_bytes(), "Packaged license differs from repository license")

        try:
            document = ET.fromstring(entries[f"lib/net10.0/{package_id}.xml"])
        except ET.ParseError as error:
            raise PackageError("Invalid package XML documentation") from error

        require(document.findtext("assembly/name") == package_id and document.find("members") is not None,
                "XML documentation assembly identity mismatch")


def run_inspector(primary, symbols, package_id, expected_version=None):
    """Execute the already-built framework metadata reader with bounded output capture."""
    record = metadata(archive_entries(primary))
    require(record.findtext("{*}id") == package_id, "Inspector package identity mismatch")
    version = record.findtext("{*}version") if expected_version is None else expected_version
    require(isinstance(version, str) and bool(version), "Inspector requires a package version")
    artifacts = Path(os.environ.get("DOKA_NESTEDSET_RELEASE_ARTIFACTS", ROOT / "artifacts"))
    binary = artifacts / "bin" / HELPER.stem / "release" / f"{HELPER.stem}.dll"
    # WHY: Direct execution uses exactly the inspected build and needs no producer-local MSBuild intermediates.
    command = ["dotnet", str(binary), str(primary), str(symbols), package_id, version]
    return subprocess.run(command, cwd=ROOT, capture_output=True, text=True, check=False, timeout=60)


def inspect_symbols(primary, symbols, package_id):
    """Delegate PE and Portable PDB parsing to the .NET framework metadata reader."""
    result = run_inspector(primary, symbols, package_id)
    require(result.returncode == 0, f"Portable PDB verification failed: {result.stderr or result.stdout}")

    try:
        probe = json.loads(result.stdout)
    except json.JSONDecodeError as error:
        raise PackageError("Package inspection did not return a JSON symbol probe") from error

    verify_symbol_probe(probe, package_id)

    return [probe]


def verify_symbol_rejection(package_dir, version):
    """Exercise actual checksum and informational-version guards without changing candidate bytes."""
    name = PACKAGE_IDS[0]
    primary = package_dir / f"{name}.{version}.nupkg"
    symbols = package_dir / f"{name}.{version}.snupkg"
    original = (sha256(primary), sha256(symbols))
    with tempfile.TemporaryDirectory(prefix="nestedset-symbol-negative-") as temporary:
        damaged = Path(temporary) / "damaged.snupkg"
        with zipfile.ZipFile(symbols) as source, zipfile.ZipFile(damaged, "w") as target:
            for entry in source.infolist():
                data = source.read(entry)
                if entry.filename == f"lib/net10.0/{name}.pdb":
                    require(bool(data), "Cannot verify corruption rejection with empty symbols")
                    # WHY: Preserve the PDB identity and change only payload, exercising the checksum guard.
                    data = data[:-1] + bytes([data[-1] ^ 1])

                target.writestr(entry, data)

        result = run_inspector(primary, damaged, name)

    require(result.returncode == 1 and result.stderr.strip() == "Portable PDB checksum does not match the candidate assembly."
            and not result.stdout.strip(), "Malformed-symbol probe was accepted or did not report a controlled failure")
    cases = [{"case": "corrupted-portable-pdb", "exit": result.returncode, "diagnostic": result.stderr.strip()}]
    wrong_version = version + "-wrong-version-probe"
    result = run_inspector(primary, symbols, name, expected_version=wrong_version)
    require(result.returncode == 1
            and result.stderr.strip() == "Assembly informational version does not match its package."
            and not result.stdout.strip(), "Wrong-version probe was accepted or did not report a controlled failure")
    cases.append({"case": "wrong-informational-version", "exit": result.returncode, "diagnostic": result.stderr.strip()})
    require(original == (sha256(primary), sha256(symbols)), "Malformed-symbol probe changed candidate archives")

    return {"schemaVersion": 1, "cases": cases}


def verify_symbol_probe(probe, package_id, content=None):
    """Validate the sealed probe shape and, when available, its actual candidate PDB bytes."""
    expected_name = package_id + ".pdb"
    require(isinstance(probe, dict) and set(probe) == {"pdbName", "checksum", "sha256", "url"}
            and all(isinstance(value, str) for value in probe.values()), "Invalid Portable PDB probe fields")
    require(probe.get("pdbName") == expected_name
            and re.fullmatch(r"SHA256:[0-9a-f]{64}", probe.get("checksum", ""))
            and re.fullmatch(r"[0-9a-f]{64}", probe.get("sha256", ""))
            and re.fullmatch(r"https://symbols\.nuget\.org/download/symbols/" + re.escape(expected_name)
                             + r"/[0-9a-f]{32}FFFFFFFF/" + re.escape(expected_name), probe.get("url", "")),
            "Invalid Portable PDB probe")

    if content is not None:
        require(hashlib.sha256(content).hexdigest() == probe["sha256"], "Portable PDB probe hash mismatch")


def _verify_packages(package_dir, version, source_commit=None, sealed_manifest=None):
    """Share byte and metadata checks between qualification and authenticated manifest readback."""
    require(re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?", version), "Invalid package version")
    require(source_commit is None or re.fullmatch(r"[0-9a-f]{40}", source_commit), "Invalid source commit")
    expected = {f"{package_id}.{version}.{suffix}" for package_id in PACKAGE_IDS for suffix in ("nupkg", "snupkg")}
    require(package_dir.is_dir(), "Package directory does not exist")
    actual = {path.name for path in package_dir.iterdir()
              if sealed_manifest is None or path.suffix.lower() in (".nupkg", ".snupkg")}
    require(actual == expected, f"Expected exactly two primary and two symbol archives; got {sorted(actual)}")
    ef_dependencies = ef_dependency_contract()
    packages = []

    for package_id in PACKAGE_IDS:
        primary = package_dir / f"{package_id}.{version}.nupkg"
        symbols = package_dir / f"{package_id}.{version}.snupkg"
        require(not primary.is_symlink() and not symbols.is_symlink(), "Candidate archives must not be symlinks")
        primary_entries = archive_entries(primary)
        symbol_entries = archive_entries(symbols)
        primary_metadata = metadata(primary_entries)
        symbol_metadata = metadata(symbol_entries)
        verify_layout(primary_entries, package_id, False)
        verify_layout(symbol_entries, package_id, True)

        for record in (primary_metadata, symbol_metadata):
            require(record.findtext("{*}id") == package_id and record.findtext("{*}version") == version,
                    f"Nuspec identity mismatch for {package_id}")
            repository = record.find("{*}repository")
            require(repository.get("type") == "git" and repository.get("url") == REPOSITORY,
                    f"Repository identity mismatch for {package_id}")
            require(source_commit is None or repository.get("commit") == source_commit,
                    f"Repository commit mismatch for {package_id}")

        declared = dependencies(primary_metadata)
        require(declared == dependencies(symbol_metadata), f"Symbol dependency mismatch for {package_id}")

        if package_id == PACKAGE_IDS[0]:
            require(not declared, "Core package must remain dependency-free")
        else:
            binding = declared.pop(PACKAGE_IDS[0], None)
            require(binding in (version, f"[{version}]", f"[{version}, {version}]"),
                    "EF package must bind the candidate Core version")
            require(set(declared) == set(ef_dependencies), "Unexpected EF dependency closure")
            require({name: bounded_dependency_range(value) for name, value in declared.items()} == ef_dependencies,
                    "EF dependency range differs from the shipping contract")

        license_node = primary_metadata.find("{*}license")
        require(license_node is not None and license_node.get("type") == "expression" and license_node.text == "MIT",
                "Package must declare its MIT license")
        require(primary_metadata.findtext("{*}readme") == "README.md", "Package must declare README.md")
        require(symbol_metadata.find("{*}packageTypes/{*}packageType[@name='SymbolsPackage']") is not None,
                "Symbols archive must declare SymbolsPackage")
        row = {"id": package_id, "version": version, "nupkg": primary.name, "snupkg": symbols.name,
               "nupkgSha256": sha256(primary), "snupkgSha256": sha256(symbols)}

        if sealed_manifest is None:
            probes = inspect_symbols(primary, symbols, package_id)
        else:
            sealed = sealed_manifest["packages"][len(packages)]
            require(all(sealed[key] == value for key, value in row.items()),
                    f"Package manifest identity or archive hash mismatch for {package_id}")
            probes = sealed["symbols"]

        require(isinstance(probes, list) and len(probes) == 1, "Expected one Portable PDB probe per package")
        verify_symbol_probe(probes[0], package_id, symbol_entries[f"lib/net10.0/{package_id}.pdb"])
        packages.append({**row, "symbols": probes})

    return {"schemaVersion": 1, "packages": packages}


def verify_packages(package_dir: Path, version: str, source_commit: str | None = None,
                    output: Path | None = None) -> dict:
    """Bind both primary and symbol packages to one exact candidate version and optional source SHA."""
    receipt = _verify_packages(Path(package_dir), version, source_commit)

    if output is not None:
        write_json(output, receipt)

    return receipt


def verified_manifest(package_dir, version, manifest, source_commit=None):
    """Verify sealed package bytes without rebuilding the qualification metadata helper.

    The caller must authenticate this manifest as a qualified release subject before publication.
    Its GUID/checksum probes were derived by the qualification helper; this path checks their
    structure and raw PDB hash while revalidating all archive identities, payloads, and hashes.
    """
    try:
        value = manifest if isinstance(manifest, dict) else read_json(manifest)
    except (ValueError, OSError) as error:
        raise PackageError(f"Unreadable package manifest: {error}") from error
    require(isinstance(value, dict) and set(value) == {"schemaVersion", "packages"}
            and type(value.get("schemaVersion")) is int and value["schemaVersion"] == 1,
            "Unsupported package manifest schema")
    rows = value["packages"]
    require(isinstance(rows, list) and len(rows) == len(PACKAGE_IDS), "Incomplete package manifest inventory")
    fields = {"id", "version", "nupkg", "snupkg", "nupkgSha256", "snupkgSha256", "symbols"}

    for package_id, row in zip(PACKAGE_IDS, rows):
        require(isinstance(row, dict) and set(row) == fields, "Invalid package manifest fields")
        require(row["id"] == package_id and row["version"] == version, "Package manifest identity/version mismatch")

        for extension in ("nupkg", "snupkg"):
            require(row[extension] == f"{package_id}.{version}.{extension}", "Package manifest filename mismatch")
            digest = row[extension + "Sha256"]
            require(isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest), "Invalid package manifest hash")

    # WHY: Candidate artifact roots also contain their authenticated manifest, SBOMs, and qualification evidence.
    # The caller verifies that outer inventory; this function requires exactly the four package archives within it.
    return _verify_packages(Path(package_dir), version, source_commit, value)


def main():
    """Expose candidate validation without performing restore, build, pack, or publication."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package-dir", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--source-commit")
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    verify_packages(arguments.package_dir, arguments.version, arguments.source_commit, arguments.output)


if __name__ == "__main__":
    try:
        main()
    except (PackageError, OSError, subprocess.SubprocessError) as error:
        print(f"[FAIL] {error}", file=sys.stderr)
        sys.exit(1)
