# Security Policy

For [supported versions](#supported-versions),
[private reporting](#reporting-a-vulnerability), and
[coordinated disclosure](#response-and-coordinated-disclosure), see below.
Do not report an exploitable vulnerability in a public issue, pull request, or
discussion.

## System and Scope

This policy covers the NestedSet repository, the `Doka.NestedSet` and
`Doka.EntityFrameworkCore.NestedSet` packages, and the repository's build and
release tooling.

`Doka.NestedSet` provides database-independent nested-set primitives.
`Doka.EntityFrameworkCore.NestedSet` is an in-process EF Core library that
maintains scoped forests through the consuming application's `DbContext`,
relational provider, connection, and transaction. The qualified provider matrix
contains Doka MySQL/MariaDB, Npgsql PostgreSQL, Microsoft SQLite, and Microsoft
SQL Server. Ordinary EF migrations are the required baseline. SafeMigrations
qualification is optional and does not become a runtime prerequisite.

NestedSet is not an authentication service, authorization service, database
server, secret store, or distributed transaction coordinator. The consuming
application selects and authorizes the scope, configures the EF model and
provider, owns database credentials and transport security, and controls every
non-NestedSet writer.

The maintained [security design](docs/security/security-design.md) records the
assets, trust boundaries, threats, controls, assumptions, and review triggers.
The [security assurance case](docs/security/assurance-case.md) maps those
requirements to implementation and test evidence. Passing tests are evidence
for the exact tested revision and environment; they are not a guarantee that
all vulnerabilities are absent.

## Threat Model and Trust Boundaries

Application-authored EF models, migrations, interceptors, triggers, raw SQL,
and direct database writers are privileged inputs. They can bypass NestedSet's
scope predicate, lock protocol, and structural invariants. Database rows and
metadata can be malformed or externally changed even when application code is
trusted, so validation and repair must not infer safety from bounds alone.

A configured scope key partitions forests and permits coordinate reuse between
scopes. It is a data boundary, not proof that the caller may access that
tenant, organization, tree collection, or authorization domain. `TreeId`
identifies one tree inside a scope and is stored on every node. A complete-tree
operation from an arbitrary node must resolve its scope and `TreeId` without
inferring either identity from overlapping bounds.

EF Core, the selected provider, and the database engine own transport, command
execution, transaction, isolation, collation, and locking semantics. NestedSet
must validate the provider and mapping assumptions it depends on and fail
before structural writes when those assumptions are unsupported.

Release contributors and pull-request content are not publication operators.
GitHub, NuGet.org, certificate authorities, and their identities form separate
trust boundaries. Telemetry collectors and application audit stores also have
different access and retention boundaries from the library.

## Security Invariants

- Every documented query and mutation must remain inside the explicitly
  selected scope. Keys or overlapping bounds from another scope must not
  resolve an anchor, parent, ancestor, descendant, subtree, or tree.
- Scope, parent, left, right, depth, and sibling position must remain mutually
  consistent after insert, move, delete, ordered save, bulk import, validation,
  and rebuild operations.
- One transaction or caller-owned savepoint must contain every structural
  range, adjacency, payload, lock, and refresh step owned by an operation.
  Structural writers for the same lock partition must serialize through the
  documented protocol.
- Runtime scope, key, order, and payload values must remain parameters.
  Physical identifiers must come from finalized EF relational metadata and
  use the selected provider's identifier handling.
- Unsupported providers, mappings, transaction states, retry boundaries, and
  lock configurations must fail before partial structural work.
- Cancellation, rollback, savepoint cleanup, and commit-unknown handling must
  not silently duplicate an operation, claim a rollback that is not known, or
  abandon the caller's transaction contract.
- Validation and rebuild must treat explicit adjacency as the repair source.
  Repeated coordinates across scopes or apparently plausible bounds must not
  authorize cross-scope repair.
- Library-owned metrics and activities must use bounded classifications and
  must exclude scope and node identifiers, entity and table names, SQL,
  credentials, connection details, exception payloads, and domain data.
- A public release is acceptable only when source identity, qualified package
  bytes, SBOMs, attestations, NuGet readback, and immutable release assets
  agree. A checksum alone is not proof of origin.

## Reportable Findings and Severity Context

Report a reachable violation of the invariants above, including:

- cross-scope reads, writes, ancestor traversal, subtree selection, or repair;
- structural corruption through a documented operation, concurrent writer, or
  supported transaction path;
- SQL injection, identifier confusion, or unsafe use of relational metadata;
- lock, retry, cancellation, savepoint, rollback, or commit-state behavior that
  can duplicate or partially apply a mutation;
- unsafe bulk import, ordering, validation, or rebuild behavior;
- disclosure of application identifiers, payload, SQL, credentials, or
  exception content through library-owned diagnostics;
- attacker-controlled resource consumption with a realistic availability
  impact beyond the documented workload boundary; or
- substitution or misbinding of source, packages, SBOMs, attestations, signing,
  publication, or release evidence.

Include the required access, configuration, data shape, provider, database
engine, and realistic confidentiality, integrity, or availability impact.
Severity depends on the reachable path and impact, not only on a component
name, a theoretical concern, or whether an existing test passes.

## Exclusions and Ownership

No vulnerability class is automatically suppressed by this policy. Direct
defects in EF Core, a provider, a database engine, GitHub, or NuGet.org have
different owners, but they can still expose a NestedSet integration defect.
The actual boundary must be triaged before a report is redirected.

Application-owned authentication, scope authorization, role and privilege
semantics, network security, database account policy, backup protection, and
raw SQL are outside NestedSet's implementation boundary. A failure in one of
those controls is still relevant when NestedSet documents an unsafe default,
crosses the boundary unexpectedly, or prevents the application from enforcing
the control.

Coordinate private upstream disclosure with the reporter. Do not forward
sensitive report content or change another repository without permission.
Reporter credit travels with an upstream handoff.

## Limitations and Compensating Controls

- Every writer that changes scope, parent, left, right, depth, or position must
  use the same lock and transaction protocol. Triggers, scripts, direct SQL,
  and other ORMs can corrupt the hierarchy when they bypass it.
- Server databases can use stable application-owned scope anchors for
  independent lock partitions. SQLite serializes database writers; a scope
  anchor does not create independent SQLite write lanes.
- Caller-owned transactions must follow the documented provider execution
  strategy and savepoint contract. SQL Server callers that require savepoints
  must keep Multiple Active Result Sets disabled because EF Core does not
  create savepoints when MARS is enabled.
- A connection failure during commit can leave the outcome unknown. Discard
  the context and reconcile from a new unit of work; do not blindly retry the
  logical mutation.
- Large scopes, deep trees, rebuilds, validation, and bulk imports consume
  resources in proportion to their documented data boundary. Applications
  must authorize maintenance operations, limit request size, configure
  timeouts, capacity-test their workload, and keep transactions short.
- NestedSet telemetry is operational instrumentation, not an application audit
  trail. Applications must record authorized business actions under their own
  data classification, access, and retention policy.
- Hosted repository controls, publishing identities, and public artifact
  verification do not exist merely because workflow files describe them. They
  require operator configuration and readback.

Use least-privilege database identities, protected credentials, provider
transport security, reviewed migrations, coordinated maintenance windows, and
current backups with a tested restore procedure. Validate affected scopes after
migrations, repairs, or out-of-band maintenance.

The OpenSSF Best Practices badge is a project self-assessment, not an
independent audit or blanket security certification. Review this policy when a
package, provider, SQL path, scope model, transaction protocol, diagnostic
signal, resource boundary, or release trust boundary changes.

## Supported Versions

No NestedSet package has been published. The repository is preparing the
`10.0.0-dev` development line, which is not a supported public release and has
no backport promise.

| Release state | Security support |
| --- | --- |
| Unreleased `10.0.0-dev` source | Reports are accepted and triaged against the exact source revision and package build |
| Stable releases | None published |

The first verified publication must update this table as part of release
readback. A tag, changelog entry, workflow run, or locally built package is not
proof that a supported package was published.

## Reporting a Vulnerability

**Do not report vulnerabilities through public issues, pull requests,
discussions, or attachments.**

Use
[GitHub private vulnerability reporting](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/security/advisories/new)
when the public repository exists and that feature is enabled. Until that
channel is available, or if the reporter cannot use it, email
`doka-labs@tuta.com` with the subject `NestedSet security report`. Absence of
the GitHub form is not a request to disclose publicly.

If sensitive attachments require additional encryption, first agree on a
secure transfer channel. No public long-lived PGP key is advertised for this
project.

Include as much of the following as possible:

- the affected package version or exact source revision and package hash;
- the .NET, EF Core, provider, and database engine versions;
- the scope mapping, transaction mode, ordering mode, and relevant operation;
- required privileges, attacker-controlled input, and potential impact;
- a minimal synthetic reproducer with expected and observed behavior;
- known mitigations; and
- any intended disclosure schedule.

Do not send production dumps, live credentials, access tokens, private signing
keys, customer data, real tenant or node identifiers, internal host names,
unredacted connection strings, or unrestricted database backups. Ask before
sending sensitive catalog, trace, or validation details.

## Response and Coordinated Disclosure

The maintainer handles intake, reproduction, severity and ownership
assessment, remediation, regression evidence, release coordination, and
advisory preparation. The targets below are response objectives, not a paid
support service level or evidence that previous reports met them:

| Stage | Target |
| --- | --- |
| Acknowledgment | Within 5 business days |
| Initial triage | Within 10 business days; explain any information still needed |
| Fix and advisory | Within 90 days of confirmation, coordinated with the reporter; prioritize critical or actively exploited defects immediately |

The private 90-day coordination target does not extend the OpenSSF criterion
for a publicly known medium-or-higher vulnerability. Once a qualifying issue is
public, the project targets a release within 60 days or documents why the
criterion is not met.

Keep the reporter informed when a target cannot be met. If no acknowledgment
arrives, follow up through the alternate private channel. Agree on disclosure
timing and credit; credit reporters unless they request anonymity. Do not
promise an embargo on behalf of a reporter without their agreement.

For an upstream-owned finding, agree on private routing with the reporter
before sharing details. A dependency boundary does not automatically exclude a
NestedSet defect.

## Fix and Release Requirements

A confirmed vulnerability fix must:

1. identify the root cause, affected boundary, and affected versions or
   revisions;
2. add positive and negative regression evidence for the reachable path;
3. update the security design, assurance case, operator guidance, and decision
   record when an assumption or trust boundary changed;
4. complete the normal provider, migration, package-consumer, and release
   qualification applicable to the change;
5. preserve private reproduction details until coordinated disclosure permits
   publication; and
6. publish affected and fixed versions, impact, mitigations, and CVE or GHSA
   identifiers when available.

Release notes must identify publicly known vulnerabilities fixed by the
release. Publication is incomplete until the public package bytes, repository
signature, attestations, SBOM, release assets, and source identity pass the
documented [release verification](docs/security/release-verification.md).

## Policy Basis and Evidence

This policy follows the Doka Labs security-policy structure established by:

- [Doka.EntityFrameworkCore.MySql SECURITY.md](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/SECURITY.md);
- [Doka.EntityFrameworkCore.SafeMigrations SECURITY.md](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/blob/main/SECURITY.md).

NestedSet-specific differences are supported by repository evidence:

- [Security design](docs/security/security-design.md);
- [Secure development](docs/security/secure-development.md);
- [Security assurance case](docs/security/assurance-case.md);
- [Support and qualification](docs/support-and-qualification.md);
- [Transactions and locking](docs/transactions-and-locking.md);
- [Diagnostics and observability](docs/diagnostics.md); and
- [Release verification](docs/security/release-verification.md).

External policy and technical statements use these primary sources:

- [GitHub private vulnerability reporting](https://docs.github.com/en/code-security/how-tos/report-and-fix-vulnerabilities/configure-vulnerability-reporting/configure-for-a-repository);
- [GitHub coordinated disclosure](https://docs.github.com/en/code-security/concepts/vulnerability-reporting-and-management/coordinated-disclosure);
- [OpenSSF Best Practices criteria](https://github.com/ossf/best-practices-badge/blob/main/docs/criteria.md);
- [Microsoft EF Core transactions and savepoints](https://learn.microsoft.com/en-us/ef/core/saving/transactions);
- [OWASP Logging Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html);
- [GitHub artifact attestations](https://docs.github.com/en/actions/concepts/security/artifact-attestations);
- [NuGet signed packages](https://learn.microsoft.com/en-us/nuget/reference/signed-packages-reference);
- [SLSA provenance](https://slsa.dev/spec/v1.2/provenance); and
- [SPDX specifications](https://spdx.dev/use/specifications/).

The organizational, repository, and primary references were checked on
2026-09-20. They justify this policy's reporting, transaction, diagnostics, and
supply-chain boundaries; they do not claim certification or successful
publication.
