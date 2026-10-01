# Project roadmap

This roadmap describes direction from September 2026 through September 2027.
It is not a delivery-date promise. Released packages, release notes, and the
[support matrix](docs/support-and-qualification.md) define supported behavior.

## Direction

### Qualify 10.0.0-rc.1 before the first stable 10.0.0

- Exercise the complete RC qualification and publication process first with
  `10.0.0-rc.1`; its first hosted run is still pending.
- Keep the reviewed two-package API declarations in the unshipped baselines
  through prereleases. Establish the stable compatibility baseline in a
  separate reviewed `10.0.0` preparation after successful RC evidence.
- Keep the EF-independent core limited to contracts useful without persistence.
- Complete real hosted CI, security settings, RC, signed tag, NuGet trusted
  publishing, provenance, and public readback evidence.
- Qualify ordinary migrations on every supported provider and optional
  SafeMigrations integration independently.
- Keep examples and operations documentation aligned with file systems, KPIs,
  organization/user groups, roles, and privileges.

### Preserve correctness at scale

- Treat scope isolation, atomic mutations, concurrent writers, rollback,
  commit ambiguity, ordering, validation, and rebuild as the primary contract.
- Optimize query shape, write amplification, allocations, and memory only from
  measured application or controlled local evidence.
- Keep timing benchmarks outside CI and release authority; runner CPU and load
  are not deterministic.
- Extend stable structural command/row and provider-plan tests when a regression
  demonstrates the need.

### Maintain supported platforms

- Keep .NET 10, EF Core 10, Doka MySQL/MariaDB, Npgsql PostgreSQL, Microsoft
  SQLite, and Microsoft SQL Server under dependency and vulnerability review.
- Requalify exact engine/provider updates through runtime, concurrency,
  migration, catalog, and package evidence.
- Keep SafeMigrations optional and update its adapter matrix only after this
  repository verifies the released packages.
- Treat a future .NET/EF major as an explicit compatibility decision, not a
  routine package update.

### Maintain supply-chain evidence

- Keep actions, SDK, images, and dependencies pinned and reviewable.
- Preserve exact packages, symbols, SBOMs, signed provenance, signed tags,
  short-lived publishing credentials, immutable releases, and public readback.
- Keep repository settings and OpenSSF claims honest about external and
  organizational evidence.
- Maintain [Passing evidence](docs/openssf-best-practices.md) for project 15143,
  including actual analysis results, report history, and maintainer attestations.

## Explicit non-goals

Through September 2027 the project does not intend to:

- support Pomelo;
- require SafeMigrations for ordinary runtime or migration support;
- add another ORM adapter without a concrete maintained consumer and full
  support decision;
- implement authentication, group membership, role assignment, privilege
  conflict rules, or application authorization;
- represent graphs with multiple parents or cycles;
- promise a universal maximum tree size, latency, or throughput;
- make GitHub benchmark timing a release gate;
- claim NativeAOT or trimming support without package/runtime evidence; or
- add configuration switches for hypothetical future use.

## Review triggers

The lead maintainer reviews this roadmap at least quarterly and when:

- the first public release completes;
- a supported platform publishes a new major or LTS line;
- a vulnerability or data-integrity issue changes priorities;
- production evidence requires a new scalability contract; or
- an accepted MADR changes package, provider, or release direction.

Completed behavior belongs in [CHANGELOG.md](CHANGELOG.md). A roadmap change
links the consumer evidence, issue, or decision that caused it.
