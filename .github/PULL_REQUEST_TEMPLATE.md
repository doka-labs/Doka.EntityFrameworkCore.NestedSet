## Summary

- Consumer problem and resulting behavior:
- Why this change is needed now:
- Risk and recovery:

## Contract impact

Use `unchanged` or `changed` for each row. Explain changed behavior and link
the relevant tests or documentation. For an unaffected contract, a short reason
is enough.

| Contract | Disposition | Impact and evidence |
| --- | --- | --- |
| Public APIs and the two package contracts |  |  |
| Tree identity, bounds, ordering, and data integrity |  |  |
| Transactions, retries, cancellation, and concurrency |  |  |
| Provider, migration, and supported-version behavior |  |  |

## Evidence impact

| Evidence path | Disposition | Impact and evidence |
| --- | --- | --- |
| Positive, rejection, rollback, and provider tests |  |  |
| Deterministic performance and allocation evidence |  |  |
| Package, consumer, SBOM, and release evidence |  |  |
| Public documentation and samples |  |  |

## Validation

Use `passed`, `not applicable`, or `pending` for each row. Give the exact command,
CI job, provider versions, and result when applicable. Explain `not applicable`;
resolve `pending` before requesting final review. A local workspace run does not
establish hosted release or publication evidence.

| Check | Status | Command, result, or reason |
| --- | --- | --- |
| Locked restore, Release build, and Roslyn style/import checks |  |  |
| Relevant unit and provider integration tests |  |  |
| Migration, package-consumer, or SBOM checks when affected |  |  |
| Other targeted checks |  |  |

## Review checklist

- [ ] The change follows `CONTRIBUTING.md`, `.editorconfig`, and the Code of Conduct.
- [ ] Public XML documentation, API baselines, guides, samples, and changelog reflect changed contracts.
- [ ] New dependencies and public options have a justified consumer and the required owner approval.
- [ ] Logs, SQL, plans, fixtures, and attachments contain no secrets or confidential production data.
- [ ] Suspected vulnerabilities are reported privately through `SECURITY.md`.
