---
id: D-017
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Mechanical profile validation and metadata-derived indexes"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-017 -- Validate decision records independently of release qualification

## Context and Problem Statement

The [foundational MADR decision](D-001-madr-profile-and-decision-governance.md)
defines the document contract independently of tooling. Repeated mechanical
checks of metadata, links, relationships, source syntax, and index drift should
use one repository implementation. Document review must remain independent of
package qualification so a prose or metadata error cannot block CI or RC.

This decision covers that local validation. It does not replace human review,
introduce a shipping dependency, or imply that pending engineering and
workflow files have been committed or qualified as a hosted release.

## Decision Drivers

- Enforce the existing profile consistently without substituting syntax checks for semantic review.
- Distinguish retrospective documentation from real historical acceptance.
- Keep metadata authoritative and generated navigation deterministic.
- Use existing standard-library engineering tools for a separate maintainer check.

## Considered Options

- Standard-library validator and metadata-derived indexes
- Human profile review with manually maintained indexes
- External ADR platform or new dedicated compiled tool project

## Decision Outcome

Chosen option: "Standard-library validator and metadata-derived indexes", because the document
standard should be complete while its mechanical enforcement stays local and dependency-free. Human review remains
responsible for correctness and approval.

### Consequences

- Good, because the README, JSON inventory, and any real relationship graph are derived from one metadata corpus and checked for drift.
- Bad, because a valid document can still contain an unsupported rationale, so a passing check does not authorize the decision.
- Bad, because a missed local document check can leave invalid or stale ADR navigation until a maintainer runs it.

### Confirmation

- Run `python3 -m unittest discover -s eng/quality/tests -p test_adrs.py` and expect malformed metadata, empty confirmation, source placement, invalid history, broken relationships, and index-drift fixtures to pass by rejecting invalid records.
- Run `eng/validate-adrs.sh --write-index` twice and expect identical README and JSON bytes. Run `eng/validate-adrs.sh` afterward and expect a successful read-only validation. Inspect CI and RC qualification wiring and expect neither the validator nor its corpus test suite in a blocking stage.

## Pros and Cons of the Options

### Standard-library validator and metadata-derived indexes

- Good, because the complete document contract and drift checks preserve traceability while using the existing Python tooling footprint.
- Bad, because authors must maintain explicit history and relationships, and semantic review still cannot be replaced by syntax checks.

### Human profile review with manually maintained indexes

- Good, because maintainers can evolve prose freely with little tooling to maintain.
- Bad, because missing confirmation, asymmetric alternatives, broken relationships, and stale navigation can pass unnoticed.

### External ADR platform or new dedicated compiled tool project

- Good, because a specialized tool may offer richer authoring and organization-wide integration.
- Bad, because the current repository would gain another installation or build dependency without a demonstrated need beyond local validation.

## More Information

Doka profile requirements extend the upstream template; they are not represented as mandatory upstream MADR
features. Records preserve their actual recording dates and initial proposal history. Current authority and
implementation confirmation are recorded through explicit history transitions. Accountability and an informed
audience do not imply consultation or notification delivery. Actual semantic changes require a reviewed history entry and reciprocal amendment or
supersession when appropriate.

### Re-evaluation Triggers

- Repeated review failures show a mechanically checkable gap in this profile.
- A real cross-repository consumer requires a compatible machine-readable schema change.
- An accepted decision is contradicted by implementation or operational evidence.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: The foundational document contract and its mechanical enforcement were recorded together, without reconstructing historical approval.

- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience.
- 2026-09-28: Status changed from accepted to implemented.
- 2026-09-28: Confirmed the full profile, template, standard-library validator, generated indexes, and positive and negative profile fixtures against the linked repository evidence. Informed metadata identifies the designated audience, not proof of notification delivery.

- 2026-09-29: Separated mechanical enforcement from the foundational MADR decision during the maintainer-authorized correction of the unpublished history. The original recording and acceptance dates are preserved; engineering implementation and delivery wiring remain outside the admitted commits.
- 2026-09-30: The maintainer removed ADR profile validation from blocking CI and RC qualification. The complete profile, validator, indexes, and fixtures remain available as a separate documentation check.

### Implementation References

- [Foundational decision](D-001-madr-profile-and-decision-governance.md)
- [Full profile](MADR-PROFILE.md)
- [Adapted template](adr-template.md)
- [Standard-library validator](../../eng/quality/adr.py)
- [Stable shell entry point](../../eng/validate-adrs.sh)
- [Regression fixtures](../../eng/quality/tests/test_adrs.py)
- [Governance](../../GOVERNANCE.md)

### Sources

- [MADR 4.0.0 full template](https://github.com/adr/madr/blob/4.0.0/template/adr-template.md) (primary source; retrieved 2026-09-19)
- [MADR 4.0.0 license](https://github.com/adr/madr/blob/4.0.0/LICENSE) (primary source; retrieved 2026-09-19)
