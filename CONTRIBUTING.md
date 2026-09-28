# Contributing

Contributions are welcome. Read the [Code of Conduct](CODE_OF_CONDUCT.md) before
participating. Route usage and defect reports through [Support](SUPPORT.md) and
suspected vulnerabilities through [Security](SECURITY.md).

## Prerequisites

- the exact .NET 10 SDK pinned in `global.json`;
- a Docker-compatible daemon for MySQL, MariaDB, PostgreSQL, and SQL Server
  Testcontainers.

The SQLite suites run in process. Tests own their server containers and do not
require a developer-managed database.

## Build and test

```sh
dotnet restore Doka.EntityFrameworkCore.NestedSet.slnx --locked-mode
dotnet build Doka.EntityFrameworkCore.NestedSet.slnx -c Release --no-restore
dotnet test Doka.EntityFrameworkCore.NestedSet.slnx \
  -c Release --no-build --no-restore
```

| Test project | Responsibility |
| --- | --- |
| `Doka.NestedSet.Tests` | EF-independent value and relationship contracts |

The ordinary migration project must have no SafeMigrations reference.
SafeMigrations tests extend the contract; they do not define runtime support.

All introduced test projects appear directly under the solution's `tests`
folder. The current revision contains the independent core suite; EF and
provider test ownership is introduced with those projects.

A filtered run is focused feedback and does not qualify all providers.
Never replace a failing provider test with a skip.

## Formatting and language

The repository `.editorconfig` is copied from `Doka.EntityFrameworkCore.MySql`
and is authoritative. Use the existing Rider formatter for layout. Roslyn style
and unused-import checks run independently and must remain clean.

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
Add a new common suite's concrete subclass for every engine so inherited
methods are discovered, including suites whose methods have explicit engine
exclusions. The MySql project contains separate same-named MySQL and MariaDB
suites; MariaDB wrappers live in its `MariaDb` folder and namespace. Common test
bodies still compile once in the specification library.

A common suite stays nongeneric and receives `IProviderFixture<TResource>` in
its protected constructor. The concrete suite's one public constructor receives
the exact registered `ProviderFixture<TResource, TEngine>` and forwards it to the
base. Use its immutable `Engine` instead of passing an engine argument in every
test row. The provider fixture owns the existing resource and forwards its
asynchronous initialization and disposal; individual tests must not dispose it.

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
test ownership and lifetime contract (`docs/implementation-design.md`; introduced with its owning feature)
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
are not controlled. Run paired measurements on the same controlled environment and preserve
the method and limitations in Performance (`docs/performance.md`; introduced with its owning feature).

## Dependency updates

A dependency change requires project-owner approval and review of every
affected lockfile, including transitive changes. After approval, update and
review the solution lockfiles:

```sh
dotnet restore Doka.EntityFrameworkCore.NestedSet.slnx \
  --force-evaluate -p:RestoreLockedMode=false
dotnet restore Doka.EntityFrameworkCore.NestedSet.slnx --locked-mode
```

Requalify the affected provider and migration contracts.

## Public API changes

`PublicAPI.Shipped.txt` is the last stable release contract.
`PublicAPI.Unshipped.txt` contains additions for the next release. The Public
API analyzer must remain clean.

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
4. record both sides of amendments or supersession; and
5. keep the human and machine indexes consistent with the included records.

External links belong under Sources with primary-source retrieval dates.
ADR metadata is authoritative; keep the human index, machine index, and
relationship graph consistent with the included records.

## Documentation

Use [docs/README.md](docs/README.md) to find the canonical owner. Update one
contract and link to it elsewhere instead of duplicating prose.

Before review:

- verify examples against the current public API;
- run complete executable examples that changed;
- check relative links, anchors, code fences, tables, and Mermaid rendering;
- verify provider/version claims against source or current primary docs;
- distinguish performed checks from suggested commands; and
- run ASCII and source-hygiene checks.

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
[Governance](GOVERNANCE.md).
