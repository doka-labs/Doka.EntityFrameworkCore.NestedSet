# Verify a NestedSet release

A published release uses three independent identities:

1. a signed annotated Git tag identifies source;
2. GitHub attestations bind candidate packages and symbols to the release
   workflow and source commit; and
3. NuGet.org repository signatures protect the public primary packages.

Passing one check does not imply the others passed.

## Prerequisites

- Git with SSH signature verification;
- GitHub CLI with `gh attestation verify`;
- .NET SDK with `dotnet nuget verify` on Linux or Windows; signed-package
  verification is not supported on macOS;
- `jq` and Python 3 for the repository's read-only NuGet readback; and
- a clean directory for downloaded evidence.

```sh
set -euo pipefail

release_tag="v<release-version>"
release_version="${release_tag#v}"
repository="doka-labs/Doka.EntityFrameworkCore.NestedSet"
release_root="artifacts/${release_tag}"
nuget_root="${release_root}/nuget.org"
```

## Verify source identity

Fetch the exact tag and inspect its signed annotated object:

```sh
git fetch origin "refs/tags/${release_tag}:refs/tags/${release_tag}"
git -c gpg.format=ssh \
  -c gpg.ssh.allowedSignersFile=.github/allowed_signers \
  verify-tag "${release_tag}"

release_commit="$(git rev-list -n 1 "${release_tag}")"
test -n "${release_commit}"
test "$(git rev-parse HEAD)" = "${release_commit}"
```

Review the public keys in [`.github/allowed_signers`](../../.github/allowed_signers)
against the approved Doka Labs signing identity before using that policy. Obtain
it from reviewed protected-main history or an already trusted checkout; an
arbitrary candidate cannot establish signer approval by including its own key.
A cryptographically valid signature from an unapproved principal is not
sufficient. Run the repository readback helper below only from this exact
verified source checkout.

## Download release assets

```sh
mkdir -p "${release_root}"
gh release download "${release_tag}" \
  --repo "${repository}" \
  --dir "${release_root}"
```

The release must contain exactly one of each candidate asset:

- `Doka.NestedSet.<version>.nupkg` and `.snupkg`;
- `Doka.EntityFrameworkCore.NestedSet.<version>.nupkg` and `.snupkg`;
- `Doka.NestedSet.spdx.json`;
- `Doka.EntityFrameworkCore.NestedSet.spdx.json`;
- `candidate.json`, `package-manifest.json`, `qualification-evidence.zip`, and
  `release-notes.md`;
- `release-provenance.jsonl`; and
- one SBOM attestation bundle for each primary package.

Missing, duplicate, or unexpected identity assets require investigation. Do
not select one conflicting file by timestamp.

Before any asset is consumed, verify the exact local inventory, candidate
version, source commit, size, and SHA-256 values. The source commit passed to
this procedure must come from the independently verified tag above; the
manifest cannot choose its own trusted source.

```sh
verified_root="${release_root}-verified"
mkdir -p "${verified_root}"

python3 - "${release_root}" "${verified_root}" "${release_version}" "${release_commit}" <<'PY'
from pathlib import Path
import hashlib
import json
import re
import shutil
import sys

source = Path(sys.argv[1])
target = Path(sys.argv[2])
expected_version = sys.argv[3]
expected_commit = sys.argv[4]

packages = ("Doka.NestedSet", "Doka.EntityFrameworkCore.NestedSet")
required_candidate = {
    "package-manifest.json",
    "qualification-evidence.zip",
    "release-notes.md",
    *(f"{package}.{expected_version}.{extension}" for package in packages for extension in ("nupkg", "snupkg")),
    *(f"{package}.spdx.json" for package in packages),
}
required_release = {
    "candidate.json",
    "release-provenance.jsonl",
    *(f"sbom-{package}.jsonl" for package in packages),
    *required_candidate,
}

actual_release = {path.name for path in source.iterdir() if path.is_file() and not path.is_symlink()}
if actual_release != required_release:
    raise SystemExit("Unexpected or missing release asset")
if any(path.is_symlink() or not path.is_file() for path in source.iterdir()):
    raise SystemExit("Unexpected or missing release asset")
if any(target.iterdir()):
    raise SystemExit("Verification target must be empty")

for name in sorted(required_release):
    shutil.copyfile(source / name, target / name)

manifest = json.loads((target / "candidate.json").read_text(encoding="ascii"))
if manifest.get("version") != expected_version or manifest.get("source", {}).get("commit") != expected_commit:
    raise SystemExit("Candidate version/source mismatch")

candidate_files = manifest.get("files")
if not isinstance(candidate_files, dict) or set(candidate_files) != required_candidate:
    raise SystemExit("candidate_changed: missing or unexpected candidate inventory")

digest_pattern = re.compile(r"[a-f0-9]{64}")
for name, identity in candidate_files.items():
    path = target / name
    with path.open("rb") as asset:
        digest = hashlib.file_digest(asset, "sha256").hexdigest()
    expected_digest = identity.get("sha256") if isinstance(identity, dict) else None
    expected_size = identity.get("size") if isinstance(identity, dict) else None
    if (
        not isinstance(expected_digest, str)
        or digest_pattern.fullmatch(expected_digest) is None
        or digest != expected_digest
        or path.stat().st_size != expected_size
    ):
        raise SystemExit(f"candidate_changed: {name}")

print("Candidate inventory, hashes, version, and source match")
PY
```

This local check copies the exact asset set into a separate directory, then
checks the copied candidate bytes against the copied manifest. Use only those
copies for subsequent provenance and SBOM checks. The manifest alone is not
authenticated; the attestation checks below provide that identity layer.

Inspect `qualification-evidence.zip` against the
[qualification contract](../release-process.md#qualification-stages). Hosted
parallel qualification retains the source/tooling results, canonical build and
package inventory, nine executable test-project results with per-project
coverage, all required query plans, three compiled SQLite samples, consumers,
and both SBOM checks. GitHub's direct job dependencies require success, scalar
producer artifact IDs select non-matrix outputs, and test/sample artifact names
select the current run and attempt. Evidence uploads retain hidden files, and
the pinned download action checks artifact digests. Existing source, version,
run, attempt, runtime, dependency-lock, and package guards remain in place;
qualification does not add a separate job-receipt or file-inventory protocol.
The consumer result's primary-package hashes must equal the inspected package
manifest for the archives being sealed, in addition to successful core and EF
execution at the selected version.

Downloaded build identity and test/sample selection must match the current run
and attempt. Successful source-quality/engineering logs may originate from an
earlier attempt of the same run and commit; their scalar artifact IDs select the
successful checks. They do not supply compiled runtime or package bytes. A
retained preflight keeps its original deadline. Follow the
[rerun boundaries](../release-process.md#recovery); arbitrary evidence from
another run cannot fill missing or conflicting results.

The final `qualify` job seals the existing candidate only after checking this
complete set. Publication recovery continues to verify the original sealed
candidate and provenance artifact IDs.

## Verify build provenance

Verify every primary and symbol package with the portable provenance bundle and
pin the repository, workflow, source, and hosted runner:

```sh
for artifact in \
  "${verified_root}/Doka.NestedSet.${release_version}.nupkg" \
  "${verified_root}/Doka.NestedSet.${release_version}.snupkg" \
  "${verified_root}/Doka.EntityFrameworkCore.NestedSet.${release_version}.nupkg" \
  "${verified_root}/Doka.EntityFrameworkCore.NestedSet.${release_version}.snupkg"; do
  gh attestation verify "${artifact}" \
    --bundle "${verified_root}/release-provenance.jsonl" \
    --repo "${repository}" \
    --signer-workflow "${repository}/.github/workflows/release-candidate.yml" \
    --signer-digest "${release_commit}" \
    --source-ref "refs/heads/main" \
    --source-digest "${release_commit}" \
    --deny-self-hosted-runners
done
```

Verify each primary package with its corresponding SBOM bundle and SPDX 2.2
predicate. Also compare the decoded predicate with the downloaded SPDX JSON;
signature success alone does not show that the expected SBOM was selected.

```sh
for package in Doka.NestedSet Doka.EntityFrameworkCore.NestedSet; do
  verified_sbom="$(gh attestation verify \
    "${verified_root}/${package}.${release_version}.nupkg" \
    --bundle "${verified_root}/sbom-${package}.jsonl" \
    --repo "${repository}" \
    --signer-workflow "${repository}/.github/workflows/release-candidate.yml" \
    --signer-digest "${release_commit}" \
    --source-ref "refs/heads/main" \
    --source-digest "${release_commit}" \
    --deny-self-hosted-runners \
    --predicate-type "https://spdx.dev/Document/v2.2" \
    --format json)"

  printf '%s\n' "${verified_sbom}" |
    jq -e --slurpfile expected "${verified_root}/${package}.spdx.json" '
      type == "array" and length == 1 and
      .[0].verificationResult.statement.predicateType == "https://spdx.dev/Document/v2.2" and
      .[0].verificationResult.statement.predicate.spdxVersion == "SPDX-2.2" and
      .[0].verificationResult.statement.predicate == $expected[0]
    ' >/dev/null
done
```

For offline verification, protect an independently obtained trusted root and
pass it to GitHub CLI. The bundle by itself is not a trust root.

## Verify public NuGet packages

Use the read-only helper from the verified source checkout. It discovers
NuGet.org's package endpoint, retains the exact public responses, compares
canonical ZIP payloads with the verified candidates, verifies NuGet signatures,
and checks public Portable PDBs against the qualified symbol probes:

```sh
mkdir -p "${nuget_root}"
python3 -m eng.release.nuget readback \
  --package-dir "${verified_root}" \
  --version "${release_version}" \
  --manifest "${verified_root}/package-manifest.json" \
  --output "${nuget_root}/readback.json"

jq -e '.success == true and (.packages | length) == 2 and
  all(.packages[]; .signatureVerified == true and
    all(.symbols[]; .verified == true))' "${nuget_root}/readback.json" >/dev/null
```

Run this step on Linux or Windows. The helper calls `dotnet nuget verify --all`
for each public package. NuGet.org adds `.signature.p7s`, so the raw public
archive hash differs from the unsigned GitHub candidate. The helper permits
that signature entry only and fails on any other canonical payload difference.
It retains the public bytes, signature logs, PDBs, and readback receipt under
`${nuget_root}`.

## Verify the GitHub release

Check that the named release is published and immutable, verify its release
attestation, and bind every staged asset byte-for-byte to that release:

```sh
release_info="$(gh release view "${release_tag}" --repo "${repository}" \
  --json tagName,isDraft,isImmutable,assets)"

printf '%s\n' "${release_info}" |
  jq -e --arg tag "${release_tag}" '
    .tagName == $tag and .isDraft == false and .isImmutable == true
  ' >/dev/null

expected_assets="$(for asset in "${verified_root}"/*; do basename "${asset}"; done | sort)"
actual_assets="$(printf '%s\n' "${release_info}" | jq -r '.assets[].name' | sort)"
test "${actual_assets}" = "${expected_assets}"

gh release verify "${release_tag}" --repo "${repository}"

for asset in "${verified_root}"/*; do
  gh release verify-asset "${release_tag}" "${asset}" --repo "${repository}"
done
```

## Accept the release only when

- the tag is annotated, trusted, and resolves to `release_commit`;
- each package and symbol file matches a signed provenance subject;
- provenance names the protected release workflow on `main`, exact source
  commit, and a GitHub-hosted runner;
- each SBOM signature and predicate matches its package and SPDX document;
- both public primary packages have valid NuGet repository signatures;
- public package contents match the qualified candidates except for the NuGet
  signature entry;
- both symbol packages expose the expected Portable PDBs; and
- the GitHub release is published, immutable, and contains the exact complete
  asset set.

Checksums detect changed bytes but do not identify who authorized them.
Signatures and provenance supply that identity layer.

## Primary sources

- Git, [git-verify-tag](https://git-scm.com/docs/git-verify-tag).
- GitHub CLI, [gh attestation verify](https://cli.github.com/manual/gh_attestation_verify).
- GitHub CLI, [gh attestation trusted-root](https://cli.github.com/manual/gh_attestation_trusted-root).
- GitHub CLI, [gh release view](https://cli.github.com/manual/gh_release_view),
  [gh release verify](https://cli.github.com/manual/gh_release_verify), and
  [gh release verify-asset](https://cli.github.com/manual/gh_release_verify-asset).
- Microsoft, [dotnet nuget verify](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-verify).
- Microsoft, [NuGet signed-package verification on supported operating systems](https://learn.microsoft.com/en-us/dotnet/core/tools/nuget-signed-package-verification).
- Microsoft, [NuGet V3 service index](https://learn.microsoft.com/en-us/nuget/api/service-index).
- Microsoft, [NuGet package content](https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource).

These interfaces are version-sensitive. Verify them against their primary
documentation when preparing an actual release.
