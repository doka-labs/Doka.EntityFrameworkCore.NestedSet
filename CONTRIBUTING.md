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
| `Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests` | Optional published SafeMigrations adapters |

The ordinary migration project must have no SafeMigrations reference.
SafeMigrations tests extend the contract; they do not define runtime support.

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
dotnet run --project samples/Kpis -c Release -- --provider sqlite --reset
dotnet run --project samples/UserGroups -c Release -- --provider sqlite --reset
```

See the [sample catalog](samples/README.md) for Doka MySQL/MariaDB setup,
scenario selection, and the meaning of `--reset`.

For complete local qualification of the current workspace on supported Linux
x64:

```sh
bash eng/release-candidate.sh \
  --version 10.0.0 \
  --workspace \
  --output artifacts/qualification-local
```

Use a new output directory for each run. A workspace result cannot authorize
publication.

Useful focused checks include:

```sh
dotnet format Doka.EntityFrameworkCore.NestedSet.slnx style \
  --severity warn --verify-no-changes --no-restore
python3 -m unittest discover -s eng/tests -p 'test_*.py' -v
bash eng/validate-adrs.sh
bash eng/verify-package-consumer.sh
```

A filtered or focused run is development feedback, not complete qualification.
The local ARM platform skips are an explicit environment policy, applied before
database work; never convert a container startup or live provider failure to a
skip. Unsupported SQL Server CI hosts fail; RC qualification rejects skipped cases.

## Secure development

Follow [Secure development](docs/security/secure-development.md) when reviewing
trust boundaries, SQL construction, Scope/TreeId isolation, callback ownership,
resource bounds, dependency changes, or publication identity. Security fixes
need a reproducer, a negative regression, and a legitimate positive control.
Record the analyzed revision and test evidence in the pull request. Keep
private reports and credentials out of public issues, fixtures, and logs.

## Formatting and language

The repository `.editorconfig` is copied from `Doka.EntityFrameworkCore.MySql`
and is authoritative. Use the existing Rider formatter for layout. Roslyn style
and unused-import checks run independently and must remain clean.

Run the focused import check without changing Rider-formatted code:

```sh
dotnet format Doka.EntityFrameworkCore.NestedSet.slnx style \
  --diagnostics IDE0005 --severity hidden --verify-no-changes --no-restore
```

This formatter check also covers tests, benchmarks, and samples without XML
documentation output. Microsoft's [XML documentation prerequisite for IDE0005](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0005)
applies to reporting the diagnostic during a build.

Build after moving global imports: linked source files can require an import
even when it appears unused in one consuming project.

- Use ASCII and US English in source, comments, documentation, configuration,
  test names, and user-visible text.
- Nullable reference types and warnings-as-errors apply solution-wide.
- Keep global usings in the existing `Imports.cs` files.
- Do not suppress nullable, analyzer, or lint findings to avoid a design fix.
- Do not add a third-party dependency without prior project-owner approval.
- Do not track binaries, build outputs, caches, test results, IDE state, or OS
  metadata.

## XML documentation and comments

- Document every public API.
- Document internal and private APIs when the contract, parameters, result,
  ownership, side effects, or invariants help a caller in the IDE.
- Use `<inheritdoc />` only when the inherited contract applies exactly.
- Explain a non-obvious implementation choice with a nearby `// WHY:` comment
  that names the invariant, framework behavior, or tradeoff. Do not narrate the
  statements.
- Keep comments accurate when behavior changes; delete obsolete explanation
  instead of leaving history in source.

## Source layout

Use blank lines to make control flow and logical phases readable:

- add a blank line after an `if`, `else`, or `switch` block before the next
  statement; attached branches stay together;
- add a blank line after a variable initialization that spans multiple lines;
- add a blank line before `return`, except when the containing block has only
  two code lines;
- separate logical groups with one blank line; and
- avoid decorative or repeated blank lines.

Rider formatting does not replace review of these rules.

## Async and cancellation

- Use the asynchronous API for asynchronous I/O and resource cleanup.
- Forward the operation token through every cancellable call.
- Pass `CancellationToken.None` explicitly when no cancellation source exists.
- Keep cleanup/rollback on a token appropriate to its contract; do not abandon
  cleanup because the forward token is already canceled.
- Handle cancellation both before a scheduled delegate starts and while its
  awaited work runs.
- Do not block on tasks or wrap synchronous computation in `Task.Run` merely to
  make it look asynchronous.

When an intentional synchronous call has a useful async overload, add the
specific Rider suppression immediately above it and a `WHY` explanation. Use
the actual inspection ID, such as `MethodHasAsyncOverload` or
`MethodSupportsCancellation`; never disable the inspection globally.

## Test structure

### Generated invariant tests

The existing core and EF unit projects use `FsCheck.Xunit.v3` for shrinkable
properties. They run with ordinary Rider discovery and `dotnet test`; no extra
project or workflow is required. Core properties exercise Int64 bounds and
scoped/unscoped ancestry. EF properties compare production bulk geometry and
native sibling ranks with independent input adjacency, and reject repeated
entities and assigned keys before staging.

Use generated arguments rather than an internal random seed so FsCheck can
shrink a failure. Create fresh mutable state inside each invocation; shrinking
reexecutes the property. Bound each forest to 64 nodes because the existing
capacity suites own large-scale evidence. Keep ordinary runs randomized. To
reproduce a failure, copy the reported seed tuple into the property's `Replay`
attribute temporarily, then retain the minimal failing input as a regression.
Each property keeps one Arrange/Act/Assert sequence.

### Provider fixture ownership

Keep common integration assertions, models, and helpers in
`Doka.EntityFrameworkCore.NestedSet.Specification.Tests`. Its common test suites
are public abstract classes. Each applicable provider project owns a thin
concrete subclass, plus its assembly fixture and collection registrations.
Tests and helpers used exclusively by one provider belong in that provider's
project. A common method and each of its scenario variants must apply to more
than one executable provider project. MySQL and MariaDB count as one project
for this boundary. Combine method and data-row exclusions when checking it;
an exclusion list must not conceal provider-exclusive ownership. Retain a model
or helper in the common library only while it has real shared consumers.
Add a new common suite's concrete subclass for every engine with an applicable
declared method. A wholly excluded family has no wrapper on that engine;
otherwise IDE metadata discovery still exposes unsupported inherited methods.
Partially excluded families retain their wrapper and specific method or variant
exclusions. The MySql project contains separate same-named MySQL and MariaDB
suites; MariaDB wrappers live in its `MariaDb` folder and namespace. Common test
bodies still compile once in the specification library.

A common suite stays nongeneric and receives `IProviderFixture<TResource>` in
its protected constructor. The concrete suite's one public constructor receives
the exact registered `ProviderFixture<TResource, TEngine>` and forwards it to the
base. Use its immutable `Engine` instead of passing an engine argument in every
test row. The provider fixture owns the existing resource and forwards its
asynchronous initialization and disposal; individual tests must not dispose it.

The shared `ProviderTest` base supplies the xUnit before-test platform hook.
Mark a metadata-only suite with `[DatabaseIndependent]` explicitly; a
`ProviderResources` fixture alone does not prove independence because a body
may still create a database. Mixed migration hooks select the named engine row.
SQL-only SafeMigrations theories use `[DatabasePlatform("SqlServer")]` to record
their engine even when it is absent from scenario data. Keep resource acquisition
lazy until after this hook; startup reached on a non-x64 host fails visibly.

Use ordinary `Fact`, `Theory`, `InlineData`, and `MemberData` for common cases.
Their data contains only scenario arguments, and every concrete engine runs them
by default. When a contract cannot run on an engine, use `EngineFact` or
`EngineTheory` with an explicit `ExcludedEngines` list and a nonempty `Reason`.
Use `EngineInlineData`, `EngineMemberData`, or `EngineTheoryDataRow` for justified
variant exclusions. Unexpected empty data must fail; do not convert it to a skip
or pre-enumerate a member factory merely to decide ownership.

Provider-local fixture-owned suites use ordinary fact and theory annotations
with scenario-only data. Their exact concrete fixture determines the engine;
an ordinary string argument remains payload. MySQL-family bodies and exclusive
helpers belong once in a local abstract suite with separate MySQL and MariaDB
leaves. Put each abstract family in its own `*TestBase.cs` and each concrete
leaf in its own `*Tests.cs`. Describe these as provider-local families, since
their bodies do not belong to the specification library. Put an existing named
`Collection` declaration explicitly on each concrete leaf, rather than on the
base. Retain class fixtures where the family already owns them; the
`ScopeAliasRowset` family has no named collection. A MySQL-only NO PAD
regression belongs only on the MySQL leaf. Do not
derive a local feature suite from a common test-bearing suite, which would
execute the common bodies again. Audit inherited local declarations from every
concrete leaf as well as declarations on the leaf itself.

All provider-local suites use the fixture-owned annotation contract; there is
no parallel `Provider*` selection or discovery path. Keep engine names out of
scenario rows and parameters. `ProviderEngineOwnership` records known engines
with their marker types and owning executables in one immutable catalog.
Discovery, metadata guards, expected model-collection fixture registrations,
and common-coverage ownership use this catalog rather than repeating engine
lists or marker switches. It does not select a test's engine. Preserve the
separate validation of a caller-supplied audit assembly against the concrete
suite's owner. When enumerating ordinary local `MemberData`,
preserve an explicit `MemberType`; otherwise resolve the source from the
concrete leaf, including
inherited family helpers. Constructor guards inspect the actual leaf class and
xUnit's class, collection, and assembly registrations; inheriting a fixture
interface alone does not prove injection.

For Rider, select **Settings/Preferences > Build, Execution, Deployment > Unit
Testing > xUnit.net > Test discovery > Test runner** when checking the provider
test list. Our engine exclusions run during xUnit discovery. Rider's default
**Metadata** mode scans the compiled assembly without launching xUnit, so its
list is not evidence that an inherited method is eligible for that provider.
Running a whole provider project also refreshes Rider's list from the runner.
See [Rider's xUnit discovery modes](https://www.jetbrains.com/help/rider/Reference_Options_Tools_Unit_Testing_xUnit.html).
An entry in the IDE or a shared-base stack frame alone does not identify the
provider that executed a failing case; retain the concrete suite or assembly
name with failure evidence.

`ConcurrentWriterTests` contains the same-tree scenario supported by all engines.
`ConcurrentCapacityTests` contains only the two simultaneous server-transaction
scenarios and has no SQLite wrapper. `ConcurrentWriterMetadataMatchesEngineCapabilities`
inspects raw inherited method metadata, independently of xUnit exclusions, so
reintroducing those scenarios into SQLite fails the provider guard.
The two server families share one type-based xUnit collection per engine. This
preserves their previous serial scheduling while each scenario still runs 64
writers concurrently. Other collections remain parallel; MySQL and MariaDB
have distinct collection types. The raw metadata guard also rejects missing
or different collection types and assembly-wide isolation for these families.

Cross-suite probes belong in independent internal helper classes when shared
and provider-local suites both consume them. Reference those types directly,
rather than reaching into another test suite through a global alias. Keep
suite-private probes private. Provider-local methods need no redundant provider
prefix, and provider-only files use the same feature folders as common suites.
Provider-local models and support types use their provider namespace. Preserve
entity short names and physical table mappings when moving them; namespace
changes alter EF's CLR model identity and require fresh qualification.
Wide registry-rowset tests belong in `Concurrency/TreeRegistry` for every
provider. Keep SQL Server's rowset-lock and Scope-validation cases in separate
suites, and its row-version tests and support in `Ordering/Tracking/RowVersion`.
Shared query-plan evidence belongs in `QueryPlanTestSupport`, rather than in
a test suite that other suites must call.

The five `ProviderAnnotationTests` methods inspect executable-wide metadata
once per executable. Their reasoned MariaDB exclusions remove exactly five
duplicate metadata cases in MySql. Both engine wrappers and both entries in
the complete owned-engine inventory remain audited. Behavioral tests retain
their distinct MySQL and MariaDB executions.

Provider assemblies share expensive server containers while preserving the
existing class or collection database ownership. See the
[test ownership and lifetime contract](docs/implementation-design.md#test-project-ownership)
before changing fixtures; container reuse does not imply one database per test.
The model-compatibility registration guard compares the complete
`ICollectionFixture<>` set with the catalog's exact expected engine-bound
fixtures. Do not filter out raw or unrelated resource registrations before
checking the set: xUnit initializes registered closed fixtures even when no
test constructor requests them, if the collection has an eligible case.

Every test has one Arrange, Act, Assert sequence in that order. Separate the
three phases with blank lines and explicit comments:

```csharp
// Arrange
var entity = CreateEntity();

// Act
var result = await sut.ExecuteAsync(entity, CancellationToken.None);

// Assert
Assert.Equal(expected, result);
```

Prepare all starting state in Arrange. Capture an expected exception in Act.
Assert the result and final state in Assert. A second action after assertions,
or a repeated Arrange/Act/Assert sequence, belongs in another test. Multiple
assertions about one outcome are appropriate.

Additional test rules:

- name one observable behavior per test;
- use theories for the same behavior across inputs or providers;
- share expensive fixtures while isolating each test's data;
- coordinate concurrency with explicit barriers, never sleeps;
- preserve the original failure and enough sanitized reproduction context;
- test success, rejection, boundary, cancellation, rollback, and concurrency
  where the contract requires them;
- run migrations against empty and upgrade databases instead of using
  `EnsureCreated`; and
- verify optional SafeMigrations replay/drift separately from ordinary EF
  migration history.

## Performance evidence

Automated tests may assert deterministic structure, SQL command/update counts,
affected-row formulas, memory ownership, and stable query-plan properties.

Do not add wall-clock, CPU, working-set, or process-wide allocation thresholds
to CI or release qualification. GitHub-hosted runner hardware and contention
are not controlled. The benchmark executable is a local diagnostic tool; run
paired comparisons on the same dedicated environment and preserve the method
and limitations in [Performance](docs/performance.md).

## Dependency updates

A dependency change requires project-owner approval. Only the two shipping
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

Dependency Review retains the SPDX allowlist and vulnerability gate. Its only
package-specific license exceptions are `Microsoft.Data.SqlClient.SNI.runtime`
6.0.2 and `Microsoft.Identity.Client.NativeInterop` 0.20.6, which ship Microsoft
license files. The workflow checks the action's complete dependency delta to
enforce those exact versions; a new version requires review of its license file
and an explicit exception update. Do not allow `LicenseRef-scancode-unknown`
globally or disable license checking. SafeMigrations' missing GitHub license
metadata is reported separately and is not resolved by its Scorecard result.

Keep the shipping package graphs locked in CI and RC qualification. SDK and
image pins require the affected full provider and migration matrix, not only
a successful restore.

## Public API changes

`PublicAPI.Shipped.txt` records the reviewed stable release contract.
`PublicAPI.Unshipped.txt` contains additions for the next stable release,
including declarations first published in an RC. The initial `10.0.0` contract
is recorded in both shipped baselines; the unshipped files retain only the
nullable directive until new declarations are added. The Public API analyzer
must remain clean.

Before a stable release, move reviewed additions into the shipped file in the
release-preparation source commit. Removals and signature changes require an
explicit compatibility and SemVer decision, tests, docs, changelog, and a MADR
record when architectural.

Every public type and member must have a current consumer and complete XML
documentation. Do not add options for hypothetical future use.

## Architecture decisions

Material decisions follow MADR 4.0 with the
[Doka profile](docs/decisions/MADR-PROFILE.md):

1. copy `docs/decisions/adr-template.md` to the next contiguous `D-NNN` file;
2. complete metadata, context, drivers, options, consequences, confirmation,
   triggers, history, references, and sources;
3. distinguish proposed from accepted status and never reconstruct approval;
4. record both sides of amendments or supersession;
5. run `bash eng/validate-adrs.sh --write-index`; and
6. run `bash eng/validate-adrs.sh` and its separate validator fixtures in
   `eng/quality/tests`.

External links belong under Sources with primary-source retrieval dates.
Generated ADR navigation must not be edited by hand.

## Documentation

Use [docs/README.md](docs/README.md) to find the canonical owner. Update one
contract and link to it elsewhere instead of duplicating prose.

Before review:

- verify examples against the current public API;
- run complete executable examples that changed;
- check relative links, anchors, code fences, tables, and Mermaid rendering;
- verify provider/version claims against source or current primary docs;
- distinguish performed checks from suggested commands; and
- review source text against the repository's ASCII and formatting conventions.

Documentation does not create hosted controls, package availability, or support
for an untested provider.

## Pull requests

Use the pull request template. Describe the concrete consumer problem, final
behavior, compatibility/security impact, and exact verification. Keep one
cohesive concern per pull request.

A reviewer checks implementation, meaningful tests, public API, provider and
migration impact, transactions/concurrency, security/privacy, documentation,
dependencies, packages, and evidence as applicable. Approval applies to the
reviewed head; material changes require review again.

External publication, Git mutations, dependency introduction, and hosted
configuration changes remain separate authorized actions under
[Governance](GOVERNANCE.md) and [Release governance](docs/release-governance.md).
