---
id: D-001
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Decision format, ownership, approval integrity, and traceability"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-001 -- Record material decisions with the full Doka MADR profile

## Context and Problem Statement

Implementation and operational guides describe current behavior but do not preserve
why a boundary was chosen, which alternatives were rejected, or which changed
assumptions would reopen it. NestedSet needs the decision discipline used by the
reference Doka repositories from repository initialization onward.

The document contract and its later automated enforcement are separate concerns.
This decision establishes the contract without requiring an engineering script,
CI workflow, additional service, or shipping dependency.

## Decision Drivers

- Make context, alternatives, consequences, confirmation, and sources reviewable.
- Identify the accountable decision maker and the designated informed audience.
- Distinguish actual acceptance from retrospective implementation documentation.
- Keep identifiers, relationships, and navigation consistent as features arrive.

## Considered Options

- Full MADR 4.0.0 structure with the Doka decision profile
- Lightweight Markdown notes with optional decision sections
- External ADR platform or separate decision service

## Decision Outcome

Chosen option: "Full MADR 4.0.0 structure with the Doka decision profile", because
the existing profile records the rationale and review evidence needed to maintain
the library while keeping decisions in the same repository as their consumers.

### Consequences

- Good, because reviewers can inspect ownership, trade-offs, evidence, and concrete re-evaluation triggers alongside the affected implementation.
- Bad, because a complete document still requires human review; metadata and a well-formed structure cannot prove that a claim or approval is real.

### Confirmation

- Inspect the [profile](MADR-PROFILE.md) and [template](adr-template.md); expect the full section contract, explicit ownership, source provenance, legal status history, and reciprocal relationship rules.
- Compare each admitted record's filename, front matter, heading, and both indexes; expect unique contiguous identifiers and navigation containing only records present in that revision.
- Reject missing alternatives, an empty confirmation, unsupported acceptance claims, unresolved local links, or a relationship without its reciprocal entry before admitting a record.

## Pros and Cons of the Options

### Full MADR 4.0.0 structure with the Doka decision profile

- Good, because a shared document contract makes rationale, negative consequences, evidence, and ownership comparable across the organization's repositories.
- Bad, because authors must maintain the complete record and its relationships even when the underlying code change is small.

### Lightweight Markdown notes with optional decision sections

- Good, because maintainers can record a small decision with less authoring overhead.
- Bad, because omitted alternatives, confirmation, ownership, or history leave later reviewers unable to assess why the decision still applies.

### External ADR platform or separate decision service

- Good, because a dedicated system can offer richer organization-wide authoring and navigation.
- Bad, because this repository has no demonstrated need for another installation, service, or independently maintained decision store.

## More Information

Doka's metadata, status, relationship, and source rules extend the upstream
template; they are not represented as mandatory upstream MADR features. The
profile and template belong to repository initialization. Each subsequent
decision belongs before or with the commit introducing its consumer, and each
revision's indexes describe that revision's records.

Reordering unpublished Git commits does not change actual recording, research,
acceptance, or implementation dates. The designated informed audience does not
prove consultation or notification delivery. Mechanical validation can enforce
this contract separately; it cannot authorize a decision.

### Re-evaluation Triggers

- Repeated reviews identify a missing mechanically checkable part of the contract.
- A real cross-repository consumer requires a compatible metadata schema change.
- Accepted rationale is contradicted by implementation or operational evidence.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: The foundational profile and its planned mechanical enforcement were originally documented together, without reconstructing historical approval.
- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the decision corpus and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed the full profile, template, and repository governance against the implementation references.
- 2026-09-29: At the maintainer's request, separated the foundational contract from mechanical enforcement and placed this record with repository initialization in the unpublished history. Actual recording and acceptance dates are preserved.

### Implementation References

- [Full profile](MADR-PROFILE.md)
- [Adapted template](adr-template.md)
- [Repository governance](../../GOVERNANCE.md)

### Sources

- [MADR 4.0.0 full template](https://github.com/adr/madr/blob/4.0.0/template/adr-template.md) (primary source; retrieved 2026-09-19)
- [MADR 4.0.0 license](https://github.com/adr/madr/blob/4.0.0/LICENSE) (primary source; retrieved 2026-09-19)
