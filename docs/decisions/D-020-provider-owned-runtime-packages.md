---
id: D-020
status: implemented
date: 2026-10-26
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Common EF kernel and four explicitly selected native provider packages"
supersedes: [D-002]
superseded-by: []
amends: [D-019]
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-020 -- Own native integration in four provider packages

## Context and Problem Statement

[D-002](D-002-package-boundaries.md) kept pure bounds separate from one EF
package. The common EF package initially avoided every concrete provider
reference. Its supported native behavior now includes different registry locks,
rowset encodings, session prerequisites, generated-index expressions, collation
rules, query translation, and physical query strategies. These responsibilities
extended beyond the provider folder into execution, model conventions, storage,
and queries.

[D-019](D-019-optional-ancestor-index-and-source-binding.md) also used reflected
optional public Doka APIs to preserve that dependency choice. The provider APIs
are published in the approved Doka 10.6.2 baseline. Keeping the integration
optional in the common kernel now entails discovery, signature, lifetime, and
fallback machinery that a directly typed adapter does not need.

The owner chose to make the installation/registration break during the current
feature development. This supersedes the single-EF-package layout in D-002,
while retaining its small ORM-independent package. It amends D-019's provider
binding/legacy availability assumptions, not its opt-in ancestor-index semantics
or its recorded experiments. The owner also explicitly retained `10.1.0` for
this break: package majors follow the .NET family and `11.x` is reserved for
.NET 11. This is a narrowly approved exception to SemVer, not a general rule
permitting breaking minor releases.

## Decision Drivers

- Keep one implementation of structural algorithms, invariants, and transaction coordination.
- Give native SQL, model conventions, and query integration a clear dependency owner.
- Compile against the approved public provider APIs and enforce version ranges through packages.
- Select each adapter explicitly without assembly discovery or a global provider registry.
- Preserve exact configured-provider identity, model visibility, native comparison, and source multiplicity.
- Keep native optimizer shapes, deep/wide work budgets, allocation ownership, rollback, retries, and cancellation.
- Make six-package inspection, consumers, SBOMs, and publication complete without a second tooling framework.

## Considered Options

- Shared EF kernel with four explicit native adapter packages
- Retain one EF package with internal provider folders
- Separate complete hierarchy engines per provider

## Decision Outcome

Chosen option: "Shared EF kernel with four explicit native adapter packages", because
native integration now has enough distinct behavior and typed dependencies to
justify a project and package boundary, while hierarchy semantics still require
one common implementation.

`Doka.NestedSet` remains ORM-independent. `Doka.EntityFrameworkCore.NestedSet`
remains the common EF kernel. The native packages are exactly
`Doka.EntityFrameworkCore.NestedSet.MySql`, `.Postgres`, `.Sqlite`, and
`.SqlServer`. Each depends on the common EF kernel and its actual native EF
provider; SafeMigrations stays independently optional. MySql supports both
MySQL and MariaDB through Doka only.

The adapter is selected by `UseNestedSets()` inside the native `UseMySql`,
`UseNpgsql`, `UseSqlite`, or `UseSqlServer` callback. The former common
`DbContextOptionsBuilder.UseNestedSets()` overloads are removed. Entity mapping,
model/convention helpers, facade methods, and save coordination remain common.

One internal selected-provider boundary groups the actually consumed query,
model, and native execution operations. It is implemented by the four shipping
adapters through assembly-internal access, not exposed as a public plugin API.
The common options extension owns an immutable selected adapter. Its EF service
identity includes the exact adapter type; registration validates actual native
provider ownership before hierarchy I/O, including supplied/compiled models.
Repeated identical selection is idempotent and conflicting selections fail.

The four adapters depend on exactly the common kernel release they were built
against: `[10.1.0]` for this source. Their overrides and internal calls are a
private ABI, not a public compatibility promise across patches. A small SDK
pack target pins only the generated adapter-to-kernel package edges. Source
`ProjectReference` restore locks retain their ordinary SDK-defined minimum
ranges. The kernel's public-only dependency on `Doka.NestedSet` also remains a
minimum. Applications using several adapters update those adapters together.

Exact NuGet constraints describe the supported graph; they cannot prevent an
application from overriding it with a direct dependency. Such a restore can
report NU1608 instead of failing unless warnings are errors. An incompatible
internal change can then break execution, but a version difference alone does
not prove a runtime failure. Archive inspection requires the exact private
edge, and independent consumer restores treat warnings as errors.

The scoped `INestedSetProviderBinding` factory validates the completed context
options, actual native provider, and exact assembly identities once per context
scope. It returns the existing immutable adapter singleton, without a separate
binding object or a retained context reference. Warm resolution reads the
validated scoped reference rather than repeating provider discovery or metadata
inspection; validation ownership does not make the adapter itself mutable.

MySql uses direct Doka 10.6.2 public source-binding, ordered-join, and engine
APIs. Exact method identities needed for provenance are obtained from typed
methods. Legacy missing-API discovery is removed; non-MySql schema-only ancestor
index diagnosis remains. Generic query algorithms and neutral markers stay
common; native translation and physical policy selection belong to the adapter.

The release set contains six package IDs at one qualified NestedSet version.
Consumers independently restore the ORM-only package, common EF kernel, and
each selected adapter. Inspection rejects a foreign adapter/provider closure.
These consumers are ordinary checked-in C# projects, with the candidate version
supplied as an MSBuild property. Their runner copies reviewed source and
configuration instead of generating C# or project XML. Each restore uses an
independent cache and checks the actual candidate archive bytes.
A seventh consumer compiles the actual FileSystem/Shared sample against
candidate MySql/Sqlite packages and executes it on SQLite. All primary packages
publish in dependency order before any symbol package; each primary package has
its own SBOM and attestation.

The SDK compares the two previously published package IDs against `10.0.0`.
Only the approved options-registration removal is suppressed for this baseline;
unnecessary suppressions are rejected. The four new adapter IDs have no prior
package baseline. This uses SDK package validation rather than a separate API
comparison implementation in release tooling.

The SQL Server adapter declares PKCS 9.0.21 as the first patched 9.x version for
the two reviewed vendor advisories, without an arbitrary upper bound. The
qualification graph uses 10.0.14 as a separate .NET 10 baseline. That dependency
belongs only to the native SQL Client graph, not the common kernel.

SQL-free rowset capacity and identifier delimiting have virtual defaults in the
kernel. Native SQL text remains adapter-owned; SQL Server overrides the default
JSON parameter capacity and validates its distinct collation token grammar.

### Consequences

- Good, because native changes have a compile-time dependency owner and shared hierarchy behavior remains single.
- Good, because applications install their selected adapter without acquiring the other database providers.
- Bad, because installation and options registration change, and the six shipping package/API/lock contracts must stay aligned.
- Bad, because applications must update adapters together to preserve the exact private kernel ABI while also satisfying each native provider's range.
- Bad, because the explicit `10.1.0` SemVer exception requires consumers to read the migration contract instead of inferring registration compatibility from the unchanged major.

### Confirmation

- Build `Doka.EntityFrameworkCore.NestedSet.slnx` in Release with warnings as errors; expect the common packages and all four adapters to build without cyclic references or warnings.
- Inspect the six shipping project/restore/package graphs; expect the common kernel to have no concrete provider dependency, each adapter to own only its selected native provider, and no shipping SafeMigrations dependency.
- Inspect every primary/symbol adapter archive; expect an exact matching kernel dependency and rejection of minimum-only, interval, or wrong-release private edges.
- Run SDK package validation against `10.0.0` for the two existing IDs; expect only the approved registration removal to be suppressed, with other missing APIs and unnecessary suppressions rejected.
- Run options/adapter regressions; expect repeated registration to remain stable and missing, wrong, conflicting, or mismatched load-context identities to reject before hierarchy I/O, including compiled and external service graphs.
- Run shared provider specifications and native query/lock/model/migration tests; expect unchanged source occurrence, visibility, collation, lifecycle, rollback, and finalizer ordering behavior.
- Run the existing deterministic allocation and deep/wide query-work controls on MariaDB/MySQL without relaxing budgets; retain actual before/after evidence separately from architecture claims.
- Run the existing package consumer, archive inspection, SBOM, and offline release-tooling checks; expect six independently verified package roots plus actual FileSystem sample execution, no foreign-provider closure, and complete primary-before-symbol publication ordering.
- Review the upgrade guide and all sample provider callbacks; expect one matching adapter selection while model-level configuration remains common.

## Pros and Cons of the Options

### Shared EF kernel with four explicit native adapter packages

- Good, because provider-specific dependencies and behavior are isolated while the same structural algorithms and shared specifications serve every provider.
- Bad, because cross-assembly internal contracts, adapter API baselines, and release evidence require deliberate coordination.

### Retain one EF package with internal provider folders

- Good, because installation remains one EF package and requires fewer shipping artifacts.
- Bad, because typed provider integration either introduces all provider dependencies into the common graph or retains dynamic binding and scattered native decisions.

### Separate complete hierarchy engines per provider

- Good, because each provider could evolve its complete execution implementation independently.
- Bad, because copied invariants, mutation algorithms, rollback, and query semantics would multiply divergence and regression risk.

## More Information

This is an explicit package/registration compatibility decision, not evidence
that separate assemblies automatically improve CPU time or allocations. Native
query/performance claims still need their own actual qualification.

Model finalization must preserve early key/index budgeting and late PostgreSQL
parent-index work. SQL Server's session guard and SQLite's writer behavior
remain native requirements, not common-provider assumptions. Known reflection
seams for EF's insertion/rollback lifecycle remain governed by D-004; replacing
Doka feature discovery does not remove those separate contracts.

Native Unicode length-policy recognition belongs to the adapter. The existing
late model capture stores only the Scope and TreeId Boolean policy facts;
registry construction reads those primitives without resolving a live provider.
Generic scalar counting stays common. Older supplied/compiled models missing
these facts must be regenerated; missing metadata does not silently authorize
an inferred mapping policy.

Consumer integrity checks compare each supplied candidate archive with the
restored cache archive independently. A lockfile hash produced by the same
restore is not an independent candidate binding. Moving the runner into a
traversal project or the PE/PDB inspector would retain its process, graph and
hashing work while introducing another owner, so the existing runner remains.
Publication uses two manifest-driven Bash loops, and CI invokes the existing
read-only result validator instead of embedding another implementation in YAML.

Checked-in consumer project XML uses `Consumer.csproj.template`: these inputs
cannot restore without an exact candidate version and source-owned framework.
The existing runner copies them to ordinary `Consumer.csproj` files in its
isolated output before restoring. Automatic NuGet discovery therefore scans
runnable repository projects, while package consumers retain their required
version, source mapping, fresh cache and archive-byte checks.

### Re-evaluation Triggers

- A new supported native provider requires behavior outside the consumed internal boundary; qualify its actual consumers before adding another extension point.
- A native provider changes public query APIs, effective version ranges, model-finalizer ordering, or SQL semantics; rerun the corresponding adapter and package tests before updating its dependency range.
- A common algorithm gains a concrete provider-name branch or native dependency; restore the provider ownership boundary and retain a regression for the leaked responsibility.
- Adapter selection adds measured per-query/per-node allocations or worsens a qualified deep/wide work budget; profile and correct that path without relaxing the semantic contract.
- The release package set changes; update independent consumers, archive/SBOM inventories, trusted publishing permissions, and dependency-ordered publication together.

### Decision History

- 2026-10-26: Decision recorded with status proposed.
- 2026-10-26: Status changed from proposed to accepted.
- 2026-10-26: The owner approved the provider-package break, retained the common EF package name, and selected registration inside native provider callbacks.
- 2026-10-26: D-002 was superseded for the package layout; D-019 was amended for direct typed provider binding. Historical measurements and the pure-core principle remain intact.
- 2026-10-26: The owner explicitly kept `10.1.0` for this provider/registration break and reserved `11.x` for .NET 11; the exception is limited to this decision and is not general approval for breaking minor releases.
- 2026-10-26: Status changed from accepted to implemented.
- 2026-10-26: Verified the selected-provider source ownership and six shipping project outputs against the actual warning-free Release shipping build. Runtime/native qualification remains separate from source architecture and this build result.
- 2026-10-26: Context-scoped binding now validates ownership once and returns the existing stateless adapter, preserving warm resolution without a per-scope wrapper or repeated native metadata inspection.
- 2026-10-26: Native mapping length-policy recognition moved to adapters; the existing late finalization capture retains only primitive Scope and TreeId policy facts for runtime and compiled-model reuse.
- 2026-10-26: Review clarified the private ABI contract: adapter package edges are exact, ordinary source restore edges remain SDK-defined, and NuGet direct-dependency overrides are not an unconditional barrier.
- 2026-10-26: Consumer sources/projects became checked-in C# assets; SDK validation against the published baseline records only the approved registration removal. Native parameter plumbing is shared without moving reservation SQL into the kernel.
- 2026-10-26: SQL-free adapter defaults were consolidated; the shipping PKCS minimum was separated from the qualified servicing pin. Pack metadata uses the supported in-target modification form, and delivery reuses validated manifest ordering and result checks without introducing another framework.
- 2026-10-26: The qualification-only PKCS pin advanced to the .NET 10 package 10.0.14 while the shipping first-patched 9.x minimum remains 9.0.21 without an upper bound.
- 2026-10-28: Candidate project inputs became explicit templates after automatic NuGet submission attempted to restore them without a candidate version. The existing runner materializes identical project XML in isolated output; no release guard or dependency check was relaxed.

### Implementation References

- [Common EF kernel](../../src/Doka.EntityFrameworkCore.NestedSet/Doka.EntityFrameworkCore.NestedSet.csproj)
- [Selected-provider options registration](../../src/Doka.EntityFrameworkCore.NestedSet/Infrastructure/NestedSetProviderRegistration.cs)
- [Validated context-scoped adapter contract](../../src/Doka.EntityFrameworkCore.NestedSet/Providers/INestedSetProviderBinding.cs)
- [Native model policy boundary](../../src/Doka.EntityFrameworkCore.NestedSet/Providers/NestedSetProviderCapabilities.Model.cs)
- [Late hierarchy metadata capture](../../src/Doka.EntityFrameworkCore.NestedSet/Mapping/NestedSetCollations.cs)
- [Captured length-policy validation and common scalar counting](../../src/Doka.EntityFrameworkCore.NestedSet/Mapping/NestedSetIdentityLength.cs)
- [Selected-provider identity](../../src/Doka.EntityFrameworkCore.NestedSet/Infrastructure/NestedSetOptionsExtension.cs)
- [MySQL/MariaDB adapter](../../src/Doka.EntityFrameworkCore.NestedSet.MySql/Doka.EntityFrameworkCore.NestedSet.MySql.csproj)
- [PostgreSQL adapter](../../src/Doka.EntityFrameworkCore.NestedSet.Postgres/Doka.EntityFrameworkCore.NestedSet.Postgres.csproj)
- [SQLite adapter](../../src/Doka.EntityFrameworkCore.NestedSet.Sqlite/Doka.EntityFrameworkCore.NestedSet.Sqlite.csproj)
- [SQL Server adapter](../../src/Doka.EntityFrameworkCore.NestedSet.SqlServer/Doka.EntityFrameworkCore.NestedSet.SqlServer.csproj)
- [Typed query primitives](../../src/Doka.EntityFrameworkCore.NestedSet/Providers/NestedSetProviderCapabilities.Queries.cs)
- [Direct Doka query integration](../../src/Doka.EntityFrameworkCore.NestedSet.MySql/Queries/MySqlNestedSetProvider.Queries.cs)
- [Native callback and configuration rejection controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Configuration/OptionsExtensionTests.Providers.cs)
- [Exact load-context identity and registration lifetime controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Configuration/OptionsExtensionTests.Identity.cs)
- [Scope validation, singleton reuse, and conflicting binding controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Configuration/OptionsExtensionTests.Bindings.cs)
- [Actual selected-adapter warm allocation controls](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/SelectedAdapterAllocationTests.cs)
- [Typed source composition](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/DokaSourceBindingTests.cs)
- [Typed ordered joins and failure behavior](../../tests/Doka.EntityFrameworkCore.NestedSet.Unit.Tests/Unit/Providers/DokaStraightJoinTests.cs)
- [Package consumers](../../eng/verify-package-consumer.sh)
- [Checked-in consumer sources and project templates](../../eng/consumers)
- [Exact private kernel package edge](../../src/Directory.Build.targets)
- [SDK baseline configuration](../../src/Directory.Build.props)
- [Upgrade guide](../../docs/upgrading-provider-packages.md)
- [Support and executed qualification](../../docs/support-and-qualification.md)

### Sources

- [GitHub automatic dependency submission](https://docs.github.com/en/code-security/reference/supply-chain-security/automatic-dependency-submission) (primary source; retrieved 2026-10-28)
- [EF Core database providers](https://learn.microsoft.com/en-us/ef/core/providers/) (primary source; retrieved 2026-10-26)
- [EF Core plug-in APIs](https://learn.microsoft.com/en-us/ef/core/miscellaneous/plugins) (primary source; retrieved 2026-10-26)
- [EF Core 10.0.13 relational options-builder infrastructure](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Infrastructure/IRelationalDbContextOptionsBuilderInfrastructure.cs) (primary source; retrieved 2026-10-26)
- [Doka 10.6.2 published package metadata](https://api.nuget.org/v3-flatcontainer/doka.entityframeworkcore.mysql/10.6.2/doka.entityframeworkcore.mysql.nuspec) (primary source; retrieved 2026-10-26)
- [NuGet dependency resolution](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution) (primary source; retrieved 2026-10-26)
- [NuGet NU1608 dependency-constraint warning](https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1608) (primary source; retrieved 2026-10-26)
- [NuGet package-version guidance](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning) (primary source; retrieved 2026-10-26)
- [SDK package validation against a published baseline](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/baseline-version-validator) (primary source; retrieved 2026-10-26)
- [MSBuild Item element and in-target metadata modification](https://learn.microsoft.com/en-us/visualstudio/msbuild/item-element-msbuild) (primary source; retrieved 2026-10-26)
- [SDK API compatibility properties](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#apicompatpermitunnecessarysuppressions) (primary source; retrieved 2026-10-26)
- [PKCS advisory CVE-2026-70162](https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7) (primary source; retrieved 2026-10-26)
- [PKCS advisory CVE-2026-84008](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx) (primary source; retrieved 2026-10-26)
- [PKCS 10.0.14 package and target-framework dependencies](https://api.nuget.org/v3-flatcontainer/system.security.cryptography.pkcs/10.0.14/system.security.cryptography.pkcs.nuspec) (primary source; retrieved 2026-10-26)
