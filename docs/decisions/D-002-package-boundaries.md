---
id: D-002
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Shipping package dependency direction and public responsibility"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-002 -- Keep a small core separate from EF persistence

## Context and Problem Statement

Applications need relational hierarchy operations without forcing domain types to inherit an EF-specific base
class. The existing solution has two shipping projects. The question is whether their separation expresses a useful
dependency boundary or adds an unnecessary adapter framework.

## Decision Drivers

- Keep interval calculations usable without a database or EF reference.
- Keep transactions, mapping, provider behavior, and set-based mutation in the EF package.
- Make ordinary installation resolve the core transitively without multiple manual package choices.

## Considered Options

- Small core and one EF integration package
- Single EF integration package
- General adapter framework with provider-independent mutation services

## Decision Outcome

Chosen option: "Small core and one EF integration package", because the core already contains useful pure semantics
and the EF package owns the persistence-dependent algorithms. The separation does not promise other ORM adapters.

### Consequences

- Good, because applications can reference pure hierarchy types or install the EF package and receive the core transitively.
- Bad, because both packages require compatible versions and independent package-consumer verification.

### Confirmation

- Run `dotnet test tests/Doka.NestedSet.Tests/Doka.NestedSet.Tests.csproj -c Release` and expect bounds and relationship tests to pass, including invalid default bounds.
- Inspect both shipping project and restore graphs; expect no concrete provider or SafeMigrations runtime dependency. Verify that the EF package references the core transitively while the core has no EF dependency.

## Pros and Cons of the Options

### Small core and one EF integration package

- Good, because pure bounds and optional interfaces stay usable independently while persistence shares one implementation.
- Bad, because two package identities, dependency versions, API baselines, and publication artifacts must remain consistent.

### Single EF integration package

- Good, because one artifact reduces release coordination and is sufficient for exclusively EF-based consumers.
- Bad, because domain-only interval use would acquire EF dependencies and the public boundary would be harder to separate later.

### General adapter framework with provider-independent mutation services

- Good, because a second ORM adapter could reuse established mutation contracts if a real consumer needed them.
- Bad, because the current mutations depend on relational transactions and set updates, so abstracting them now would add unverified contracts.

## More Information

This record describes the existing package boundary retrospectively. The optional interface does not provide
authorization, scope isolation, or transaction behavior. No generic persistence adapter API is introduced by this
decision.

### Re-evaluation Triggers

- A concrete non-EF consumer needs reusable behavior beyond bounds and predicates.
- A core public type requires EF metadata or the shipping dependency graph gains a concrete provider.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: Existing implementation documented retrospectively; no historical approval or consultation is inferred.

- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed the separate core and EF projects, one-way project dependency, and core regression specifications against the linked repository evidence.

### Implementation References

- [Pure core project](../../src/Doka.NestedSet/Doka.NestedSet.csproj)
- [EF integration project](../../src/Doka.EntityFrameworkCore.NestedSet/Doka.EntityFrameworkCore.NestedSet.csproj)
- [Core tests](../../tests/Doka.NestedSet.Tests)
- [Implementation design](../../docs/implementation-design.md)

### Sources

- No external sources; repository evidence only.
