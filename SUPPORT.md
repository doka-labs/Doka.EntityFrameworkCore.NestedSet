# Support

This policy routes NestedSet questions and reports to the appropriate channel.
Public support is best effort. It is not a commercial support agreement, a
response-time guarantee, or an emergency incident-response service.

## Choose the Correct Channel

| Request | Channel | Public? |
| --- | --- | --- |
| Suspected security vulnerability | Follow [SECURITY.md](SECURITY.md#reporting-a-vulnerability) | No |
| Harassment or other conduct concern | Follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md#reporting-an-incident) | No |
| Reproducible NestedSet defect | [GitHub Issues][issues] | Yes |
| Feature or compatibility proposal | [GitHub Issues][issues] | Yes |
| Usage, configuration, or capability question | [GitHub Issues][issues] | Yes |

Do not send ordinary usage questions to the private security or conduct
channels. Do not post vulnerabilities, credentials, confidential customer
information, or production data in public issues, pull requests, or
attachments.

The issues route is available only while GitHub Issues are enabled and only to
people with repository read access. If the repository is private, this policy
does not create a separate public or private general-support mailbox.

## Before Opening an Issue

Check the [README](README.md), [documentation index](docs/README.md),
support and qualification contract (`docs/support-and-qualification.md`; introduced with its owning feature),
deployment and recovery runbook (`docs/runbooks/deployment-and-recovery.md`; introduced with its owning feature),
and [existing issues][issues]. Reduce the behavior to the smallest synthetic
case that preserves the failure.

An actionable report normally includes:

- the exact NestedSet package version or source revision;
- .NET SDK and runtime, EF Core, provider, driver, and database engine versions;
- operating system and architecture, plus relevant proxy, pooler, cluster,
  managed-service, or SQLite file-system topology;
- the affected package and API operation;
- a minimal entity model and complete `HasNestedSet` configuration;
- synthetic scope, key, root, parent, left, right, depth, and position values
  needed to reproduce the starting forest;
- transaction ownership, isolation, execution strategy, tracking state,
  cancellation, and concurrent-writer conditions;
- expected and actual outcomes, stable NestedSet failure code, and exception
  type;
- whether ordinary EF migrations or an optional SafeMigrations adapter is
  involved;
- exact reproduction commands; and
- the last working version or revision when reporting a regression.

For `Doka.NestedSet` issues that do not involve a database, say so and omit
provider details. Do not invent environment information to complete the
report.

For query issues, include the composed LINQ shape and the terminal operation.
For ordering issues, include every configured criterion, direction, collation,
null value, tie breaker, and whether strict or manual-override mode is active.
For transaction or concurrency issues, include the scope-lock mapping and the
order in which application and hierarchy locks are acquired.

For performance issues, provide the scope and tree shape, operation, command
and update counts, affected-row counts, database execution plan, and lock-wait
evidence before elapsed time. Repository benchmark timings from different
machines are not comparable acceptance evidence.

Use fenced code blocks for code, SQL, selected logs, plans, and stack traces. A
small executable repository is preferred when a code fragment cannot preserve
the model, provider, or transaction behavior.

## Safe Diagnostic Sharing

Use disposable databases and synthetic data for reproducers. Replace sensitive
names and values consistently so scope equality, key relationships, ordering,
collation, and identifier behavior remain reproducible.

Remove passwords, tokens, connection strings, private signing material,
personal data, customer and tenant identifiers, user or group identities, role
grants, internal host names, confidential schema names, and production paths
from every public report and attachment. Generated SQL, EF logs, query plans,
validation results, and traces can contain mapped names or application values;
review them before sharing.

`ValidateDetailedAsync` returns node keys to its direct caller. Those results
are application data even though NestedSet's process-wide telemetry excludes
node and scope identifiers. The absence of credentials does not make a
diagnostic artifact safe to publish.

Do not upload production dumps, backups, or complete unredacted logs. If an
operation fails against production, preserve the original error, transaction
outcome, database state, and available evidence before recovery attempts. Use
the tested recovery procedure; do not delete migration history, rewrite
bounds, run a rebuild, weaken locking, or repeat an unknown commit merely to
create a reproducer.

Route any suspected vulnerability through the private security process.

## Supported Scope and Ownership

The authoritative package, runtime, provider, engine, migration, and tooling
matrix is maintained in
Support and qualification (`docs/support-and-qualification.md`; introduced with its owning feature). This policy
does not extend that matrix, infer compatibility from a shared SQL dialect, or
promise a release date. Use published packages when available, or identify the
exact revision and package hash for an unpublished build.

`Doka.NestedSet` owns `INestedSetNode<TNodeKey, TTreeId>`, its scoped counterpart,
`NestedSetBounds`, and the database-independent relationship predicates in
`NestedSetNodeExtensions`.

`Doka.EntityFrameworkCore.NestedSet` owns its documented:

- entity mapping and structural indexes;
- tree, subtree, ancestor, descendant, parent, and child queries within an
  optional Scope;
- insert, move, delete, ordering, bulk import, validation, and rebuild
  operations;
- transaction, savepoint, cancellation, retry, and lock behavior;
- coordinated asynchronous `SaveChanges` ordering behavior;
- typed failures and bounded diagnostics; and
- ordinary EF migration integration.

MySQL and MariaDB are qualified through the Doka provider. Pomelo is not a
supported provider. PostgreSQL uses Npgsql. SQLite and SQL Server use the
Microsoft providers. Each database family is qualified independently; a shared
dialect or successful package restore is not evidence of support.

SafeMigrations adapters for MySQL/MariaDB, PostgreSQL, and SQLite provide
optional additional qualification. SafeMigrations is not a runtime
prerequisite, and ordinary EF migrations must work without it. SQL Server has
no SafeMigrations adapter in this repository.

The consuming application owns authentication, authorization of the selected
scope and operation, role and privilege semantics, domain validation, payload
filters, database credentials, network and transport policy, backups, direct
SQL, non-NestedSet writers, and workload-specific capacity.

Reports are assessed at these boundaries before being redirected. If evidence
locates a defect in EF Core, Doka, Npgsql, a Microsoft provider, SafeMigrations,
a driver, or a database engine, maintainers should link a minimal upstream
reproducer and retain the NestedSet-specific impact or regression evidence. A
dependency boundary alone is not sufficient reason to redirect a report.

General application architecture, database administration, unsupported
providers or engine versions, authorization design, business-specific
hierarchy semantics, and automatic reconstruction of unknown legacy data are
not maintained NestedSet contracts. They may receive community guidance
without expanding the support matrix.

## Version and Compatibility Lifecycle

No NestedSet package has been published. The repository currently prepares the
`10.0.0-dev` line. Reports against this line must identify the exact revision
and package build; a changelog entry, tag, or local archive is not evidence of
publication.

Both packages target .NET 10. The EF package declares compatibility with EF
Core 10 beginning at the repository's minimum version and excludes EF Core 11.
The exact minimum, locked versions, providers, and engine images belong to the
support and qualification contract rather than this routing policy.

Consumers and reproducers should use supported .NET and EF Core releases and
remain current on their patch line. That upstream lifecycle does not by itself
establish NestedSet compatibility: EF providers generally do not work across
major EF Core versions, and every provider or engine line requires explicit
NestedSet qualification.

The `-dev` prerelease is not the first stable `10.0.0` publication. Public API
changes during prerelease development must be documented; compatibility with a
published stable package is promised only after that package is released and
qualified.

Unsupported versions or configurations may be investigated to understand a
regression or future compatibility need. Such investigation does not create a
support guarantee. NativeAOT, trimming, a new EF Core major, a new provider, or
a new database family becomes supported only after the repository's documented
qualification and decision process completes.

## Response and Lifecycle

Priority follows security impact, data integrity, supported-matrix regressions,
reproducibility, and affected users rather than submission order. Maintainers
may request a smaller reproducer or additional sanitized evidence.

Issues may be closed as duplicates, documented expected behavior, unsupported
configurations, or upstream defects, or when essential reproduction details
remain unavailable. The reason and relevant references should be recorded. New
evidence can justify reopening an issue.

An accepted feature request, assigned milestone, merged change, or closed issue
does not prove package availability. Publication claims require the release
readback documented by the repository.

Security reports follow the acknowledgment and coordinated-disclosure targets
in [SECURITY.md](SECURITY.md#response-and-coordinated-disclosure). Those targets
do not apply to ordinary support.

## Policy Basis and Evidence

This policy follows the Doka Labs support-policy structure established by:

- [Doka.EntityFrameworkCore.MySql SUPPORT.md](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/SUPPORT.md);
- [Doka.EntityFrameworkCore.SafeMigrations SUPPORT.md](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/blob/main/SUPPORT.md).

NestedSet-specific differences are supported by repository evidence:

- Support and qualification (`docs/support-and-qualification.md`; introduced with its owning feature);
- Transactions and locking (`docs/transactions-and-locking.md`; introduced with its owning feature);
- Diagnostics and observability (`docs/diagnostics.md`; introduced with its owning feature);
- Migrations and indexes (`docs/migrations.md`; introduced with its owning feature);
- Performance and capacity (`docs/performance.md`; introduced with its owning feature);
- Deployment and recovery (`docs/runbooks/deployment-and-recovery.md`; introduced with its owning feature); and
- [Roadmap](ROADMAP.md).

External lifecycle and reporting statements use these primary sources:

- [Microsoft .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy);
- [Microsoft EF Core releases and planning](https://learn.microsoft.com/en-us/ef/core/what-is-new/);
- [Microsoft EF Core database providers](https://learn.microsoft.com/en-us/ef/core/providers/);
- [Semantic Versioning 2.0.0](https://semver.org/);
- [GitHub Issues documentation](https://docs.github.com/en/issues/tracking-your-work-with-issues/using-issues/creating-an-issue); and
- [Microsoft .NET issue-reporting guidance](https://github.com/dotnet/extensions/blob/main/CONTRIBUTING.md#writing-a-good-bug-report).

The organizational, repository, and primary references were checked on
2026-09-21. They justify this policy's routing, reproduction, lifecycle, and
compatibility boundaries; they do not claim commercial support, successful
publication, or compatibility outside the qualified matrix.

[issues]: https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/issues
