# Documentation

This index is the canonical entry point for NestedSet documentation. Choose a
guide by the task you need to complete. Feature guides define supported
behavior, architecture records explain why material decisions exist, and
runbooks own operational procedures.

## Use NestedSet

- [Getting Started](getting-started.md) builds a complete Doka MySQL/MariaDB
  application and its first hierarchy.
- [Hierarchy Model](hierarchy-model.md) defines scopes, forests, roots, parent
  links, bounds, depth, position, and tree identity.
- [Configuration](configuration.md) covers conventional and explicit property
  mapping, context integration, indexes, and dependency injection.
- [Ordering](ordering.md) defines automatic sibling sorting, rename handling,
  manual placement, and database comparison behavior.
- [Operations](operations.md) covers composable queries and every insert, move,
  delete, validate, and rebuild operation.
- [Bulk Import](bulk-import.md) covers atomic forest and subtree insertion.
- [API Reference](api-reference.md) maps the public types and methods to their
  inputs, outputs, ordering, transaction behavior, and failure contracts.

## Integrate and operate NestedSet

- [Supported Databases and Qualification](support-and-qualification.md)
  defines package ranges, engine targets, test ownership, and evidence limits.
- [Migrations and Indexes](migrations.md) covers ordinary EF migrations,
  provider SQL, optional SafeMigrations adapters, and upgrade checks.
- [Transactions and Locking](transactions-and-locking.md) defines transaction
  ownership, savepoints, typed per-tree locks, retries, cancellation, and recovery.
- [Diagnostics and Observability](diagnostics.md) covers typed failures,
  activities, metrics, logging boundaries, and incident evidence.
- [Performance](performance.md) explains read/write cost, memory ownership,
  mutation budgets, and how to measure an application workload.
- [Deployment and Recovery](runbooks/deployment-and-recovery.md) routes schema,
  lock, hierarchy, and partial-operation failures.

## Understand and maintain the repository

- [Developer Databases](../docker/README.md) explains optional Compose profiles,
  Rider connections, shared image pins, and developer volume ownership.
- [Implementation Design](implementation-design.md) describes package
  boundaries, mapping metadata, query and mutation execution, and provider
  integration.
- [Regression Coverage](regression-coverage.md) maps typed runtime risks to
  positive, negative, and adversarial tests and explains evidence limits.
- [Security Design](security/security-design.md) defines assets, trust
  boundaries, abuse cases, controls, and residual application responsibilities.
- [Security Assurance Case](security/assurance-case.md) maps security claims to
  controls, evidence, and remaining application responsibilities.
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
