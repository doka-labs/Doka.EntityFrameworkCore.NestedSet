"""Read-only NuGet availability and signed package/public Portable PDB verification."""

import argparse
import hashlib
import json
import re
import subprocess
import sys
import time
from http.client import HTTPException
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import Request, urlopen

try:
    from . import packages
except ImportError:
    import packages


INDEX = "https://api.nuget.org/v3/index.json"
REPOSITORY = "doka-labs/Doka.EntityFrameworkCore.NestedSet"


class NuGetError(ValueError):
    """Fail closed when remote state conflicts with the candidate."""


class PendingError(NuGetError):
    """Represent temporarily unavailable or unsigned publication state."""


def fetch(url, headers, timeout):
    """Read one bounded HTTPS response, retaining status separately from content."""
    if urlsplit(url).scheme != "https":
        raise NuGetError("Only HTTPS NuGet endpoints are allowed")

    try:
        deadline = time.monotonic() + timeout
        request = Request(url, headers={"User-Agent": "Doka.NestedSet.Release/1", **headers})

        with urlopen(request, timeout=timeout) as response:
            if urlsplit(response.url).scheme != "https":
                raise NuGetError("NuGet redirected outside HTTPS")

            content = bytearray()

            # WHY: read() may wait forever on a slow trickle of bytes despite a socket inactivity timeout.
            # Read available chunks and recheck the elapsed deadline between them.
            while time.monotonic() < deadline:
                block = response.read1(1024 * 1024)

                if not block:
                    if response.length not in (None, 0):
                        return 0, b""

                    return response.status, bytes(content)

                content.extend(block)

            return 0, b""
    except HTTPError as error:
        return error.code, b""
    except (URLError, TimeoutError, ConnectionError, OSError, HTTPException):
        return 0, b""


def require_response(status, description):
    """Distinguish bounded eventual consistency from terminal protocol errors."""
    if status == 200:
        return

    if status in (0, 404, 408, 429) or 500 <= status <= 599:
        raise PendingError(f"Pending {description}: HTTP {status}")

    raise NuGetError(f"Rejected {description}: HTTP {status}")


def package_base(fetcher, timeout):
    """Discover the official flat-container resource rather than assuming service-index structure."""
    status, content = fetcher(INDEX, {}, timeout)
    require_response(status, "NuGet service index")

    try:
        resources = json.loads(content)["resources"]
        matches = {item["@id"] for item in resources if item.get("@type") == "PackageBaseAddress/3.0.0"}
    except (ValueError, KeyError, TypeError) as error:
        raise NuGetError("Invalid NuGet service index") from error

    if len(matches) != 1:
        raise NuGetError("Expected one NuGet PackageBaseAddress/3.0.0 resource")

    address = matches.pop()
    parsed = urlsplit(address)

    if parsed.scheme != "https" or parsed.hostname != "api.nuget.org" or parsed.username or parsed.password \
            or parsed.port not in (None, 443) or parsed.query or parsed.fragment:
        raise NuGetError("Unexpected NuGet package endpoint")

    return address.rstrip("/") + "/"


def package_url(base, package):
    """Address the exact lowercase public NuGet identity."""
    name = package["id"].lower()
    version = package["version"].lower()

    return f"{base}{name}/{version}/{name}.{version}.nupkg"


def canonical_sha256(payload):
    """Hash the versioned sorted name/size/content inventory used for canonical equality."""
    encoded = json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("ascii")

    return hashlib.sha256(b"doka-nestedset-nuget-payload-v1\0" + encoded).hexdigest()


def matching_payload(content, candidate):
    """Reject remote collisions while allowing only NuGet signature metadata differences."""
    expected = packages.canonical_payload(candidate)
    observed = packages.canonical_payload(content)

    if observed != expected:
        raise NuGetError(f"Public package payload conflicts with candidate: {candidate.name}")

    return canonical_sha256(observed)


def check_availability(package_dir, version, allow_matching=False, *, manifest=None, fetcher=fetch):
    """Require all IDs absent initially; retries may retain only exact matching public payloads."""
    inventory = (packages.verify_packages(Path(package_dir), version) if manifest is None
                 else packages.verified_manifest(package_dir, version, manifest))
    base = package_base(fetcher, 30)
    states = []

    for package in inventory["packages"]:
        status, content = fetcher(package_url(base, package), {}, 30)

        if status == 404:
            states.append({"id": package["id"], "version": version, "state": "absent"})
            continue

        if status == 200 and allow_matching:
            digest = matching_payload(content, Path(package_dir) / package["nupkg"])
            states.append({"id": package["id"], "version": version, "state": "matching", "canonicalSha256": digest})
            continue

        # WHY: A transient or authorization failure cannot prove that a new immutable version is unused.
        raise NuGetError(f"Cannot establish unused version for {package['id']}: HTTP {status}")

    return {"schemaVersion": 1, "version": version, "packages": states}


def check_unused_version(version, *, fetcher=fetch):
    """Check both future package identities before candidate production without requiring package files."""
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?", version):
        raise NuGetError("Invalid package version")

    base = package_base(fetcher, 30)
    states = []

    for package_id in packages.PACKAGE_IDS:
        status, _ = fetcher(package_url(base, {"id": package_id, "version": version}), {}, 30)

        if status != 404:
            raise NuGetError(f"Cannot establish unused version for {package_id}: HTTP {status}")

        states.append({"id": package_id, "version": version, "state": "absent"})

    return {"schemaVersion": 1, "version": version, "packages": states}


def verify_signature(path, log, timeout):
    """Verify the exact retained public response using the SDK's NuGet signature verifier."""
    result = subprocess.run(["dotnet", "nuget", "verify", str(path), "--all", "--verbosity", "normal"],
                            capture_output=True, text=True, check=False, timeout=timeout)
    log.write_text(result.stdout + result.stderr, encoding="utf-8")

    if result.returncode:
        raise NuGetError(f"Public package signature verification failed: {path.name}; see {log}")


def readback(package_dir, version, output, timeout_seconds=7200, *, manifest=None, fetcher=fetch,
             signature_verifier=verify_signature, clock=time.monotonic, sleep=time.sleep, poll_seconds=10):
    """Observe signed primary packages and checksum-validated public symbols within one bounded deadline."""
    if not 0 < timeout_seconds <= 7200 or not 0 < poll_seconds <= 60:
        raise NuGetError("Readback timeout must be 1..7200 seconds and polling interval at most 60 seconds")

    package_dir = Path(package_dir)
    output = Path(output)
    payload_dir = output.parent / (output.stem + "-payloads")
    payload_dir.mkdir(parents=True, exist_ok=True)
    started = clock()
    deadline = started + timeout_seconds
    observed = {}
    events = []
    base = None

    def remaining():
        """Cap every network or subprocess operation by the shared completion deadline."""
        value = deadline - clock()

        if value <= 0:
            raise PendingError("NuGet readback deadline exhausted")

        return max(0.001, min(30, value))

    def receipt(success, error=None):
        """Retain partial observations on failure without marking missing subjects as verified."""
        # WHY: NuGet may index the dependent package first; publication binds receipts in dependency order.
        result = {"schemaVersion": 1, "repository": REPOSITORY, "version": version, "success": success,
                  "packages": [observed[name] for name in packages.PACKAGE_IDS if name in observed], "events": events}

        if error:
            result["error"] = str(error)

        packages.write_json(output, result)

        return result

    try:
        receipt(False)
        inventory = (packages.verify_packages(package_dir, version) if manifest is None
                     else packages.verified_manifest(package_dir, version, manifest))

        while clock() < deadline:
            pending = []

            if base is None:
                try:
                    base = package_base(fetcher, remaining())
                except PendingError as error:
                    pending.append(str(error))

            if base is not None:
                for package in inventory["packages"]:
                    row = observed.get(package["id"])

                    if row is None:
                        try:
                            status, content = fetcher(package_url(base, package), {}, remaining())
                            require_response(status, package["id"])
                            digest = matching_payload(content, package_dir / package["nupkg"])

                            if not packages.archive_entries(content).get(".signature.p7s"):
                                raise PendingError(f"Repository signature pending for {package['id']}")

                            public_path = payload_dir / package["nupkg"]
                            public_path.write_bytes(content)
                            signature_verifier(public_path, payload_dir / (package["id"] + ".signature.log"), remaining())
                            row = {key: package[key] for key in ("id", "version", "nupkgSha256", "snupkgSha256")}
                            row.update(publicNupkgSha256=hashlib.sha256(content).hexdigest(), canonicalSha256=digest,
                                       signatureVerified=True, symbols=[])
                            observed[package["id"]] = row
                        except PendingError as error:
                            pending.append(str(error))
                            continue

                    for probe in package["symbols"]:
                        if any(symbol["url"] == probe["url"] for symbol in row["symbols"]):
                            continue

                        try:
                            status, content = fetcher(probe["url"], {"SymbolChecksumValidationSupported": "1",
                                                      "SymbolChecksum": probe["checksum"]}, remaining())
                            require_response(status, probe["pdbName"])

                            if hashlib.sha256(content).hexdigest() != probe["sha256"]:
                                raise NuGetError(f"Public Portable PDB bytes conflict: {probe['pdbName']}")

                            (payload_dir / probe["pdbName"]).write_bytes(content)
                            row["symbols"].append({**probe, "verified": True})
                        except PendingError as error:
                            pending.append(str(error))

            complete = len(observed) == len(inventory["packages"]) and all(
                len(observed[item["id"]]["symbols"]) == len(item["symbols"]) for item in inventory["packages"])

            if complete:
                return receipt(True)

            events.append({"elapsedSeconds": round(clock() - started, 3), "pending": pending})
            receipt(False)
            sleep(max(0, min(poll_seconds, deadline - clock())))

        raise NuGetError("NuGet readback deadline exhausted; publication may already exist")
    except (NuGetError, packages.PackageError, OSError, subprocess.SubprocessError) as error:
        receipt(False, error)
        raise


def main():
    """Offer readback by default and explicit unused-version/pre-push availability commands."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("readback", "unused-version", "availability"), nargs="?", default="readback")
    parser.add_argument("--package-dir", type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, help="Authenticated qualification manifest; avoids metadata helper execution")
    parser.add_argument("--timeout-seconds", type=int, default=7200)
    arguments = parser.parse_args()

    if arguments.command == "unused-version":
        packages.write_json(arguments.output, check_unused_version(arguments.version))
    elif arguments.package_dir is None:
        parser.error("--package-dir is required for readback and availability")
    elif arguments.command == "readback":
        readback(arguments.package_dir, arguments.version, arguments.output, arguments.timeout_seconds,
                 manifest=arguments.manifest)
    else:
        result = check_availability(arguments.package_dir, arguments.version, True, manifest=arguments.manifest)
        packages.write_json(arguments.output, result)


if __name__ == "__main__":
    try:
        main()
    except (NuGetError, packages.PackageError, OSError, subprocess.SubprocessError) as error:
        print(f"[FAIL] {error}", file=sys.stderr)
        sys.exit(1)
