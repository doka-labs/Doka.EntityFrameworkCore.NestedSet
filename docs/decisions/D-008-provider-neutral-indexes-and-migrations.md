---
id: D-008
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Provider contracts, structural index ownership, and optional SafeMigrations integration"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-008 -- Keep supporting indexes independent of optional migration adapters

## Context and Problem Statement

Nested-set reads need scope-leading access paths, while structural updates temporarily change many coordinates.
Applications must obtain the required indexes from ordinary EF migrations even when SafeMigrations is absent. MySQL
and MariaDB use the Doka provider; PostgreSQL, SQLite, and SQL Server have their configured provider paths.

## Decision Drivers

- Configure indexes through the final EF model and physical property mappings.
- Avoid unique structural constraints that conflict with interval-shift update sequences.
- Qualify ordinary migrations independently from optional adapter behavior.
- Keep exact tested provider and adapter versions visible in the support matrix.

## Considered Options

- Provider-neutral model indexes with optional adapter qualification
- Require SafeMigrations for all structural schema creation
- Publish manual provider-specific index SQL only

## Decision Outcome

Chosen option: "Provider-neutral model indexes with optional adapter qualification", because indexes are part of
the entity model, while SafeMigrations is an optional application migration policy. A provider dependency does not
belong in the shipping NestedSet packages.

### Consequences

- Good, because ordinary Doka and PostgreSQL migrations remain the required path, and optional SQLite adapter qualification does not change package prerequisites.
- Bad, because a supporting index is not a universal optimal query plan; real payload filters and workload distributions may need application indexes.

### Confirmation

- Inspect the [index convention](../../src/Doka.EntityFrameworkCore.NestedSet/Configuration/NestedSetIndexConvention.cs) and [registry metadata](../../src/Doka.EntityFrameworkCore.NestedSet/Infrastructure/NestedSetTreeRegistryMetadata.cs); expect typed tree identity and configured structural indexes without a SafeMigrations runtime dependency.

Run live-provider cases on each provider project present in this revision; a
filtered run does not establish coverage for a provider introduced later.


- Migration SQL and optional adapter confirmation are added with their owning migration test projects; runtime results alone do not qualify those contracts.

## Pros and Cons of the Options

### Provider-neutral model indexes with optional adapter qualification

- Good, because ordinary EF scaffolding owns the model contract and applications can add SafeMigrations independently.
- Bad, because both ordinary and optional generated migrations need separate lifecycle and physical-catalog tests.

### Require SafeMigrations for all structural schema creation

- Good, because one migration path could centralize drift and replay policies.
- Bad, because consumers would acquire a mandatory integration dependency and lose the ordinary migration contract they requested.

### Publish manual provider-specific index SQL only

- Good, because database owners could tune access paths directly for known workloads.
- Bad, because index definitions could drift from renamed model properties, ownership metadata, and generated migration snapshots.

## More Information

The support matrix is the canonical version inventory; this record does not claim support for an unqualified future
provider release. The current optional adapter baseline is recorded in the support matrix. SQLite runtime safe migrations
and unsupported SQL-script generation are distinct contracts. SQL Server ordinary support does not imply an
optional SQL Server SafeMigrations adapter.

### Re-evaluation Triggers

- A new provider version changes index metadata, key mapping, SQL generation, or physical catalog behavior.
- Query-plan evidence shows a missing structural access path or a harmful redundant index.
- The application requests a migration policy unavailable through the current optional adapters.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: Existing implementation documented retrospectively; no historical approval or consultation is inferred.

- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed the deterministic model-index conventions and ordinary EF metadata path against the linked runtime and unit specifications. Physical migration lifecycle and optional adapter specifications require the following migration-test change.

### Implementation References

- [Index roles](../../src/Doka.EntityFrameworkCore.NestedSet/Configuration/NestedSetIndexes.cs)
- [Model index convention](../../src/Doka.EntityFrameworkCore.NestedSet/Configuration/NestedSetIndexConvention.cs)
- [Provider capabilities](../../src/Doka.EntityFrameworkCore.NestedSet/Providers)
- Ordinary migration tests (`tests/Doka.EntityFrameworkCore.NestedSet.Migrations.Tests`; introduced with its owning feature)
- Optional adapter tests (`tests/Doka.EntityFrameworkCore.NestedSet.SafeMigrations.Tests`; introduced with its owning feature)
- [Migration contract](../../docs/migrations.md)
- Pinned support matrix (`docs/support-and-qualification.md`; introduced with its owning feature)
- [Central package versions](../../Directory.Packages.props)

### Sources

- No external sources; repository evidence only.
