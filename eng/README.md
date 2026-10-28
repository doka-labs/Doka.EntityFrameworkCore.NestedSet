# Engineering tools

The engineering tools verify local NuGet packages, their SBOMs, coverage artifacts, and dependency evidence.
Run them from the repository root after restoring the solution with the SDK pinned in [global.json](../global.json):

```bash
dotnet restore Doka.EntityFrameworkCore.NestedSet.slnx --locked-mode
bash eng/verify-package-consumer.sh
```

## Qualification and release evidence

Release operators start with `./eng/pre-tag-check.sh` after updating clean
`main`, then follow [Release publication](../docs/operations/release-publication.md).
The no-argument command checks source and SSH signing readiness through the
existing publication module; it does not qualify packages, create tags, or
request publication credentials.

[`ci.yml`](../.github/workflows/ci.yml) runs independent jobs for C# style and
unused imports, offline engineering regressions, the Release solution build,
NuGet pack and archive inspection, nine executable test projects, and the
three SQLite samples. Test and sample matrix cells run on separate runners without a repository-imposed
parallelism cap. Each restores its own project graph while the shipping
packages retain their reviewed locks, so Docker resources and instrumented
outputs are isolated. Coverage
verification waits for all test reports from the current run attempt. The stable
`Repository qualification` check requires every job to succeed and rejects
failed, canceled, or skipped gates.
The CI Unit test job invokes the existing read-only results validator, without
initializing a release qualification or duplicating its report checks in YAML:

```bash
python3 -m eng.release.qualification --verify-results artifacts/ci-tests/ef-unit \
  --project Doka.EntityFrameworkCore.NestedSet.Unit.Tests
```

Omit `--project` to require results for every executable solution test project.
Results-only mode rejects release qualification options and invalid report sets.
[`release-candidate.yml`](../.github/workflows/release-candidate.yml) runs
fixed [`release-candidate.sh`](release-candidate.sh) `--job` selectors from exact
checkouts of the dispatch source. Preflight establishes one shared 7,200-second
UTC deadline. Source quality, engineering regressions, and the canonical Release
build run independently. The build packs each shipping package once and inspects
all primary/symbol archives; its TAR preserves complete execution files, hidden
mapping files, the isolated coverage collector, and executable modes. Consumers
and SBOMs receive a separate small package artifact. Build and package downloads
select exact nonempty producer artifact IDs.

Nine test-project cells and three compiled SQLite samples execute the producer's
compiled files on independent runners after the build. Consumers and SBOMs run
alongside them. Test/sample matrices use `fail-fast: false` without a repository
parallelism cap. The final `qualify` job checks all direct `needs` results with
`jq`. Four scalar producer IDs select source-quality, engineering, consumer,
and SBOM evidence; the test/sample artifact prefix selects only the current run
and attempt, with merged downloads. All six evidence-upload declarations retain
hidden files. The pinned artifact action checks download digests, so no new
job-receipt schema, role/file inventory, or artifact-index API is needed.

Existing source/run/attempt, lock, runtime, and package guards remain required.
The final job rejects missing test results, incomplete coverage, missing query
plans or samples, unsuccessful or mismatched consumers, and invalid SBOMs.
It uses the existing offline SBOM verifier and requires neither .NET setup nor
GitHub API credentials. It assembles the existing candidate
without another build or pack, then passes its artifact ID to the unchanged
attestation/publication flow. See the
[job graph](../docs/release-process.md#qualification-stages).

The local entry point runs `quality`, `tests`, `packages`, `consumer`, and `sbom`
serially by default, then assembles the candidate. Both paths require the same
complete set of checks from the selected commit without downloading PR-CI results.

Benchmarks are deliberately absent from this stage inventory. GitHub-hosted
runner timing, CPU, working-set, and process-wide allocation results are not
stable release criteria. The test stage may enforce deterministic mutation
properties such as hierarchy validity, SQL command counts, update counts, and
affected rows. See [performance and capacity](../docs/performance.md).

```bash
bash eng/release-candidate.sh --version 10.1.0 --workspace --output artifacts/qualification-local
```

The default local two-hour deadline terminates the owned process group and retains failed logs. Use `--timeout-seconds`
for a reviewed local workload deadline between 60 and 14400 seconds. Each local run requires a fresh output directory;
a failed run cannot be resumed. Hosted jobs derive their remaining budget from the shared `--deadline-utc`, not a
fresh two-hour budget. Before sealing, use **Re-run all jobs** or a new dispatch to refresh all checks and preflight.
Partial reruns may retain successful source-quality/engineering logs from the same run and commit. Build identity
and test/sample selection still require the current attempt; consumers and SBOMs must match the sealed package
bytes. A retained preflight keeps its original deadline. See [rerun boundaries](../docs/release-process.md#recovery).
Existing recovery after sealing retains the original candidate IDs and bytes.
Publication reads the already validated manifest in two Bash loops: every
primary package in dependency order, then every symbol package. A failed push
stops the loop immediately and retains its package-specific workflow step log.
`identity.json`, stage directories, and `logs/` preserve the source and failure
context. GitHub retains these files even when qualification fails.
The `candidate/` directory contains six primary and six symbol packages, six SBOMs, manifests, notes, and portable qualification evidence.
`operator-summary.md` routes review to the exact source and candidate hashes. Build outputs and candidate lockfile
copies stay in the run-owned directory. The six shipping package graphs have
committed locks. Qualification copies those locks and adapts internal
project-version edges while keeping their external dependencies locked.
Tests, samples, benchmarks, and engineering tools do not require separate
committed lockfiles.

A normal run requires clean committed source. `--workspace` verifies changes without making them publishable.
Only the exact main workflow-dispatch context produces a publication-eligible candidate, and it still requires
provenance, the authorized signed tag, and protected approval. Do not set hosted identity variables manually.
See [release publication](../docs/operations/release-publication.md) for external actions and recovery.

## Package-consumer verification

Verify already-produced files without packing again:

```bash
bash eng/verify-package-consumer.sh --package-dir artifacts/packages --version 10.1.0 \
  --output artifacts/package-consumers
```

The explicit mode requires all three arguments and a new or empty output directory.
The seven ordinary C# projects live in [`consumers/`](consumers). Their checked-in
props isolate the probes from the producer's central versions, build settings,
and source-project graph. `CandidateVersion` supplies exact package references;
the runner copies these files rather than generating C# or project XML. They
are intentionally outside the main solution because they restore already-packed
candidate versions. The FileSystem probe links the maintained sample source.

The runner verifies six independent single-package roots using separate fresh caches,
exact candidate versions, and source mapping: `core`, `ef`, `mysql`, `postgres`,
`sqlite`, and `sqlserver`. Each of these roots has one direct candidate reference. The ORM-only consumer executes
bounds behavior without EF; the common EF consumer loads its public contract. MySql,
Postgres, and SqlServer configure their real adapters/models and translate queries
without external database I/O; Sqlite performs a real in-memory hierarchy insert/read.
Every single-adapter closure must contain its selected candidate adapter and the
common packages, without foreign candidate adapters or database providers.
A seventh `filesystem` consumer compiles the actual FileSystem and Shared sample
sources against candidate `.MySql` and `.Sqlite` packages and runs the complete
sample on SQLite. This retains package-only sample execution separately from
single-adapter dependency-isolation checks.

All consumers compare restored archive hashes with the supplied files. Temporary
runtime/cache directories are cleaned; source projects, configuration, locked restore
graphs, logs, and `result.json` remain, including on failure. Network access to nuget.org
is required; Docker is not required for these package consumers. Native integration
qualification remains a separate test stage.

Hosted final qualification compares `candidateSha256` in the existing consumer result
with the inspected primary-package hashes before sealing. This binds the small
package artifact used by consumers to the package copies in the build artifact.

The no-argument developer convenience still packs before verification. RC uses explicit consumer mode after its
single qualified pack; PR CI does not run package consumers. Both CI and RC use
[`packages.py`](release/packages.py) to check primary and symbol archives, metadata, dependencies, license,
XML documentation, source commit, and actual PE/PDB identity. CI retains the inspection manifest alongside its
packages. The framework-only [.NET 10 inspector](tools/Doka.NestedSet.PackageInspection/Program.cs) is built with the
solution. No new runtime package
is introduced. Hosted publication uses the attested inspection manifest and exact file hashes without rebuilding.

Adapter archives must declare an exact dependency on the matching common kernel
because they implement its internal ABI. The public kernel-to-ORM edge remains
a minimum dependency. The pack target changes SDK pack items only; it does not
rewrite ProjectReference restore edges into a different lockfile contract.

Consumer archive binding deliberately compares the supplied candidate bytes
with each restored cache archive. A `contentHash` created by that same restore
cannot replace this independent comparison. The existing runner retains these
checks; moving its process, assets-graph and hashing logic to a traversal project
or to the PE/PDB inspector would preserve the work while adding another owner.
The SDK also compares the two original package IDs against their published
`10.0.0` baseline. Only the owner-approved registration removal has a scoped
compatibility suppression; the four new adapter IDs have no previous baseline.

## Architecture decision checks

```bash
bash eng/validate-adrs.sh
python3 -m unittest discover -s eng/quality/tests -p 'test_*.py' -v
```

The first command verifies the complete [MADR profile](../docs/decisions/MADR-PROFILE.md), links, history, relationships,
and deterministic indexes. Use `bash eng/validate-adrs.sh --write-index` to update navigation after valid ADR edits;
the check is separate from CI and RC qualification. The second command verifies the validator against positive and
negative fixtures. Run the release engineering tests separately with
`python3 -m unittest discover -s eng/tests -p 'test_*.py' -v`. Run the PR-only
lockfile diagnostic fixtures with
`python3 -m unittest discover -s eng/ci-tests -p 'test_*.py' -v`.
Both suites run in pull-request CI; only the release suite reruns during RC
qualification. Release fixtures include offline failure/retry/publication
contracts and parse every engineering Python source with the standard library,
including release entry points that unit discovery does not import. They do
not perform external publication or establish hosted signature evidence.

For local Python linting, run `ruff check eng` when Ruff is installed. The
engineering sources pass Ruff 0.16.3. Ruff is not a repository or CI dependency;
the standard-library syntax test above remains the mandatory check.

## SBOM generation and verification

After packing all six shipping packages, run:

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
every report for packages and source-line entries. Across those reports, all six shipping libraries must have executed
lines. An empty report fails even when another report contains valid coverage; no coverage-percentage threshold applies.

Only the Unit assembly has two complementary runs. `Category=Allocation` runs
first without a collector and writes `allocations.trx`; `Category!=Allocation`
runs afterward with Code Coverage and writes `coverage.trx`. Existing isolated
allocation collections and standalone budget controls supply that category.
The registration's released-adapter and deliberately retained GC controls also
belong to this partition, with unchanged assertions and xUnit metadata guards.
Both partitions are required; their case identities must be disjoint and every
selected case must pass without skipping. The result verifier retains the
partition counts, paths, and hashes and combines the logical Unit total. It
rejects a missing, unknown, or repeated partition and duplicate case identities.
Other test projects keep one instrumented run. This keeps instrumentation
allocations out of product budgets without running the full native suites twice.
It also keeps collector-owned roots out of registration-lifetime observations.
[D-013](../docs/decisions/D-013-provider-owned-integration-test-projects.md)
retains Code Coverage 18.11.0 with this split; the known static-instrumentation
unloading defect remains open. The
[collector boundary and re-evaluation criteria](../docs/performance.md#coverage-collector-18110-and-unloadability)
do not authorize dropping either partition or claiming instrumented unloading
support. No additional runner, gate or diagnostic cleanup is introduced.

Each CI test lane requires a Cobertura report before uploading its artifacts.
The separate coverage job downloads the reports from every lane of the current
run attempt and runs this verifier. Hosted RC requires coverage from every
executable test project, then verifies all reports together in `qualify` before
assembling a candidate. Local serial RC performs the same verification in its
test stage. A canceled coverage run can
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
