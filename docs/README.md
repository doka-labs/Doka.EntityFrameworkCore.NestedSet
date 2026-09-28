# Documentation

This index is the canonical entry point for NestedSet documentation. Choose a
guide by the task you need to complete. Feature guides define supported
behavior, architecture records explain why material decisions exist, and
runbooks own operational procedures.

## Understand and maintain the repository

- [Architecture Decisions](decisions/README.md) indexes the MADR 4.0 decision
  corpus and its status.
- [Contributing](../CONTRIBUTING.md), [Support](../SUPPORT.md),
  [Security Policy](../SECURITY.md), [Governance](../GOVERNANCE.md), and
  [Roadmap](../ROADMAP.md) define repository participation and ownership.

## Document ownership

| Document type | Owns | Does not own |
| --- | --- | --- |
| Root README | Installation, first use, and high-level contracts | Complete edge cases and operator procedures |
| Feature guide | Supported behavior, examples, and failures | Decision history |
| API reference | Public entry points and exact method contracts | Tutorials |
| Implementation design | Runtime structure and responsibility boundaries | Operator commands |
| Architecture decision | Context, alternatives, and consequences | Repeated feature documentation |
| Runbook | Diagnosis, recovery, and operator steps | Architecture rationale |
| Qualification document | Test matrix and evidence boundaries | Claims about an unexecuted run |

Each public behavior has one canonical owner. Other documents link to that
owner instead of maintaining a second version of the same contract. External
version or hosted-service claims require dated primary sources. Commands in a
document are procedures; they are not evidence that a command has run.
