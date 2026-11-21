# Run and extend repository tests

Use the SDK and dependency graphs declared by this checkout. Run the commands
in [Contributing](../CONTRIBUTING.md#build-and-test) before using `--no-build`.
The [support matrix](support-and-qualification.md) owns supported package and
engine versions. This guide describes how to select and maintain checks; it is
not an independently maintained list of every test or a claim of a fresh run.

## Test project ownership

The Core project tests EF-independent bounds and identity predicates. The EF
Unit project owns model, expression, planning, and guard checks without a live
server. The four provider projects execute native behavior; ordinary migrations
and optional SafeMigrations have separate projects. Benchmark contract tests
exercise fixture and lifecycle correctness without making benchmark timing an
acceptance threshold. The project list is maintained in
[Contributing](../CONTRIBUTING.md#build-and-test).

The referenced Specification project is a non-runnable, non-packable library.
Common test suites are public abstract classes. Each applicable concrete
provider suite inherits its shared body; running the library itself does not
execute those tests. No provider project compiles a second linked copy of the
common source. Exclusive test bodies and helpers stay in their provider project.

Common suites receive an immutable `IProviderFixture<TResource>` view through
their protected constructor. Concrete leaves expose the exact closed
`ProviderFixture<TResource, TEngine>` registered by their owner and forward it
to the shared base. The fixture supplies `Engine` and `Value`; scenario data does
not select a provider. MySql contains separate MySQL and MariaDB leaves, with
MariaDB under its own namespace. One executable owns both engines.

Ordinary facts and theories run the same engine-neutral input variants on every
applicable leaf. Engine-specific attributes carry exact exclusions and a reason.
Wholly excluded families have no leaf; partially excluded families retain their
applicable cases. Every shared variant must retain more than one executable
provider owner, counting MySQL/MariaDB as one. Provider-exclusive cases belong
in that provider instead of excluding every other executable.

Discovery guards inspect inherited declarations and effective rows without
constructing database resources. They reject unknown engines, foreign audit
owners, missing or extra leaves, invalid annotations, unexpected empty sources,
and exclusions that collapse common coverage. Explicit `MemberType` and normal
leaf/base lookup remain valid; a data factory is not pre-enumerated a second
time just to decide eligibility.

## Fixture and evidence ownership

Each executable owns its assembly fixture. It starts at most one container per
owned server engine, lazily, and releases it when that assembly's collections
finish. SQL Server, PostgreSQL, MySQL, and MariaDB use their respective shared
servers; SQLite needs no container. Separate assemblies do not share a static
server singleton even inside one test process.

Server reuse and database reuse have different scopes. A class or collection
owns its database resources; individual cases isolate their data within that
contract. A fixture registration is not a request to serialize unrelated cases.
Independent collections remain parallel. The allocation collection and specific
64-connection scenarios retain their documented, narrow isolation boundaries.

Model collections register exact engine-bound fixtures. The complete set of
`ICollectionFixture<>` registrations must match the owner catalog: missing,
foreign, unbound, wrong-resource, and unrelated additional registrations are
rejected before any fixture starts. Checking only recognized registrations
would conceal unwanted resources that xUnit still initializes.

Constructor checks follow public xUnit metadata through class, collection, and
assembly ownership. Exact fixture identity, supported built-ins and argument
fallbacks matter; assignability or nullable annotations do not resolve a missing
required fixture. Fixture constructors have their own parent-only dependency
rules. A fixture at the same scope is not an ordinary constructor fallback.

The engine catalog has one owner for engine name, marker type, executable, and
expected closed registrations. Do not add a separate engine list to a guard.
A valid engine for a suite does not authorize a different audit assembly.
The five executable-wide annotation audits run once per executable; explicit
MariaDB exclusions avoid auditing that same assembly twice while both engine
leaves and all native scenarios remain present.

Query-plan fixtures own immutable seeds separately from resettable behavior
fixtures. Each scenario opens a fresh context. Shared statistics refresh,
actual-plan explanation, parameter cloning, and evidence writing belong to
`QueryPlanTestSupport`, not an unrelated test class. Preserve exact statements,
native parameter facets, case identity, and deterministic evidence filenames.
An interrupted plan must not be accepted as complete evidence. Failure output
must preserve the original operation error even when secondary diagnostics fail.

Keep index eligibility separate from an optimizer's ordinary cost choice.
The PostgreSQL nullable-parent controls verify catalog definitions and exact
results, retain ordinary `EXPLAIN` output as observations, and use a rolled-back
transaction-local planner control only to prove eligible access. Missing or
wrong-filter indexes and session restoration have separate rejection/cleanup
controls. A forced eligible plan is not native performance evidence.
Prepared-query controls must also restore session settings after execution
cancellation and release only statements they created; a name collision must
preserve the caller's existing statement.

Pool lifecycle tests own only their own connection pools. Initialization,
normal disposal, and failed deletion must release those resources while a peer
pool's active/idle sessions and rows remain usable. Server-wide pool clearing,
raised limits, disabled pooling, or a private server-response deadline do not
prove that ownership contract.

## Choose the right check

| Changed boundary | Relevant checks |
| --- | --- |
| Bounds, ancestry, and complete identity | [Core examples and generated properties](../tests/Doka.NestedSet.Tests/Unit); equal coordinates in foreign trees/scopes remain distinct |
| Model roles, converters, shadow/indexer/field values, and framework binding | [EF Unit mapping/binding guards](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Configuration), then native model and generated-model cases |
| Insert, move, delete, order, or coordinated save | [Shared native contracts](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration): success, rejection, cancellation, caller transaction/savepoint, callback, exact database and tracker rollback cases |
| Query composition | [Deferred expression controls](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Queries/Composition) and [native composition](../tests/Doka.EntityFrameworkCore.NestedSet.Specification.Tests/Integration/Querying/Composition): rejected provenance, exact bags, visibility, direct paths, aliases, cardinality, and work evidence |
| Migration or optional adapter | [Ordinary migrations](../tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests) and [optional adapter cases](../tests/Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests): actual empty/populated upgrades, physical schema, data, history, replay, and isolated rejection |
| Capture/refresh/materialization hotpath | [Capture/recovery controls](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Execution), independent semantic oracle, unchanged work/resource controls, then source-matched diagnostic allocation and [BenchmarkDotNet comparison](../benchmarks/Doka.EntityFrameworkCore.NestedSet.Benchmarks) |
| Sample entry or process boundary | [Sample cases](../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Samples): in-process scenarios and persisted results; separate launch, captured-stream, cancellation, termination, and reaping where the child process is the subject |

These categories are selection guidance. Test source is the executable
regression specification; feature guides and XML documentation remain the
public contract owners. Neither a generated case count nor a large inventory
proves all future inputs correct.

## Write one coherent scenario

Name actual tests `Method_Scenario_ExpectedBehavior`. Preserve their discovered
engine and argument variants when renaming or splitting source. Update `nameof`,
reflection lookup, filters, partitions, and documentation together; compare old
and new discovery through an explicit mapping rather than accepting a smaller
case list. Provider-neutral data still contains only scenario inputs.

Use one actual Arrange, Act, Assert sequence with blank lines between phases.
A coherent rollback, retry, or concurrency contract can require several
necessary steps in its one Act phase. Independent later actions belong in
separate cases. Multiple assertions about the same result remain appropriate.
Helpers should expose meaningful setup, execution, and independent oracle work;
a helper created only to meet a line count does not clarify the contract.

Use explicit synchronization to establish contested states. Forward work uses
the runner's cancellation token. Necessary rollback, evidence preservation,
and resource release remain protected once work has failed. A sleep is not
proof that another writer reached a lock, and a fixed local deadline is not a
functional oracle. Actual provider timeout-contract tests keep the timeout they
intentionally exercise. Shared functional fixtures remove implicit SQL and native
transaction speed limits; this policy does not change application contexts.

Capacity cases check exact persisted populations, independent geometry oracles,
registry revisions, full validation, and bounded work/resources. Nested profile
setup, query, and replay contexts use the same functional-fixture policy as their
caller. Preserve explicit timeout-contract overrides, application-context isolation
controls, and the outer runner's hang protection. See the
[capacity procedure](performance.md#real-relational-capacity-cases).

## Property-based regressions

The existing FsCheck properties complement provider regressions rather than
replacing SQL, collation, transaction, concurrency, or capacity evidence. Pass
generated inputs to the property so a failure can be shrunk. Create fresh
mutable state inside each invocation; shrinking re-executes it.

Keep ordinary runs randomized. Reproduce a failure with its reported seed and
retain the minimal failing input as a deterministic regression. Do not replace
all generated input with a single internal seed or impose a wall-clock timeout.
The existing bulk properties bound a generated forest to 64 nodes; dedicated
capacity cases own larger physical populations. Core properties exercise the
Int64 bounds domain and complete scoped/unscoped identity independently of
EF; bulk properties compare planned geometry with input adjacency and reject
repeated entities and assigned keys before staging.

## Provider discovery in Rider

Select **Settings/Preferences > Build, Execution, Deployment > Unit Testing >
xUnit.net > Test discovery > Test runner** when checking provider eligibility.
Metadata discovery lists inherited methods without executing the xUnit engine
exclusions. Running a whole provider project refreshes the list through the
runner. An IDE entry or a shared-base stack frame alone does not identify the
concrete provider that executed a case.

SQL Server cases stay discoverable on local ARM but visibly skip before
container startup under the existing unsupported-host policy. Metadata-only
checks still run. Native Linux x64 qualification must execute applicable cases;
container startup errors on eligible hosts are failures, not platform skips.
See the [supported-host boundary](support-and-qualification.md#provider-and-engine-matrix).

## Evidence limits

For a fresh run retain source revision/hashes, package graph, SDK/runtime,
provider/image, actual schema/population, command, filter, and per-project TRX.
Verify nonzero discovery and complete applicable variants. A source link, an
old report, or a command in a guide does not establish a new successful run.

The Unit collector-sensitive partition runs without instrumentation; its
complement runs under the selected collector. Both are required, disjoint, and
complete. Do not turn a collector-owned root into a product retention failure
or omit a partition to obtain green output. See the
[collector boundary](performance.md#coverage-collector-18110-and-unloadability).

The four focused qualification guides preserve different dated observations:
[query/native work](qualification/query-composition.md),
[construction/read/materialization](qualification/query-performance.md),
[provider/migrations](qualification/provider-migrations.md), and
[Unicode identities](qualification/unicode-identities.md). Their old pass counts,
local-source packages, and host exclusions remain historical. They do not
qualify a new source graph, native SQL Server, or hosted publication.

No finite matrix detects every future regression. A changed mapping, compiler,
provider, framework patch, transaction setting, or operation requires relevant
fresh positive/negative checks. Diagnostic runtime and sampled allocation
attribution are not hardware-dependent release thresholds.
