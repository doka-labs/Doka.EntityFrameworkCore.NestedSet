# Engineering tools

The engineering tools verify local NuGet packages, their SBOMs, coverage artifacts, and dependency evidence.
Run them from the repository root after restoring the solution with the SDK pinned in [global.json](../global.json):

```bash
dotnet restore Doka.EntityFrameworkCore.NestedSet.slnx --locked-mode
bash eng/verify-package-consumer.sh
```

## Qualification and release evidence

[`ci.yml`](../.github/workflows/ci.yml) runs offline engineering regressions,
then restores, checks C# style, builds, tests, runs samples, and packs the
solution for pull requests into `main`.
[`release-candidate.yml`](../.github/workflows/release-candidate.yml) runs
[`release-candidate.sh`](release-candidate.sh) once from a fresh checkout. The
release runner executes `quality`, `tests`, `packages`, `consumer`, and `sbom`
in order, then assembles the candidate. It rebuilds and retests its selected
commit without downloading results from CI.

Benchmarks are deliberately absent from this stage inventory. GitHub-hosted
runner timing, CPU, working-set, and process-wide allocation results are not
stable release criteria. The test stage may enforce deterministic mutation
properties such as hierarchy validity, SQL command counts, update counts, and
affected rows. See [performance and capacity](../docs/performance.md).

```bash
bash eng/release-candidate.sh --version 10.0.0-dev --workspace --output artifacts/qualification-local
```

The default two-hour deadline terminates the owned process group and retains failed logs. Use `--timeout-seconds`
for a reviewed workload deadline between 60 and 14400 seconds. Every run requires a fresh output directory; a
failed run cannot be resumed. `identity.json`, stage directories, and `logs/` preserve the source and failure
context. GitHub retains these files even when qualification fails.
The `candidate/` directory contains the four packages, two SBOMs, manifests, notes, and portable qualification evidence.
`operator-summary.md` routes review to the exact source and candidate hashes. Build outputs and candidate lockfile
copies stay in the run-owned directory. Only internal project-version edges are adapted; external packages remain locked.

A normal run requires clean committed source. `--workspace` verifies changes without making them publishable.
Only the exact main workflow-dispatch context produces a publication-eligible candidate, and it still requires
provenance, the authorized signed tag, and protected approval. Do not set hosted identity variables manually.
See [release publication](../docs/operations/release-publication.md) for external actions and recovery.

## Package-consumer verification

Verify already-produced files without packing again:

```bash
bash eng/verify-package-consumer.sh --package-dir artifacts/packages --version 10.0.0-dev \
  --output artifacts/package-consumers
```

The explicit mode requires all three arguments and a new or empty output directory. It verifies the exact Core and EF
archives independently using separate fresh caches, exact NuGet version constraints, and source mapping. A Core-only
consumer proves the core has no EF dependency; the EF consumer executes the runnable SQLite domain sample. Both check
that the restored archive hashes equal the supplied files. Temporary runtime/cache directories are cleaned; sources,
project/configuration files, locked restore graphs, logs, and `result.json` remain for inspection, including on failure.
Network access to nuget.org is required; Docker is not required for these two consumers.

The no-argument developer convenience still packs before verification. CI/RC always use explicit mode after their
single qualified pack. [`packages.py`](release/packages.py) additionally checks both primary and symbol archives,
metadata, dependencies, license, XML documentation, source commit, and actual PE/PDB identity. Its framework-only
[.NET 10 inspector](tools/Doka.NestedSet.PackageInspection/Program.cs) is built with the solution. No new runtime package
is introduced. Hosted publication uses the attested inspection manifest and exact file hashes without rebuilding.

## SBOM generation and verification

After packing the two shipping packages, run:

```bash
package_version="$(dotnet msbuild src/Doka.NestedSet/Doka.NestedSet.csproj -nologo -getProperty:PackageVersion)"
bash eng/generate-sbom.sh --package-dir artifacts/packages --output artifacts/sbom --version "$package_version"
```

[`generate-sbom.sh`](generate-sbom.sh) uses the standalone Microsoft SBOM Tool 4.1.5 release executable with
platform-specific SHA-256 verification. Supported script hosts are Linux x64 and macOS x64/Arm64. The executable
includes its own runtime; no separate .NET 8 installation or `dotnet tool install` is required. The product SDK and
targets remain .NET 10. Bash, curl, Python 3, and the pinned .NET SDK are required; network access
downloads the verified tool and restores package dependencies.

[`verify-package-sbom.py`](verify-package-sbom.py) uses the Python standard library and a fresh temporary consumer
per package to resolve the actual NuGet dependency closure. It checks exact package names and versions and the
candidate file hash, runs the vendor's generation/validation commands, and rejects incomplete or contaminated
inventories. Offline negative tests cover altered package bytes and removed,
changed, or extraneous dependencies; the RC does not rerun vendor validation
on deliberately tampered archives.
The output directory must be empty. Temporary tool and consumer directories are cleaned after execution.

Each package has its own `<package-id>/_manifest/spdx_2.2/manifest.spdx.json` beneath the output directory, together
with restore and validation evidence. The top-level summary and checksums describe the collected results.
The RC workflow signs the corresponding package and manifest only after verification; local generation does not
perform signing or publication. See [release attestation](../docs/release-process.md#attestation).

Run the independent verifier's regression tests without downloading or installing tools:

```bash
python3 -m unittest discover -s eng/tests -p 'test_*.py' -v
```

## Coverage-artifact verification

After the test run produces Cobertura reports, run:

```bash
python3 eng/verify-coverage.py artifacts/test-results
```

[`verify-coverage.py`](verify-coverage.py) uses only the Python standard library. It rejects missing reports and checks
every report for packages and source-line entries. Across those reports, both shipping libraries must have executed
lines. An empty report fails even when another report contains valid coverage; no coverage-percentage threshold applies.

CI runs this check before uploading test artifacts. A canceled coverage run can
leave instrumented DLL/PDB copies in a test output directory and cause a later
collection to produce an empty report. Rebuild the Release configuration and
rerun the complete test stage; the verifier reports an empty artifact instead
of treating it as success.

## Database image updates

[`docker/database-images.Dockerfile`](../docker/database-images.Dockerfile) is the canonical, immutable image
source consumed by tests and benchmarks. It is embedded as assembly data for Testcontainers; the optional
[developer Compose environment](../docker/README.md) builds its independent named stages into local service images.
Each `FROM` line selects one engine by its stage name; every image requires a version tag and SHA-256 digest.
Doka's MySQL and MariaDB capability versions are derived from these same tags. Dependabot monitors this Docker
manifest and the SDK in `global.json`.

After changing an image pin, rebuild the consuming assemblies and start a selected developer profile with
`--build` as documented in the Docker guide. No developer Compose service is used by qualification or benchmarks.

Review image updates against the supported engine lines and run the full runtime and migration suites before accepting
them. Digest pinning identifies tested content; it does not replace compatibility or security review.

## Dependabot lockfile diagnostics

When locked restore fails on a Dependabot pull request, CI runs
[`prepare-lockfile-patch.py`](prepare-lockfile-patch.py) and uploads `dependabot-lockfile-patch` if the complete
solution restore produces changed lockfiles. The original check remains failed. The diagnostic has no branch-write
credentials and does not commit or push changes.

The script selects lockfiles from projects in the solution, reevaluates their dependencies, and writes a patch
containing only those lockfiles. It rejects paths that escape the original checkout boundary. Run it locally with:

```bash
python3 eng/prepare-lockfile-patch.py --solution Doka.EntityFrameworkCore.NestedSet.slnx \
  --output artifacts/dependabot-lockfiles.patch
```

This command updates local lockfiles through `dotnet restore`; the patch records those changes for review. Inspect
the package graph and verify a subsequent locked restore before accepting the update. If restore makes no lockfile
changes, the diagnostic fails explicitly: the original error needs a different diagnosis.
See [Dependency updates](../CONTRIBUTING.md#dependency-updates) for the complete workflow.
