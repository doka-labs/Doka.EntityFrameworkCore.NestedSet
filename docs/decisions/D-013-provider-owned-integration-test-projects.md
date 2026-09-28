---
id: D-013
status: implemented
date: 2026-09-25
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "EF Core test ownership, fixture-owned engines, and referenced specifications"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-013 -- Give each database provider its own integration test project

## Context and Problem Statement

One EF Core test project mixed unit tests with SQLite, Doka MySQL, MariaDB,
PostgreSQL, and SQL Server integration cases. Rider showed one large test list,
and selecting one provider required a filter over the combined assembly. The
same behavioral contracts must still run on every supported engine without
copying their source files or losing cases during the split.

The first split compiled linked common sources into each provider project.
Rider then displayed the same files in four project trees, which made navigation
and maintenance harder. A referenced specification library must keep one
physical and compiled owner for common assertions while retaining independently
discoverable provider suites, the existing engine matrix, and fixture lifetimes.

The referenced layout still repeated engine names in common theory rows and
selected both MySQL and MariaDB from one concrete suite. Review also found that
accepting a local method with one owned row could conceal foreign rows, and
checking inherited fixture interfaces alone did not establish that xUnit could
resolve the concrete constructor. The owner selected immutable fixture-owned
engines with shared engine-neutral cases and explicit justified exclusions.

A follow-up review found provider-exclusive methods still placed in common
suites by excluding every other engine. Some exclusions applied only to data
variants, so checking the method alone was insufficient. MySQL-family methods
also belong to one executable project despite covering two engines. The same
review identified five executable-wide metadata audits repeated for MariaDB.

A further consistency review found 17 provider-local suites still using the old
`Provider*` annotations alongside fixture-owned suites. Shared probes were nested
inside test classes and imported through aliases, and four SQL Server files
remained outside their feature folders. The complete local migration replaces
the old annotation path while preserving every database scenario, resource
lifetime, and live engine-ownership consumer.

The next review found inconsistent local family files, provider-only types in
the common namespace, repeated engine catalogs, and an incomplete collection
registration audit. A query-plan evidence writer also remained inside a suite.
The alleged dead ownership check was only partly redundant: resolving a suite's
engine does not validate a separately supplied audit assembly. The refinement
standardizes navigation and closes metadata gaps without changing fixture scope
or database behavior.

## Decision Drivers

- Make unit and provider ownership visible as separate projects in Rider.
- Preserve the complete existing test matrix, including MySQL and MariaDB in
  one Doka provider project.
- Keep one source of truth for cross-provider assertions and fixtures.
- Show common sources once in Rider and keep provider-only sources with their
  executable owner.
- Make provider selection explicit and reject unknown test project identities.
- Keep the qualification gate's zero-skipped-test contract intact.
- Make common cases run on every concrete engine by default while preserving
  justified method and variant exclusions.
- Audit every local declared row and the concrete constructor's actual xUnit
  fixture mapping without starting database resources.
- Keep each common method and variant applicable to more than one executable
  provider owner, accounting for combined method and row exclusions.
- Keep exclusive family bodies once in their executable and run metadata-only
  assembly audits once without dropping either engine's structural ownership.
- Use one fixture-owned annotation contract for all local suites, keep shared
  probes independent of test suites, and align provider-local feature folders.
- Keep provider-local family bases, engine leaves, and model namespaces uniform.
- Derive engine, marker, and executable inventories from one immutable catalog.
- Audit complete collection registrations and retain independent audit ownership.

## Considered Options

- Referenced specification suites with fixture-owned engine identity
- Referenced specification suites with explicit engine rows
- Separate provider test projects compiling shared integration sources
- One project with provider traits and runner filters
- Copy integration tests into provider-specific source trees

## Decision Outcome

Chosen option: "Referenced specification suites with fixture-owned engine identity",
because the concrete fixture supplies one immutable engine to engine-neutral
shared methods while provider projects remain independently runnable. Common
sources retain one owner in Rider. The owner accepted this revised implementation
approach, as recorded in Decision History.

All projects appear directly under the solution's `tests` folder.
`Doka.EntityFrameworkCore.NestedSet.Specification.Tests` is a non-runnable,
non-packable library containing public abstract common suites, models, and
reusable test infrastructure. It references the already resolved xUnit
`xunit.v3.extensibility.core` and `xunit.v3.assert` components at 4.0.1. Executable
runner targets stay in the runner projects; this organization does not upgrade
the framework or introduce another dependency version.

The four provider projects and the EF unit project use `ProjectReference` to
consume the library without compiling linked copies of its sources. Each
engine owns a thin public concrete subclass for every common suite,
including nested suites. xUnit discovers inherited methods on those concrete
classes. Provider-exclusive tests and their helpers live only in their owning
project; mixed source files are split along that ownership boundary. A common
method and each scenario variant must apply to more than one executable provider
project after combining method and row exclusions. MySQL and MariaDB count as
one executable owner. Test-free helpers and model identities remain common only
where they have real shared consumers. The unit
project keeps its unit test bodies and consumes reusable infrastructure through
the same library reference.

Shared command probes have independent internal source owners. `NativeGuardProbe`
serves common guard tests and provider-local collation tests. `ScaleProbe` and
`ScaleCommand` serve common refresh/query-shape tests and provider-local cache
tests. Direct references replace nested-suite imports and global aliases;
suite-private probes stay private. The extraction preserves the ordinary
`AddInterceptors` registration, context correlation, query tags, command reader
accounting, observer disposal, and measurements. Existing context/entity CLR
identities and model/cache configuration remain unchanged. Provider-local files
use feature folders, with SQL Server registry tests under
`Concurrency/TreeRegistry` and row-version tests under
`Ordering/Tracking/RowVersion`.

Wide registry-rowset suites use `Concurrency/TreeRegistry` on every provider,
including SQLite's `JsonRowsetTests`. SQL Server separates `WideRowsetLockTests`
from `ScopeValidationTests`, and its row-version suite and support use
`RowVersionTests`, `RowVersionFixture`, and `RowVersionContext`. SQLite's local
timeout suite uses `WriterTimeoutTests`. Provider-only models and support use
their provider namespace. These moves intentionally change CLR model-cache
identity, so fresh model tests qualify them. Entity short names and physical
table names remain unchanged; in particular, `SqlServerVersionNode` keeps its
short name and explicit table mapping. The earlier probe extraction itself
does not alter the context/entity identities described above.

`QueryPlanTestSupport` owns the shared asynchronous evidence writer. All six
existing call paths reference the helper directly instead of another suite.
Output-directory overrides, file names, double-newline formatting, and explicit
cancellation behavior remain unchanged.

Common suites stay nongeneric. Their protected constructors consume an
`IProviderFixture<TResource>` view; a concrete leaf's one public constructor
receives the exact registered `ProviderFixture<TResource, TEngine>` and forwards
it to the shared base. The fixture exposes immutable `Engine` and `Value`, creates
the existing resource once, and forwards its asynchronous initialization and
disposal. Shared tests use that engine without engine arguments in theory rows.
Metadata-only suites use `ProviderResources` rather than a database resource.

MySQL and MariaDB share the MySql executable but have separate same-named
concrete suites. MariaDB wrappers live in its `MariaDb` folder and namespace.
Discovery obtains the engine from the concrete constructor's closed fixture
type, without activating the resource or reading ambient test state. A fixture
from a foreign provider project, contradictory engine fixtures, or a missing
engine fixture fails visibly. Inherited methods retain one common test body.

Ordinary `Fact`, `Theory`, `InlineData`, and `MemberData` are the shared default:
each concrete engine runs the same engine-neutral scenario arguments. Arbitrary
provider sampling is broadened, while preceding engine/scenario cases remain
preserved. `EngineFact` and `EngineTheory` exclude unsupported methods with exact
engine names and a nonempty `Reason`. `EngineInlineData`, `EngineMemberData`, and
`EngineTheoryDataRow` express justified variant exclusions. Explicit method
exclusions omit cases before fixture construction; row exclusions apply when
xUnit enumerates data. Genuinely empty or unexpectedly over-filtered data fails
under immediate and delayed enumeration instead of disappearing or becoming a
skip. Discovery does not pre-read member factories to filter ownership and then
enumerate them again. The shared ownership guard rejects a method or variant
whose combined exclusions leave only one executable project. This boundary is
enforced through existing exclusion metadata; no `OnlyEngines` API is added.

Every common suite retains a concrete structural owner on every engine, even
when all its methods are explicitly excluded there. For example, the SQLite
server-query-plan wrapper remains structurally owned while its unsupported
method is excluded. The ownership guard must not equate no runnable methods with
a missing concrete suite.

Fixture-owned provider-local feature suites use ordinary `Fact`, `Theory`,
`InlineData`, and `MemberData` with scenario-only arguments. Their concrete
fixture supplies the owner, so an engine-looking string payload is not a selector.
MySQL-family bodies and exclusive helpers compile once in a local nongeneric
abstract suite with exact MySQL and MariaDB fixture leaves. The MySQL-only NO PAD
regression is declared only on the MySQL leaf. These feature suites do not derive
from common test-bearing suites and do not repeat their retained common methods.

All seven local MySQL-family bases live in their own `*TestBase.cs` file, with
one type per concrete engine file. Existing named collection declarations are
explicit on each leaf rather than inherited from its base. xUnit supports the
previous inherited declaration; explicit placement makes local resource
ownership visible in Rider. It does not introduce a collection where none
existed: `ScopeAliasRowset` retains its independent class fixtures. The engine
leaf documentation describes a provider-local family rather than a shared
specification-library contract.

The local audit examines effective inherited methods on each concrete leaf and
retains declarations from its provider assembly, including local abstract
families. It does not misclassify inherited specification-library declarations.
For ordinary local member data, the audit preserves explicit `MemberType` and
otherwise initializes it from the concrete leaf before traversing inherited
members, matching xUnit's public metadata contract. Invalid inherited annotations
or empty member sources cannot evade the audit through an empty concrete leaf.

Every local suite now follows the fixture-owned contract. The 17 formerly legacy
suites contain 35 test methods and 57 engine/scenario variants; the migration
removes only selector arguments and retains every behavioral variant under its
exact engine fixture. The four `Provider*` attributes, two custom discoverers,
annotation validation APIs, and legacy ownership branches are removed rather
than maintained as a second annotation style.

`ProviderEngineOwnership` retains one immutable catalog of exact engine names,
marker types, and executable assembly names. Known-engine checks, executable
membership, owned-engine enumeration, expected closed model-collection fixture
types, and the common-coverage executable count derive from this catalog.
Discovery and ownership guards use its explicit assembly lookup.
Server lookup and executable metadata checks consume its current-assembly
lookup. These are active ownership functions, separate from selecting a test's
engine. The catalog rejects unknown engines, unknown executables, and missing
current-assembly context instead of retaining a compatibility forwarding type.

Resolving a fixture's engine verifies ownership against the actual suite
assembly. A caller may supply a different assembly to the local audit, so the
guard also verifies that supplied audit owner against the resolved engine. The
foreign-audit-owner regression protects this boundary. Only the redundant
unknown-engine branch is removed after successful resolution. Arbitrary valid
fixture marker types still resolve through their static engine name; the
built-in marker catalog is expected registration metadata, not an additional
restriction on fixture marker identity.

The five `ProviderAnnotationTests` methods perform executable-wide metadata
audits once per executable. Explicit MariaDB exclusions explain that MySql
executes the same assembly audit. Both sealed wrappers and exact engine fixture
registrations remain structurally checked, including the full MySQL/MariaDB
owned-engine inventory. The baseline comparison permits exactly five named
duplicate metadata cases to disappear; every behavioral scenario remains covered.

The fixture-constructor guard uses public xUnit metadata for the actual leaf and
its resolved class, collection, and assembly mapping chain. It models exact type
lookup, supported generic registration normalization, test-class built-ins and
default/optional/parameter-array fallbacks. Fixture constructors instead resolve
their built-ins or parent-scope dependencies, with no same-level dependency or
test-argument fallback. An assignable registered type or nullable annotation
cannot replace a missing required exact fixture. Inherited collections and
nested-class boundaries use xUnit's public collection factory. Auditing includes
registered fixtures unused by the test constructor and initializes no resources.
This contract covers the repository's ordinary reflection runner, not a custom
activator or Native AOT runner.

Every provider assembly owns its assembly fixture registration and public named
collection definitions. `TestDatabaseServers` lazily starts one container per
owned server engine and disposes it after that assembly's collections finish.
A complete MySql run uses two containers, PostgreSql and SqlServer each use one,
and Sqlite uses none. The shared library does not hold a static current fixture
that could cross assembly lifetimes. Database creation asynchronously resolves
the executing assembly fixture through xUnit's current class or test context.

Existing class and collection fixture lifetimes retain their database ownership.
The MySql `Model compatibility` collection registers both closed
`ProviderFixture<ModelCompatibilityDatabase, MySqlEngine>` and
`ProviderFixture<ModelCompatibilityDatabase, MariaDbEngine>` types. Common model
suites receive their exact collection fixture without a class fixture that would
shadow it, preserving serialized collection reuse. Local model suites receive
their exact closed engine-bound collection fixtures as well. The bare
`ModelCompatibilityDatabase` registrations in MySql and SqlServer are removed
after their direct constructor consumers migrate; each retained engine-bound
fixture still creates and owns its resource at the existing lifetime. Tests may
share their fixture's database with isolated data;
this is not a one-database-per-test guarantee.
`ProviderCollectionFixtureContract` compares the complete
`ICollectionFixture<>` registration set against the catalog's exact expected
closed engine-bound fixtures. Missing or foreign fixtures, wrong resources,
restored bare `ModelCompatibilityDatabase`, and unrelated registrations fail
without resource construction. Filtering only recognized fixtures would hide
extra registered resources. The pinned xUnit runner initializes all closed
collection registrations when the collection has an eligible case, even if
no test constructor requests a fixture; wholly statically skipped collections
do not force that construction.

Each provider's `Allocation measurements` collection retains disabled
parallelization. The unit project
owns its local measurement definition as well. A unit metadata guard requires
exactly one local definition for each used named collection and retains
measurement isolation. Collection and
assembly initialization, cleanup, and discovery are not supported contexts for
the database helper's contextual fixture lookup.

### Consequences

- Good, because each provider project reports only its own tests, with no
  skipped rows from other providers.
- Good, because a change to a shared behavior test applies to every qualified
  database without source duplication.
- Good, because common source files compile once and appear under one project
  in Rider, while exclusive tests remain visible under their provider.
- Good, because the fixture binds one immutable engine, and a new ordinary
  common case applies to every engine without repeated provider row lists.
- Good, because executable ownership keeps exclusive methods and helpers visible
  under their provider project, including family bodies without duplication.
- Bad, because every applicable provider needs a concrete subclass when a
  common suite is added, and fixture registrations must remain assembly-owned.
- Bad, because provider test attributes are test infrastructure that must stay
  compatible with the pinned xUnit version.
- Bad, because explicit exclusions and constructor guards require separate
  positive and negative qualification for shared and local fixture-owned cases.

### Confirmation

Run live-provider cases on each provider project present in this revision; a
filtered run does not establish coverage for a provider introduced later.

- Build `Doka.EntityFrameworkCore.NestedSet.slnx` in Release with warnings as
  errors and expect no compiler or analyzer diagnostics.
- Inspect the solution and project references: the specification project must
  be a non-runnable, non-packable library; common sources must compile only there;
  unit and provider projects must reference it without linked `Compile` items.
- Discover tests in the unit and four provider runner projects and compare their
  engine/scenario identities with the preceding layout, normalizing only the
  intentional concrete namespace and removed engine argument. Every preceding
  behavioral case must remain covered, with additional common engine cases
  expected. Reconcile exactly five duplicate MariaDB metadata cases separately;
  their removal must not hide a behavioral omission. The
  specification library must not appear as a runner. Every common and nested
  suite must have one concrete owner per engine, including wholly excluded suites.
- Repeat provider-only discovery with xUnit theory pre-enumeration disabled;
  justified method exclusions must remain absent, row exclusions must preserve
  each eligible variant, and intentionally empty or unexpectedly over-filtered
  data must fail. Instrument member factories to reject an extra enumeration
  performed solely for ownership filtering.
- Run the five EF test projects and expect every discovered case to pass with
  zero skipped tests. A provider-only case must not appear in another project's
  test list.
- Run positive and negative engine and annotation guards. Ordinary shared cases
  must run by default on each engine; missing exclusion reasons, unknown engine
  names, contradictory fixtures, and foreign fixture owners must fail. Local
  cases must use exact engine fixtures and neutral scenario data; no obsolete
  Provider annotation, discoverer, or row-selection branch may remain.
- Verify common ownership after combining method and data exclusions. A variant
  limited to SQLite or to the MySQL/MariaDB executable must fail the shared guard.
  Ordinary local fixture-owned cases and engine-looking payloads must pass;
  missing or foreign fixtures, explicit engine selectors, invalid inherited
  family annotations, and empty member sources must fail. Preserve explicit
  cross-type `MemberType` and resolve unset sources through the actual leaf.
- Inspect exclusive source ownership and inherited local discovery. Each family
  body must compile once in its provider project; the NO PAD method must exist
  only on the MySQL leaf. The five metadata methods must be discovered once per
  executable while their full inventory still checks both MySQL-family engines.
- Run `ProviderFixtureContractTests` and the concrete-suite guard. Exact class,
  collection, and assembly registrations and supported test-argument fallbacks
  must pass. Wrong leaf constructors, assignable-only substitutions, invalid
  shapes, same-level fixture dependencies, fixture optional/default fallbacks,
  and unsupported required services must fail without initializing resources.
- Inspect and exercise assembly and named collection registrations. The two
  MySql engines must retain separate lazy servers, and interleaved provider
  assemblies in one process must not select or dispose another assembly's
  fixture. Existing database isolation and allocation collection parallelism
  must remain intact. Both MySql model collection fixtures must remain at
  collection scope, with no class fixture shadow; their resources must initialize
  and dispose once at the existing lifetime.
- Run catalog and complete collection-set regressions. Actual expected sets,
  including both MySql engine fixtures, must pass. Missing or foreign fixtures,
  wrong resources, unexpected registrations, and a restored bare model resource
  must fail without resource activation. Engine names, marker types, owned
  executables, expected registrations, and shared-coverage counts must derive
  from the same catalog. Retain rejection of a foreign caller-supplied audit
  assembly even when the suite's actual fixture owner is valid.
- Reconcile all 57 migrated local database variants independently of unit-case
  totals. The 64 retired unit cases exercise removed Provider annotation,
  selector-row, or discoverer mechanisms; retain their explicit retirement
  inventory. Preserve meaningful engine-ownership and current-assembly isolation
  checks, fixture initialization/disposal isolation, named collection checks,
  and all live neutral discovery and guard regressions. Replacing obsolete
  mechanism tests must not hide a missing database behavior case.
- Inspect shared probes and provider folders. Cross-suite consumers must use
  standalone internal helpers directly; private observers remain private.
  For the probe extraction, preserve context/model identities, command
  correlation, observer disposal,
  measurement warmups, budgets, and allocation collection isolation.
- Verify seven independent local MySQL-family base files and explicit existing
  leaf collections. Preserve ScopeAliasRowset class ownership without adding a
  named collection and keep NO PAD cases only on the MySQL leaf. Requalify the
  moved provider-local model namespaces and renamed context references against
  preserved entity short names, physical mappings, and model-cache warmups.
- Verify all six query-plan evidence call paths use the independent helper,
  preserving output-directory overrides, file names, double-newline separators,
  and asynchronous cancellation behavior.

## Pros and Cons of the Options

### Referenced specification suites with fixture-owned engine identity

- Good, because common assertions compile once and have one Rider source owner,
  while concrete provider projects retain direct execution and container scope.
- Good, because fixture identity removes repeated engine arguments and makes
  ordinary common cases apply to all engines by default.
- Bad, because inherited discovery, concrete suite completeness, exact fixture
  types, and per-assembly registration need explicit regression coverage.

### Referenced specification suites with explicit engine rows

- Good, because the existing row lists make the historical engine matrix visible
  and require little change to inherited test bodies.
- Bad, because common data repeats engine identity, arbitrary provider sampling
  can omit supported cases, and one MySql leaf conflates MySQL and MariaDB.

### Separate provider test projects compiling shared integration sources

- Good, because project boundaries support direct provider-specific execution
  without another runner filter.
- Good, because the defining and executing assembly are the same, simplifying
  provider selection and fixture ownership.
- Bad, because each provider compiles the shared fixtures and tests and Rider
  repeats the same files across four project trees.

### One project with provider traits and runner filters

- Good, because the source and binary compile once.
- Bad, because Rider still shows one large project and the selected filter can
  omit a provider case from a supposedly complete run.

### Copy integration tests into provider-specific source trees

- Good, because every project contains only the files it executes.
- Bad, because duplicated assertions and fixtures can drift across providers.

## More Information

This decision concerns test organization only. It does not alter the production
package layout or the runtime provider contract. Ordinary migrations and the
optional SafeMigrations adapter retain their own existing test projects.

### Re-evaluation Triggers

- A new supported engine cannot obtain one immutable fixture identity and a
  complete common suite owner without duplicating test bodies.
- An engine capability changes so a recorded exclusion reason is no longer
  valid, or an ordinary common case needs a newly justified exclusion.
- Combined method and variant exclusions leave only one executable owner, or
  an exclusive helper no longer has real shared consumers; move that source
  to its owner rather than add a hidden positive engine list.
- xUnit changes inherited discovery, contextual fixture resolution, or named
  collection registration in a way that breaks provider assembly ownership.
- The pinned reflection runner changes constructor built-ins, fallback order,
  exact generic lookup, or fixture parent dependency rules; requalify the guard
  against the new primary source before treating its result as authoritative.
- xUnit changes inherited local method metadata or `MemberType` initialization;
  requalify neutral local and abstract-family ownership in both enumeration modes.
- Adding a common suite no longer produces complete concrete provider ownership
  under the discovery comparison gate.
- A fixture-owned local case needs a separate annotation selection path, or a
  shared probe loses its shared consumers; reassess the actual ownership need
  before introducing another annotation style or keeping a common helper.
- xUnit changes collection-fixture enumeration or eager activation; requalify
  complete registration-set guards and resource lifetime against its source.
- Adding an engine requires a second name, marker, or executable inventory;
  keep the catalog authoritative before changing fixture or coverage guards.
- A provider-local context or entity rename changes conventional physical names
  or measured cache warmups; qualify the mapping rather than assuming namespace
  and short-name changes are interchangeable.

### Decision History

- 2026-09-25: Decision recorded with status proposed.
- 2026-09-25: The owner requested separate provider projects with shared test sources; implementation does not by itself record formal acceptance.
- 2026-09-26: Review added fail-visible fact validation and raw metadata guards; extensibility sources were updated to the pinned xUnit version.
- 2026-09-27: The owner requested one referenced specification library with thin provider suites and provider-only source ownership to avoid repeated linked files in Rider. The proposed choice was revised from linked compilation; no formal acceptance transition is recorded.
- 2026-09-27: The revised design preserves engine eligibility, assembly container lifetimes, class or collection database ownership, and allocation collection parallelism; confirmation requires fresh builds and complete runner discovery and execution.
- 2026-09-27: Review identified partially owned local row sources and a fixture check that did not prove concrete constructor resolution. The owner selected fixture-owned immutable engines with nongeneric common suites, separate MySQL/MariaDB leaves, and engine-neutral default-all cases with reasoned exclusions; the prior referenced explicit-row strategy remains an alternative.
- 2026-09-27: Exact public xUnit metadata, every raw local row, fixture parent dependencies, and preserved collection resource lifetimes are confirmation boundaries. No formal acceptance transition or new successful test count is recorded by this amendment.
- 2026-09-27: Provider-exclusive methods and helpers move to their executable owners, including shared local MySQL-family bodies with exact fixture leaves. Shared method and variant ownership combines all exclusions; local audits include inherited declarations and xUnit-compatible MemberType initialization. Five duplicate MariaDB metadata audits are intentionally removed while both engine inventories remain checked. Status, original date, and prior history remain unchanged; this amendment records no new successful run or formal acceptance.
- 2026-09-27: The 17 remaining local legacy suites migrate completely to fixture-owned ordinary annotations, preserving their 35 methods and 57 engine/scenario variants. The four Provider attributes, two discoverers, obsolete guard branches, and two unbound model collection registrations are removed; active engine/executable ownership becomes ProviderEngineOwnership. Shared probes become standalone internal helpers, and provider-only files and names follow feature ownership. The 64 obsolete mechanism unit cases are retired separately from behavioral qualification. Original date, proposed status, and prior history remain unchanged; this amendment does not claim a successful qualification run or formal acceptance.
- 2026-09-27: Local family bases and leaves use one type per file with explicit existing leaf collections; ScopeAliasRowset retains class fixtures. Provider-local namespaces and feature names are aligned while entity short names and physical mappings remain intact. One immutable engine/marker/executable catalog supplies ownership and expected registrations; complete collection-set guards reject extra resources. The independent audit-owner check remains, and the redundant unknown-engine branch is removed. Shared query-plan evidence moves into its own helper with all six call paths preserved. Original date, proposed status, and prior history remain unchanged; this amendment records no new successful run or formal acceptance.

- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed the referenced specification library, fixture-owned engine suites, provider-local source ownership, and registration and discovery regression specifications against the linked repository evidence.

### Implementation References

- [Engine and executable ownership](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Providers/ProviderEngineOwnership.cs)
- [Shared and local ownership guard](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Providers/ProviderTestContract.cs)
- [Ownership guard regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/ProviderTestContractTests.cs)
- [Immutable provider fixture](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Providers/ProviderFixture.cs)
- [Engine metadata selection](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Providers/EngineTestSelection.cs)
- [Engine exclusions and discovery](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Providers/EngineTestAttributes.cs)
- [Public constructor contract guard](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Providers/ProviderFixtureContract.cs)
- [Constructor contract regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/ProviderFixtureContractTests.cs)
- [Complete collection registration guard](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Providers/ProviderCollectionFixtureContract.cs)
- [Collection registration regressions](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/ProviderCollectionFixtureContractTests.cs)
- [Specification library](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests.csproj)
- [Common abstract suites](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration)
- [EF unit project](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests.csproj)
- [MySQL and MariaDB project](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests.csproj)
- [PostgreSQL project](../../tests/Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests/Doka.EntityFrameworkCore.NestedSet.PostgreSql.Tests.csproj)
- [SQL Server project](../../tests/Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests/Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests.csproj)
- [SQLite project](../../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests.csproj)
- [Concrete provider suite](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Concurrency/NativeTrackedIdentityGuardTests.cs)
- [Concrete MariaDB suite](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/MariaDb/Concurrency/NativeTrackedIdentityGuardTests.cs)
- [Provider-local collation family base](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Concurrency/NativeCollationTestBase.cs)
- [MySQL collation leaf and exclusive NO PAD case](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Concurrency/NativeCollationTests.cs)
- [MariaDB collation family leaf](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/MariaDb/Concurrency/NativeCollationTests.cs)
- [Provider-local concrete TPC collation family base](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/ModelCompatibility/Inheritance/TpcCollationTestBase.cs)
- [Provider-local Doka cache family base](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Ordering/Tracking/DokaCacheTestBase.cs)
- [Provider-local Scope alias family base](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Concurrency/TreeRegistry/ScopeAliasRowsetTestBase.cs)
- [SQLite wide registry rowsets](../../tests/Doka.EntityFrameworkCore.NestedSet.Sqlite.Tests/Concurrency/TreeRegistry/JsonRowsetTests.cs)
- [SQL Server wide registry rowsets](../../tests/Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests/Concurrency/TreeRegistry/WideRowsetLockTests.cs)
- [SQL Server Scope validation](../../tests/Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests/Concurrency/TreeRegistry/ScopeValidationTests.cs)
- [SQL Server row-version tests](../../tests/Doka.EntityFrameworkCore.NestedSet.SqlServer.Tests/Ordering/Tracking/RowVersion/RowVersionTests.cs)
- [Shared query-plan evidence](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Indexes/QueryPlanTestSupport.cs)
- [Provider assembly registration](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Infrastructure/TestAssembly.cs)
- [Provider collection definitions](../../tests/Doka.EntityFrameworkCore.NestedSet.MySql.Tests/Infrastructure/TestCollections.cs)
- [Assembly server lifetime](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Infrastructure/Database/TestDatabaseServers.cs)
- [Engine ownership tests](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/ProviderEngineOwnershipTests.cs)
- [Shared native guard probe](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Concurrency/NativeGuardProbe.cs)
- [Shared refresh and query-shape probe](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Ordering/Tracking/ScaleProbe.cs)
- [Shared command observation](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Ordering/Tracking/ScaleCommand.cs)
- [Shared annotation guard](../../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Providers/ProviderAnnotationTests.cs)

### Sources

- [xUnit 4.0.1 test constructor resolution](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Runners/Reflection/XunitTestClassRunnerBase_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 fixture parent mapping](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Runners/Reflection/XunitTestCollectionRunnerBaseContext_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 public collection factory](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Framework/TestCollectionFactoryBase_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 collection inheritance declaration](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/CollectionAttribute_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 complete collection fixture metadata](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Utility/ExtensibilityPointFactory.TestCollection_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 collection initialization and skipped-case boundary](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Runners/Reflection/XunitTestCollectionRunnerBase_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 default type activation](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Framework/TypeActivator_reflection.cs) (primary source; retrieved 2026-09-27)
- [Microsoft provider specification testing](https://learn.microsoft.com/en-us/ef/core/providers/writing-a-provider#the-ef-core-specification-tests) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 shared fixture suite](https://github.com/dotnet/efcore/blob/v10.0.12/test/EFCore.Specification.Tests/Query/QueryTestBase.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 concrete provider suite](https://github.com/dotnet/efcore/blob/v10.0.12/test/EFCore.SqlServer.FunctionalTests/Query/NorthwindWhereQuerySqlServerTest.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 command interceptor contract](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Diagnostics/IDbCommandInterceptor.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 interceptor and provider-cache options](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Infrastructure/CoreOptionsExtension.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 context model cache identity](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Infrastructure/ModelCacheKey.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 physical table naming](https://github.com/dotnet/dotnet/blob/95017c711e6afc1085133d440e42b4bd78155701/src/efcore/src/EFCore.Relational/Extensions/RelationalEntityTypeExtensions.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 conventional DbSet table naming](https://github.com/dotnet/dotnet/blob/95017c711e6afc1085133d440e42b4bd78155701/src/efcore/src/EFCore.Relational/Metadata/Conventions/TableNameFromDbSetConvention.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 conventional discriminator values](https://github.com/dotnet/dotnet/blob/95017c711e6afc1085133d440e42b4bd78155701/src/efcore/src/EFCore/Metadata/Conventions/DiscriminatorConvention.cs) (primary source; retrieved 2026-09-27)
- [EF Core 10.0.12 short CLR names](https://github.com/dotnet/dotnet/blob/95017c711e6afc1085133d440e42b4bd78155701/src/efcore/src/EFCore/Metadata/IReadOnlyTypeBase.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 standard InlineData enumeration](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/InlineDataAttribute_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 standard Theory defaults](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/TheoryAttribute_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 delayed theory execution](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/ObjectModel/Reflection/XunitDelayEnumeratedTheoryTestCase_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 inherited method discovery](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/ObjectModel/Reflection/XunitTestClass_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 concrete type discovery](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Framework/TestFrameworkDiscoverer.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 member-data resolution](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Attributes/MemberDataAttributeBase_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 type-aware MemberData metadata initialization](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Utility/ExtensibilityPointFactory.TestMethod_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 inherited class-fixture metadata](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Utility/ExtensibilityPointFactory.TestClass_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 immediate and delayed theory discovery](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Framework/TheoryDiscoverer_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 fixture resolution](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Utility/FixtureMappingManager_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 assembly fixture lifetime](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Runners/Reflection/XunitTestAssemblyRunnerBase_reflection.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 contextual fixture access](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/TestContext.cs) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 non-runner library references](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/src/xunit.v3.core/Package/buildTransitive/xunit.v3.core.mtp-off.targets) (primary source; retrieved 2026-09-27)
- [xUnit 4.0.1 fact discovery API](https://api.xunit.net/v3/4.0.1/v3.4.0.1-Xunit.v3.FactDiscoverer.Discover.html) (primary source; retrieved 2026-09-26)
- [xUnit 4.0.1 theory discovery API](https://api.xunit.net/v3/4.0.1/v3.4.0.1-Xunit.v3.TheoryDiscoverer.Discover.html) (primary source; retrieved 2026-09-26)
- [xUnit 4.0.1 data attribute API](https://api.xunit.net/v3/4.0.1/v3.4.0.1-Xunit.v3.DataAttribute.GetData.html) (primary source; retrieved 2026-09-26)
- [xUnit 4.0.1 attribute constructor error handling](https://github.com/xunit/xunit/blob/v3-4.0.1/src/xunit.v3.common/Extensions/ReflectionExtensions_reflection.cs#L69) (primary source; retrieved 2026-09-26)
- [xUnit 4.0.1 discovery error cases](https://github.com/xunit/xunit/blob/v3-4.0.1/src/xunit.v3.core/Framework/Reflection/XunitTestFrameworkDiscoverer_reflection.cs#L47) (primary source; retrieved 2026-09-26)
- [xUnit 4.0.1 release notes](https://xunit.net/releases/v3/4.0.1) (primary source; retrieved 2026-09-26)
- [Raw custom attribute metadata](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.customattributedata?view=net-10.0#remarks) (primary source; retrieved 2026-09-26)
- [xUnit source location rule xUnit3003](https://xunit.net/xunit.analyzers/rules/xUnit3003) (primary source; retrieved 2026-09-25)
- [xUnit VSTest RunSettings and theory pre-enumeration](https://xunit.net/docs/config-runsettings) (primary source; retrieved 2026-09-25)
