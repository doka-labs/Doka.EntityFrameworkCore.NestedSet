# Contributing

Contributions are welcome. Read the [Code of Conduct](CODE_OF_CONDUCT.md) before
participating. Route usage and defect reports through [Support](SUPPORT.md) and
suspected vulnerabilities through [Security](SECURITY.md).

## Prerequisites

- the exact .NET 10 SDK pinned in `global.json`;
- a Docker-compatible daemon for MySQL, MariaDB, PostgreSQL, and SQL Server
  Testcontainers; and
- Bash and Python 3 for repository qualification tools.

The SQLite suites run in process. Tests own their server containers and do not
require a developer-managed database.

SQL Server database cases remain visible but skip at runtime on a local ARM
machine before starting a SQL Server container. Metadata and unit tests still
run. Native Linux x64 CI must execute every applicable SQL Server provider,
ordinary migration, and SafeMigrations case with zero skips; an unsupported CI
platform fails. A local ARM run supplies development feedback and cannot
complete repository qualification. See the
[platform and qualification contract](docs/support-and-qualification.md#provider-and-engine-matrix).

For manual debugging and Rider database access, use the optional
[developer Compose environment](docker/README.md). Its profiles build from the
same pinned images as Testcontainers. It owns separate persistent volumes and
is not a prerequisite for tests or benchmarks. Docker resources appear under
the solution's `Config/docker` folder.

## Build and test

```sh
dotnet restore Doka.EntityFrameworkCore.NestedSet.slnx --locked-mode
dotnet build Doka.EntityFrameworkCore.NestedSet.slnx -c Release --no-restore
dotnet test Doka.EntityFrameworkCore.NestedSet.slnx \
  -c Release --no-build --no-restore
```

| Test project | Responsibility |
| --- | --- |
| `Doka.EntityFrameworkCore.NestedSet.Benchmarks.Tests` | Benchmark launcher, scenarios, diagnostics, and provenance |
| `Doka.NestedSet.Tests` | EF-independent value and relationship contracts |
| `Doka.EntityFrameworkCore.NestedSet.Unit.Tests` | EF model, planning, and guard behavior without a database |
| `Doka.EntityFrameworkCore.NestedSet.Specification.Tests` | Non-runnable shared integration contracts and reusable test infrastructure |
| `Doka.EntityFrameworkCore.NestedSet.MySql.Tests` | Doka MySQL and MariaDB behavior |
| `Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests` | PostgreSQL behavior |
| `Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests` | SQL Server behavior |
| `Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests` | SQLite behavior |
| `Doka.EntityFrameworkCore.NestedSet.Migrations.Tests` | Ordinary EF migrations and physical catalogs |
| `Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests` | Optional SafeMigrations adapter contracts |

The ordinary migration project must have no SafeMigrations reference.
SafeMigrations tests extend the contract; they do not define runtime support.
The current optional suite retains the adapter contract now declared with
published Doka 10.6.3 and SafeMigrations Core/adapters 10.6.1. Use the central
package graph; do not add legacy expectations, compatibility skips or weaker
assertions to make it green. The
[provider qualification](docs/qualification/provider-migrations.md) separates
fresh published-package verification from the earlier same-source local
Core/adapter measurements. A package update alone does not rerun those tests.

All test projects appear directly under the solution's `tests` folder. The
specification project is a non-packable library, not a test runner. Unit and
provider projects reference it through `ProjectReference`; they do not compile
linked copies of its sources. Run the provider project to execute its inherited
contracts. Running the specification project does not run integration tests.

### Rider feedback and capacity qualification

The provider projects contain both ordinary regression tests and the large
`Category=Capacity` cases. Rider's **Run All Tests from Solution** includes the
million-node imports, repairs, and ten-million-node reads unless explicitly
configured otherwise.

For ordinary feedback, open **Settings > Build, Execution, Deployment > Unit
Testing** and set **Skip tests from categories** to `Capacity`. Save this in
the **Solution personal** settings layer. A mixed Run All then displays these
cases as ignored by Rider, before their bodies execute. To deliberately run
them, group the Unit Tests Explorer by **Categories** and run only the
**Capacity** node; Rider permits a selection consisting exclusively of an
ignored category. This selects capacity tests across provider projects.

Set **Maximum number of test runners to run in parallel** to the number of
logical processors available on your development machine. This enables
independent test assemblies to run concurrently. Rider requires an explicit
integer; it has no automatic CPU-count setting. The existing xUnit allocation
collection isolation still applies within each assembly.

These are local IDE preferences, not repository defaults. Git ignores
`*.DotSettings.user`, and CLI, CI, and RC execution retain every capacity case. A normal
Rider run with ignored capacity cases is not complete qualification. Full-size
capacity runs also need adequate Docker memory and I/O; on a shared local host,
follow the [capacity execution guidance](docs/performance.md#capacity-targets).
See JetBrains' [category selection](https://www.jetbrains.com/help/rider/Test_Categories.html)
and [runner settings](https://www.jetbrains.com/help/rider/Reference__Options__Tools__Unit_Testing.html).

## Runnable samples

The independent console samples keep their results for inspection. These local
smoke commands explicitly reset each sample-owned SQLite database and discard
its previous sample results without requiring a developer server:

```sh
dotnet run --project samples/FileSystem -c Release -- --provider sqlite --reset
dotnet run --project samples/SignalLab -c Release -- --provider sqlite --reset
dotnet run --project samples/UserGroups -c Release -- --provider sqlite --reset
```

See the [sample catalog](samples/README.md) for Doka MySQL/MariaDB setup,
scenario selection, and the meaning of `--reset`.

## Focused checks

Complete qualification runs on hosted runners through the shared
[quality workflow](.github/workflows/quality-gates.yml) that pull-request CI
and release candidates call. There is no local complete qualification.

Useful focused checks include:

```sh
dotnet format Doka.EntityFrameworkCore.NestedSet.slnx style \
  --severity warn --verify-no-changes --no-restore
dotnet format Doka.EntityFrameworkCore.NestedSet.slnx style \
  --diagnostics IDE0005 --severity hidden --verify-no-changes --no-restore
python3 -m unittest discover -s eng/tests -p 'test_*.py' -v
bash eng/validate-adrs.sh
bash eng/verify-package-consumer.sh
```

A filtered or focused run is development feedback, not complete qualification.
The local ARM platform skips are an explicit environment policy, applied before
database work; never convert a container startup or live provider failure to a
skip. Unsupported SQL Server CI hosts fail; the shared result verification rejects
skipped cases in pull-request CI and the release candidate.

## Secure development

Follow [Secure development](docs/security/secure-development.md) when reviewing
trust boundaries, SQL construction, Scope/TreeId isolation, callback ownership,
resource bounds, dependency changes, or publication identity. Security fixes
need a reproducer, a negative regression, and a legitimate positive control.
Record the analyzed revision and test evidence in the pull request. Keep
private reports and credentials out of public issues, fixtures, and logs.

## Code and tests

Follow `.editorconfig` and the existing Rider layout. Use the style/import checks
above without running `dotnet format whitespace`. Keep changes focused and
consistent with the surrounding feature and provider boundaries.

- Use ASCII and US English. Keep nullable analysis and warnings-as-errors clean;
  fix correctness findings rather than hiding them.
- Document public APIs and useful internal contracts with XML comments. Explain
  non-obvious implementation choices with a short nearby WHY comment.
- Keep shared imports in the existing `Imports.cs` files and file-specific
  imports local. Use a named alias for a real type-name collision.
- Use native asynchronous I/O. Owned async operations, including test and
  benchmark helpers, require an explicit `CancellationToken` and forward it
  through cancellable work and callbacks. Actual framework and runner entry
  signatures remain unchanged; their owned helpers still require tokens. Use
  `CancellationToken.None` explicitly when the entry point supplies no token.
  Deliberately uninterruptible rollback, disposal and terminal finalization
  helpers take no token. Never abandon a started command before resource or
  transaction cleanup; document that safety reason locally.
- Keep samples and test/benchmark fixtures independently synthetic; do not copy
  confidential application schemas, identities or calculation recipes.
- Do not track build output, caches, test results or personal IDE/OS metadata.

### Tests

Add relevant regression tests for new or changed behavior, including positive,
rejection and boundary cases. Cover cancellation, rollback and concurrency where
the affected contract requires them. Each test has one coherent Arrange, Act,
Assert sequence, separated by blank lines; use discovered theory rows for
independent input variants. Coordinate concurrency with barriers rather than sleeps.

Place runtime tests in the existing C# projects. Share fixtures while isolating
test data, and preserve provider coverage and the separation between ordinary EF
migrations and optional SafeMigrations tests. Migration changes need real empty
and upgrade database checks, including relevant data/history/replay assertions.
See [test project ownership](docs/implementation-design.md#test-project-ownership),
[generated property maintenance](docs/regression-coverage.md#maintaining-generated-properties)
and [regression coverage](docs/regression-coverage.md) for technical details.
Allocation and GC-lifetime controls retain the existing
[uninstrumented partition](docs/performance.md#coverage-collector-18110-and-unloadability).

Keep benchmark timings diagnostic. Use deterministic contract and resource
assertions in tests; do not add hardware-dependent timing gates. Record the
commands actually run, their results and relevant limitations.

### APIs and documentation

Keep API baselines, XML documentation, guides, samples and changelog aligned with
changed contracts. Compatibility changes require an explicit decision; follow
[release governance](docs/release-governance.md) for public API baselines. Material
architecture decisions use the existing [MADR profile](docs/decisions/MADR-PROFILE.md).

Use the [documentation index](docs/README.md) to find the existing contract owner
and update it rather than creating a competing copy. Check changed examples,
relative links and anchors. Public documentation must be understandable without
private review notes or local workspace files.

## Dependency updates

A dependency change requires project-owner approval. The six shipping
packages under `src/` commit `packages.lock.json`; tests, samples, benchmarks,
and engineering tools restore their own graphs without committed locks.
Review affected shipping locks, including transitive changes. After approval,
update the dependency declarations and restore:

```sh
dotnet restore Doka.EntityFrameworkCore.NestedSet.slnx \
  --force-evaluate -p:RestoreLockedMode=false
dotnet restore Doka.EntityFrameworkCore.NestedSet.slnx --locked-mode
```

Requalify the affected provider and migration contracts.

Dependency Review retains the SPDX allowlist and vulnerability gate. The
allowlist includes the PostgreSQL SPDX license declared by the existing Npgsql
provider, verified against its package metadata and
[versioned license](https://github.com/npgsql/efcore.pg/blob/v10.0.3/LICENSE).
This is a recognized permissive license, not an unknown-license exception. The only
package-specific license exceptions are `Microsoft.Data.SqlClient.SNI.runtime`
6.0.3 and `Microsoft.Identity.Client.NativeInterop` 0.20.6, which ship Microsoft
license files. The workflow checks the action's complete dependency delta to
enforce those exact versions; a new version requires review of its license file
and an explicit exception update. Do not allow `LicenseRef-scancode-unknown`
globally or disable license checking. SafeMigrations' missing GitHub license
metadata is reported separately and is not resolved by its Scorecard result.

Keep the shipping package graphs locked in CI and RC qualification. SDK and
image pins require the affected full provider and migration matrix, not only
a successful restore.

## Pull requests

Use the pull request template. Describe the concrete consumer problem, final
behavior, compatibility/security impact, and exact verification. Keep one
cohesive concern per pull request.

Target the protected release branch that owns the affected framework major.
Registered inactive lines receive full preparation PR checks and exact
base/head Dependency Review before they can qualify or publish a release.
Keep each branch's SDK, EF graph and line policy intact; applicable fixes need
reviewed ports and that destination's checks. Ordinary feature/manual CI remains
nonpublishing. See [release lines](docs/operations/release-lines.md).

A reviewer checks implementation, meaningful tests, public API, provider and
migration impact, transactions/concurrency, security/privacy, documentation,
dependencies, packages, and evidence as applicable. Approval applies to the
reviewed head; material changes require review again.

External publication, Git mutations, dependency introduction, and hosted
configuration changes remain separate authorized actions under
[Governance](GOVERNANCE.md) and [Release governance](docs/release-governance.md).
