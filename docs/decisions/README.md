# Architecture decisions

These records use the [Doka MADR profile](MADR-PROFILE.md) and its complete
[template](adr-template.md). Each record's front matter is authoritative for
its current status, update date, and relationships. This manually maintained
index describes decisions without duplicating that mutable metadata.

| Record | Decision |
| --- | --- |
| [D-001 -- Record material decisions with the full Doka MADR profile](D-001-madr-profile-and-decision-governance.md) | Record material decisions with the full Doka MADR profile. |
| [D-002 -- Keep a small core separate from EF persistence](D-002-package-boundaries.md) | Keep a small core separate from EF persistence. |
| [D-003 -- Identify each tree with a stable TreeId and optional Scope](D-003-scoped-forests-and-adjacency.md) | Identify each tree with a stable TreeId and optional Scope. |
| [D-004 -- Serialize writers through typed per-tree registry locks](D-004-atomic-mutations-and-locks.md) | Serialize writers through typed per-tree registry locks. |
| [D-005 -- Integrate configured ordering with asynchronous saves](D-005-database-ordering-and-savechanges.md) | Integrate configured ordering with asynchronous saves. |
| [D-006 -- Validate iteratively and repair derived structure from adjacency](D-006-validation-and-adjacency-repair.md) | Validate iteratively and repair derived structure from adjacency. |
| [D-007 -- Import a complete branch through EF with bounded structural batches](D-007-atomic-bulk-import.md) | Import a complete branch through EF with bounded structural batches. |
| [D-008 -- Keep supporting indexes independent of optional migration adapters](D-008-provider-neutral-indexes-and-migrations.md) | Keep supporting indexes independent of optional migration adapters. |
| [D-009 -- Expose bounded diagnostics while applications own access and context lifetime](D-009-bounded-diagnostics-and-application-ownership.md) | Expose bounded diagnostics while applications own access and context lifetime. |
| [D-010 -- Delete every mapped table fragment in bounded batches](D-010-delete-mapped-table-fragments.md) | Delete every mapped table fragment in bounded batches. |
| [D-011 -- Group hierarchy behavior by feature](D-011-feature-oriented-source-layout.md) | Group hierarchy behavior by feature. |
| [D-012 -- Carry typed tree identities through feature execution](D-012-typed-tree-runtime.md) | Carry typed tree identities through feature execution. |
| [D-013 -- Give each database provider its own integration test project](D-013-provider-owned-integration-test-projects.md) | Give each database provider its own integration test project. |
| [D-014 -- Share database image pins with optional developer Compose](D-014-shared-database-images-and-developer-compose.md) | Share database image pins with optional developer Compose. |
| [D-015 -- Use BenchmarkDotNet for optional development measurements](D-015-benchmarkdotnet-development-measurements.md) | Use BenchmarkDotNet for optional development measurements. |
| [D-016 -- Qualify immutable candidates before operator-controlled publication](D-016-release-candidate-evidence-and-publication.md) | Qualify immutable candidates before operator-controlled publication. |
| [D-017 -- Validate decision records independently of release qualification](D-017-madr-profile-and-decision-validation.md) | Validate decision records independently of release qualification. |
| [D-018 -- Compose generic hierarchy queries through ordinary EF expressions](D-018-generic-hierarchy-query-composition.md) | Compose generic hierarchy queries through ordinary EF expressions. |
| [D-019 -- Opt into generated ancestor indexes and statement-local source binding](D-019-optional-ancestor-index-and-source-binding.md) | Opt into generated ancestor indexes and statement-local source binding. |
| [D-020 -- Own native integration in four provider packages](D-020-provider-owned-runtime-packages.md) | Own native integration in four provider packages. |
| [D-021 -- Separate release-line qualification from current publication authority](D-021-release-lines-and-monotonic-publication.md) | Separate release-line qualification from current publication authority. |
| [D-022 -- Qualify releases through the shared CI workflow](D-022-shared-ci-and-release-qualification.md) | Qualify releases through the shared CI workflow. |
| [D-023 -- Keep engineering contracts reproducible at their owning boundaries](D-023-reproducible-engineering-contracts.md) | Keep engineering contracts reproducible at their owning boundaries. |
