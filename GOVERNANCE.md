# Project governance

`Doka.EntityFrameworkCore.NestedSet` is maintained by Doka Labs. Dominic
Kalkbrenner (`@kdominic89`) is the lead maintainer, release maintainer, and
security responder named by the current package metadata.

## Roles

| Role | Responsibility | Current holder |
| --- | --- | --- |
| Lead maintainer | Product direction, public API, support matrix, dependencies, architecture, final technical decision | Dominic Kalkbrenner |
| Release maintainer | Candidate review, signed tag, protected publication approval, recovery | Dominic Kalkbrenner |
| Security responder | Private intake, triage, remediation, coordinated disclosure | Dominic Kalkbrenner |
| Reviewer | Independent assessment of correctness, compatibility, security, maintainability, and evidence | Assigned per pull request |
| Contributor | Focused implementation, tests, documentation, and review response | Any participant following `CONTRIBUTING.md` |

One person may hold several roles. That concentration does not satisfy an
independent-review, continuity, or bus-factor criterion.

## Decision model

Issues and pull requests are the normal public record. The lead maintainer
accepts or rejects routine changes after applicable checks and review.

A MADR decision is required when a change materially affects:

- public API or package boundaries;
- supported framework, provider, or database policy;
- scope, locking, transactions, ordering, repair, or data-integrity semantics;
- security, privacy, diagnostics, or application ownership boundaries;
- dependency, build, test, or release authority; or
- an architectural dependency direction.

Decision records use the [Doka MADR profile](docs/decisions/MADR-PROFILE.md).
An accepted record names actual decision makers and acceptance history.
Retrospective documentation must not invent prior approval. When evidence
changes an accepted choice, amend or supersede it explicitly.

## Change acceptance

A change is acceptable when:

- its consumer problem and scope are clear;
- the implementation is complete without unrelated surface;
- all applicable deterministic checks pass;
- positive, negative, rollback, cancellation, concurrency, and provider cases
  are covered proportionately;
- public API, migration, security, documentation, and release impacts are
  classified; and
- review findings are resolved or recorded transparently.

Automated checks support human review and do not replace it. Approval applies
to the reviewed commit only. A material change after approval needs review of
the changed result.

## Release authority

Only the release maintainer may choose a public version, create the authorized
signed tag, approve the protected `nuget` job, or decide recovery from partial
publication. Passing CI does not authorize release. The full responsibility
split is in [Release governance](docs/release-governance.md).

## Conflicts of interest

An author does not count as an independent reviewer of their own change. A
person directly involved in a conduct or security report does not decide it
when an unconflicted responder is available. If no independent internal path
exists, that limitation is disclosed rather than presenting the outcome as
independent.

## Continuity

The current role registry has one release-capable maintainer. The project does
not claim continuity after losing that person or a bus factor of two.

Continuity becomes claimable only after a second maintainer or tested succession
arrangement has independently usable legal authority and access for GitHub,
NuGet, vulnerability reports, signing, environment approval, and recovery.
Public evidence may record the arrangement and last test date but never
credentials.

## Review cadence

Review this document at least annually and whenever a role, organization
setting, signing identity, trusted publisher, support line, or decision process
changes. Reconcile it with [ROADMAP.md](ROADMAP.md),
[SECURITY.md](SECURITY.md), the [security design](docs/security/security-design.md),
and [repository settings](docs/runbooks/repository-settings.md).
