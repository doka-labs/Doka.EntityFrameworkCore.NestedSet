# Secure development

Use [CONTRIBUTING.md](../../CONTRIBUTING.md) for setup, formatting, test commands,
and public API review. This guide applies the Doka Labs secure-development
approach to NestedSet's actual database and change-tracker boundaries. It does
not certify developer training or independent review.

## Review the boundary that changed

Review changes against [Security Policy](../../SECURITY.md), the
[security design](security-design.md), and the
[assurance case](assurance-case.md). In particular:

| Boundary | Review and regression evidence |
| --- | --- |
| SQL construction | Keep values parameterized and identifiers provider-quoted; reject unsupported metadata before SQL |
| Scope and tree selection | Preserve the complete configured identity, native key equality, and collation; require application authorization separately |
| Structural writes | Validate anchors under locks; retain atomic updates, deterministic multi-tree lock order, and transaction ownership |
| Save callbacks | Reject unplanned hierarchy writes while preserving ordinary audit/outbox writes and rollback-restorable tracker state |
| Bulk import and rebuild | Validate complete input, reject cycles/duplicates, and retain bounded batches and iterative traversal |
| Queries and diagnostics | Preserve query-filter contracts; avoid leaking keys, scopes, payloads, SQL parameters, or credentials through telemetry |
| Package and dependency updates | Review upstream advisories, actual API changes, locked graphs, native key behavior, and supported provider regressions |

The [regression matrix](../regression-coverage.md) links positive, negative, and
adversarial tests to these mechanisms. Keep one Arrange/Act/Assert sequence per
test. A security fix needs a reproducer that fails before the fix and a positive
control for legitimate use. Correct the owning implementation; do not bypass a
failed test or raise a budget to hide the defect.

Record the reviewed revision, affected provider families, commands, results,
and residual limitations in the pull request. Keep private vulnerability details
in the private reporting record until coordinated disclosure.

## Developer knowledge

OpenSSF's knowledge criteria require an actual primary developer's knowledge.
A document, AI-generated explanation, or green test run cannot establish it.
The maintainer must understand economy of mechanism, fail-safe defaults,
complete mediation, open design, separation of privilege, least privilege,
least common mechanism, and psychological acceptability, as well as limited
attack surface and allowlist input validation.

For this library that includes SQL injection, cross-scope data exposure,
authorization mistakes, concurrency corruption, callback changes after
validation, unbounded input/resource use, secret leakage, and dependency or
publication compromise. Reviewers should be able to explain at least one
countermeasure for each relevant class and where the implementation enforces it.
Knowledge confirmations belong to the maintainer's self-assessment, not an
invented repository certificate.

## Static analysis and dependency review

[Directory.Build.props](../../Directory.Build.props) enables nullable analysis,
warnings as errors, the SDK's recommended .NET analyzers, code-style analysis,
and NuGet audit of direct and transitive dependencies. The SDK analyzer rules
are separate from compiler warnings. Shipping projects also enable XML API
documentation, public API analysis, and package validation.

PR CI runs analysis in Release builds, style/import checks, package inspection,
and the test matrix. GitHub CodeQL default setup is configured on the hosted
repository; it is not represented by an additional workflow in this repository.
Dependency Review checks proposed dependency changes. Secret scanning and push
protection complement review; their enabled state is not proof of an empty
alert history. Triage alerts rather than treating a green build as a substitute.

Two reviewed Microsoft native-package licenses need targeted exceptions:
[SNI.runtime 6.0.2](https://www.nuget.org/packages/Microsoft.Data.SqlClient.SNI.runtime/6.0.2/License)
and [NativeInterop 0.20.6](https://www.nuget.org/packages/Microsoft.Identity.Client.NativeInterop/0.20.6/License).
These are license files, not MIT expressions. The exceptions apply to the test
dependency graph; neither shipping NestedSet package gains these dependencies.
The existing action continues checking other licenses and vulnerabilities. Since
its pinned v5.0.0 PURL exclusion compares identities without enforcing versions,
the following workflow step verifies the exact reviewed versions against the
action's complete comparison output. A package update requires a new license
review. Null license metadata and Scorecard results are separate inputs.

Before a major production release, retain the actual static-analysis results
for the proposed revision and fix confirmed exploitable medium-or-higher
findings promptly. Do not count Scorecard configuration/process warnings as
CodeQL source vulnerabilities, or dismiss source vulnerabilities because the
Scorecard workflow succeeded.

## Dynamic analysis evidence

Provider suites execute actual database behavior, including hostile identifiers,
native aliases, invalid imports, rollback, cancellation, concurrent writers,
and late callback changes. Their xUnit assertions remain active in Release test
execution. The library's defensive validation is not compiled away for Release.
This does not claim a separate fuzzing campaign or production `Debug.Assert`
instrumentation.

The existing unit projects also run FsCheck properties for bounds, complete
tree identity, generated bulk geometry, native sibling ordering, and rejected
imports. [Generated coverage](../regression-coverage.md#generated-invariant-coverage)
names the positive and negative cases. FsCheck reports a replay seed and shrinks
failures to smaller inputs. The pinned Scorecard version recognizes the actual
`FsCheck.Xunit` imports; detection alone does not establish a successful test run
or coverage of database execution paths. Hosted alerts reflect the revision of
the latest hosted analysis.

The proposed Passing assertion answer interprets active test-suite assertions
as satisfying the testing configuration described by the criterion. The
official definition does not explicitly settle that equivalence. If the
maintainer does not adopt that interpretation, record Unmet for this SUGGESTED
criterion instead of adding artificial product instrumentation for a badge.

The Passing definition permits an automated suite with at least 80% **branch**
coverage as dynamic-analysis evidence. The existing collector emits Cobertura
for the two shipping modules. A configured coverage check, line percentage, or
test count alone is not that evidence. The dated measured results and source
comparison are in [Passing evidence](../openssf-best-practices.md#measured-analysis-evidence).
Refresh those results for a changed runtime before making a release claim.
This document introduces no coverage threshold gate.

NestedSet's own shipping source is managed C# without unsafe blocks or native
code. Database engines and providers remain separate dependencies; the
memory-unsafe-source criterion must be reassessed if the project starts
shipping native or unsafe implementation code.

## Cryptographic boundary

The runtime calls .NET `SHA256.HashData` to derive deterministic physical names
for registry tables, indexes, and constraints. Those digests are truncated to
meet identifier limits. They are naming components, not authentication tokens,
confidentiality controls, or claims of cryptographic collision resistance.
Review physical-identity checks and fail-closed migration behavior if naming
changes; do not turn a truncated name hash into a security mechanism.

The library does not implement TLS, password authentication/storage, key
agreement, or cryptographic nonce generation. Database transport encryption
belongs to the configured provider and application. GitHub/NuGet HTTPS,
release signatures, package hashes, and provenance belong to delivery and are
covered by [release verification](release-verification.md).

## Reports, advisories, and credentials

Use the private GitHub reporting channel or the alternate address in
[SECURITY.md](../../SECURITY.md#reporting-a-vulnerability). Track receipt and
first-response dates privately so the maintainer can verify the 14-calendar-day
Passing criterion. A promised response target is not historical response proof.
Check ordinary bug/enhancement acknowledgments against actual reports, and
exclude automated dependency PRs from those counts.

Prioritize critical or actively exploited vulnerabilities immediately. No
publicly known medium-or-higher vulnerability may remain unfixed for more than
60 days if the corresponding Passing criterion is claimed. Coordinate upstream
findings without assuming that an upstream defect cannot affect NestedSet.
Security release notes identify fixed publicly known project vulnerabilities
and their CVE/GHSA identifiers; dependency advisories remain clearly separate.

Use synthetic fixture data and disposable test database credentials. These
public test values are not credentials for private infrastructure. Never commit
a working private token, key, password, or production connection string. If one
is exposed, revoke it first and assess history, artifacts, logs, and dependent
systems; deleting the current text alone is insufficient.

## Primary sources

Retrieved 2026-10-01:

- [OpenSSF Passing definitions and details](https://www.bestpractices.dev/en/criteria/0?details=true).
- [Pinned OpenSSF criterion metadata](https://github.com/ossf/best-practices-badge/blob/e1b85623fd6ccad3283943db5957f3130e6bb84b/criteria/criteria.yml).
- [Saltzer and Schroeder: protection design principles](https://web.mit.edu/Saltzer/www/publications/protection/).
- [.NET code analysis](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview).
- [.NET security analyzer rules](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/security-warnings).
- [.NET SHA256.HashData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256.hashdata?view=net-10.0).
- [Doka security policy](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/SECURITY.md).
- [SafeMigrations secure development](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/blob/main/docs/security/secure-development.md).

Retrieved 2026-10-09:

- [FsCheck xUnit v3 3.4.0 package and dependencies](https://www.nuget.org/packages/FsCheck.Xunit.v3/3.4.0).
- [FsCheck property testing and shrinking](https://fscheck.github.io/FsCheck/).
- [Scorecard 5.5.0 C# detection](https://github.com/ossf/scorecard/blob/v5.5.0/checks/raw/fuzzing.go).
- [Pinned Scorecard action version](https://github.com/ossf/scorecard-action/blob/2d1146689b8cda280b9bc96326124645441f03bc/go.mod).
- [Dependency Review v5.0.0 license handling](https://github.com/actions/dependency-review-action/blob/v5.0.0/src/licenses.ts).
- [Dependency Review v5.0.0 PURL comparison](https://github.com/actions/dependency-review-action/blob/v5.0.0/src/purl.ts).
