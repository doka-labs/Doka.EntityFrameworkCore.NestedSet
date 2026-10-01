# OpenSSF Best Practices evidence

The public self-assessment is [NestedSet project 15143](https://www.bestpractices.dev/en/projects/15143).
It was registered and achieved **Passing (100%)** on 2026-10-01, verified against
the public project entry. This is a project self-assessment, not a certification
or a Silver/Gold achievement. Scorecard is a separate assessment. This page
supplies evidence for the 67 Passing criteria; it does not save hosted answers.

The inventory was checked on 2026-10-01 against the live Passing page and
OpenSSF source revision `e1b85623fd6ccad3283943db5957f3130e6bb84b`:
43 MUST, 10 SHOULD, and 14 SUGGESTED criteria. `Met` below is an evidence-backed
proposed answer, not the saved badge state. `Confirm` needs maintainer knowledge
or private history. `Pending` needs the actual first release or its exact
revision's hosted evidence. `N/A` is used only where the definition allows it.

MUST criteria need Met or a permitted N/A. A justified unmet SHOULD or an unmet
SUGGESTED criterion can still permit Passing. Unknown answers do not complete
the assessment. Do not select N/A merely because this is a new project.

## Basics

| Criterion | Level | Proposed answer and evidence |
| --- | --- | --- |
| `description_good` | MUST | Met: [README](../README.md) describes persistent ordered EF hierarchies and their uses |
| `interact` | MUST | Met: [README](../README.md), [Support](../SUPPORT.md), and [Contributing](../CONTRIBUTING.md) explain obtaining source, feedback, and contributions |
| `contribution` | MUST | Met: [Contributing](../CONTRIBUTING.md) defines the reviewed PR process |
| `contribution_requirements` | SHOULD | Met: [Contributing](../CONTRIBUTING.md) defines formatting, documentation, API, and test requirements |
| `floss_license` | MUST | Met: project software is [MIT](../LICENSE); the attributed Code of Conduct has its separately stated CC BY-SA 4.0 license |
| `floss_license_osi` | SUGGESTED | Met: [MIT is OSI-approved](https://opensource.org/license/mit); the conduct document is not a software-license exception |
| `license_location` | MUST | Met: root [LICENSE](../LICENSE) and shipping package license metadata |
| `documentation_basics` | MUST | Met: [Getting Started](getting-started.md), samples, configuration, and operations |
| `documentation_interface` | MUST | Met: [API Reference](api-reference.md), operation contracts, and package XML documentation |
| `sites_https` | MUST | Met: GitHub and the OpenSSF project use HTTPS; the prepared NuGet delivery route also requires HTTPS |
| `discussion` | MUST | Met: public [issues](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/issues) and PR discussions have searchable, addressable browser access |
| `english` | SHOULD | Met: public documentation and contribution/reporting paths accept US English |
| `maintained` | MUST | Met: active [commit history](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/commits/main/) and [roadmap](../ROADMAP.md) |

## Change control

| Criterion | Level | Proposed answer and evidence |
| --- | --- | --- |
| `repo_public` | MUST | Met: public [GitHub source](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet) |
| `repo_track` | MUST | Met: Git records content, authors, and timestamps |
| `repo_interim` | MUST | Met: Git history contains development changes between release boundaries |
| `repo_distributed` | SUGGESTED | Met: Git is distributed version control |
| `version_unique` | MUST | Met: [Release governance](release-governance.md) assigns both packages one immutable unique version per release |
| `version_semver` | SUGGESTED | Met: [Changelog](../CHANGELOG.md) and governance use SemVer, starting with prerelease 10.0.0-rc.1 before stable 10.0.0 |
| `version_tags` | SUGGESTED | Pending: signed `v10.0.0-rc.1` tag after candidate qualification; no first-release tag is claimed now |
| `release_notes` | MUST | Met for prepared notes: reviewed [10.0.0-rc.1 notes](../CHANGELOG.md) describe the proposed RC; attach them to the actual release before claiming delivery |
| `release_notes_vulns` | MUST | N/A while no publicly known project vulnerability has been fixed; recheck [advisories](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/security/advisories) for the release |

## Reporting

| Criterion | Level | Proposed answer and evidence |
| --- | --- | --- |
| `report_process` | MUST | Met: [Support](../SUPPORT.md) and issue forms define ordinary bug reporting |
| `report_tracker` | SHOULD | Met: public GitHub issue tracker |
| `report_responses` | MUST | Met as maintainer self-assessment of no received reports: confirmed on 2026-10-01 across public and other channels; no response percentage or official new-project exemption is claimed. N/A is not allowed |
| `enhancement_responses` | SHOULD | Met as maintainer self-assessment of no received requests: confirmed on 2026-10-01 across channels; automated dependency PRs are excluded. Reassess when requests arrive |
| `report_archive` | MUST | Met: [closed and open issues](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/issues?q=is%3Aissue) preserve searchable reports and responses |
| `vulnerability_report_process` | MUST | Met: [Security Policy](../SECURITY.md#reporting-a-vulnerability) documents private reporting and disclosure |
| `vulnerability_report_private` | MUST | Met: hosted private vulnerability reporting was enabled on 2026-10-01; [private form](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/security/advisories/new) and alternate email are documented |
| `vulnerability_report_response` | MUST | N/A: no private reports in six months, confirmed by the maintainer on 2026-10-01; the complete public issue inventory also contained no vulnerability reports, and no project advisories were present |

## Quality

| Criterion | Level | Proposed answer and evidence |
| --- | --- | --- |
| `build` | MUST | Met: working SDK/MSBuild build documented in [Contributing](../CONTRIBUTING.md#build-and-test) |
| `build_common_tools` | SUGGESTED | Met: standard `dotnet restore/build/pack` and MSBuild projects |
| `build_floss_tools` | SHOULD | Met: .NET SDK, MSBuild, and Roslyn build the shipping libraries; proprietary SQL Server is an integration-test dependency, not a compiler prerequisite |
| `test` | MUST | Met: MIT-licensed [test source](../tests) and documented xUnit suites |
| `test_invocation` | SHOULD | Met: standard `dotnet test` command |
| `test_most` | SUGGESTED | Met: [regression matrix](regression-coverage.md) plus measured shipping-module branch coverage below |
| `test_continuous_integration` | SUGGESTED | Met: [PR CI](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/actions/workflows/ci.yml) runs named parallel build, analysis, tests, and sample checks |
| `test_policy` | MUST | Met: [test requirements](../CONTRIBUTING.md#test-structure) require coverage for new functionality and regressions |
| `tests_are_added` | MUST | Met: typed-runtime and provider-structure changes include [positive/negative/adversarial regressions](regression-coverage.md) in source history |
| `tests_documented_added` | SUGGESTED | Met: Contributing documents the policy for change proposals |
| `warnings` | MUST | Met: [build properties](../Directory.Build.props) enable nullable and recommended SDK analysis |
| `warnings_fixed` | MUST | Met: warnings fail Release builds; proposed release must retain a clean build result |
| `warnings_strict` | SUGGESTED | Met: warnings as errors, enforced style, public API analysis, and XML documentation |

## Security

| Criterion | Level | Proposed answer and evidence |
| --- | --- | --- |
| `know_secure_design` | MUST | Met by maintainer attestation on 2026-10-01, not by AI inference: knowledge of secure software design; exact principles are listed in [Secure development](security/secure-development.md#developer-knowledge) |
| `know_common_errors` | MUST | Met by maintainer attestation on 2026-10-01: knowledge of typical EF/database-library vulnerabilities and countermeasures |
| `crypto_published` | MUST | Met: .NET SHA-256 derives deterministic physical names; no custom algorithm |
| `crypto_call` | SHOULD | Met: runtime calls framework `SHA256.HashData` rather than implementing a hash |
| `crypto_floss` | MUST | Met: .NET's SHA-256 implementation is available as FLOSS |
| `crypto_keylength` | MUST | N/A: the library supplies no cryptographic key-based security mechanism; truncated naming hashes are not such a mechanism |
| `crypto_working` | MUST | N/A: no library security protocol or cipher mode; naming uses SHA-256, not a broken hash |
| `crypto_weaknesses` | SHOULD | N/A: no cryptographic security mechanism; see the [actual naming boundary](security/secure-development.md#cryptographic-boundary) |
| `crypto_pfs` | SHOULD | N/A: no key agreement protocol |
| `crypto_password_storage` | MUST | N/A: no external-user password authentication/storage; ordinary database connection credentials belong to the application/provider |
| `crypto_random` | MUST | N/A: no cryptographic key/nonce generation; tree identifiers are not secrets |
| `delivery_mitm` | MUST | Met for current HTTPS source delivery; retain HTTPS NuGet/release readback for 10.0.0-rc.1 |
| `delivery_unsigned` | MUST | Met: no unauthenticated HTTP hash delivery; [verification](security/release-verification.md) uses authenticated HTTPS and release signatures |
| `vulnerabilities_fixed_60_days` | MUST | Met at dated check: no project advisory was present; recheck public advisories and private confirmed findings before release |
| `vulnerabilities_critical_fixed` | SHOULD | Met at dated check: no known open critical project advisory; [Security Policy](../SECURITY.md#response-and-coordinated-disclosure) prioritizes critical/actively exploited defects |
| `no_leaked_credentials` | MUST | Met at dated check: secret scanning and push protection enabled, zero secret alerts; public disposable test credentials do not secure private infrastructure |

## Analysis

| Criterion | Level | Proposed answer and evidence |
| --- | --- | --- |
| `static_analysis` | MUST | Met for analyzed source: SDK .NET analyzers plus successful hosted CodeQL; retain results for the exact final proposed release revision |
| `static_analysis_common_vulnerabilities` | SUGGESTED | Met: .NET security rules and CodeQL analyze common source vulnerability patterns |
| `static_analysis_fixed` | MUST | Met at dated check: CodeQL's successful current-main analysis reported zero results; Scorecard process warnings are separate |
| `static_analysis_often` | SUGGESTED | Met: SDK analysis runs for PR creation/new commits/reopening against main, including forks; configured CodeQL scans default/protected-branch pushes and non-fork PR revisions, with weekly supplementary scans |
| `dynamic_analysis` | SUGGESTED | Met for measured source: automated suites exceed 80% branch coverage in each shipping module; refresh after runtime changes |
| `dynamic_analysis_unsafe` | SUGGESTED | N/A: shipping implementation is managed C# without unsafe/native source |
| `dynamic_analysis_enable_assertions` | SUGGESTED | Met as maintainer interpretation of active xUnit assertions during Release tests; no separate product assertion instrumentation is claimed. The official definition does not expressly settle this interpretation; Unmet is a permissible SUGGESTED alternative |
| `dynamic_analysis_fixed` | MUST | Met at dated check: no known confirmed medium-or-higher exploitable dynamic-analysis finding remains; correctness regressions are fixed and tested |

## Measured analysis evidence

The successful [CI run 36774036411](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/actions/runs/36774036411)
ran against PR revision `0a1f052463536c099c79eaf84967c2cffbfeaed6`. Its original
Cobertura artifacts, downloaded and inspected on 2026-10-01, report:

| Shipping module | Artifact / suite | Branch coverage | Line coverage |
| --- | --- | --- | --- |
| `Doka.NestedSet` | `ci-tests-core-36774036411-1` | 100.00% | 96.67% |
| `Doka.EntityFrameworkCore.NestedSet` | `ci-tests-mysql-36774036411-1` | 81.00% | 91.18% |

These are each module's own `package` measurements, not the average of repeated
provider reports. No fabricated union or line-to-branch substitution is used.
The GitHub comparison to main `d2ab4669c6165668dddb28a324c6570bf934f2ca` changes no
shipping C# or test source; it changes delivery configuration, documentation,
image pins, and package properties. This is historical source evidence, not a
hosted qualification of an unpublished candidate. The first hosted RC process
for `10.0.0-rc.1` has not been executed yet.

Hosted [CodeQL run 36895136867](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/actions/runs/36895136867)
succeeded on that main revision on 2026-10-01; the associated successful
analysis records have zero results. The current code-scanning alert list also
contains Scorecard process warnings. Those are not silently classified as
source vulnerabilities or claimed fixed by this document.

The hosted default-setup API readback on 2026-10-01 reports C#, Actions, and
Python configured, with the default query suite and weekly schedule. GitHub's
documented default-setup contract also scans default/protected-branch pushes
and non-fork PR revisions. PR CI's SDK analyzers cover fork PR revisions as well.
No daily CodeQL schedule is claimed, and a single successful scan is not used
as proof of scan frequency.

## Hosted state and remaining confirmations

Read-only API checks on 2026-10-01 confirmed a public MIT repository, private
vulnerability reporting, release immutability, secret scanning, push protection,
zero secret alerts, and no project advisories or releases. These are dated
observations. Follow [Repository settings](runbooks/repository-settings.md) to
recheck them; configuration in source alone does not prove hosted state.

No non-PR bug or enhancement issues were present. On 2026-10-01 the maintainer
also confirmed no ordinary reports or requests through other channels. The
proposed Met answers disclose that empty population, with no percentage invented
from Dependabot PRs or an empty denominator. This is a maintainer interpretation
for a newly public project, not an official new-project exemption: the definitions
allow no N/A and give no explicit empty-population rule. If that interpretation
is not adopted, retain Unknown and seek clarification rather than fabricating
history. Reassess both answers when actual reports or requests arrive.

The maintainer confirmed the two developer-knowledge criteria and absence of
private security reports on 2026-10-01. Publish evidence documents through
review and complete the actual release's notes/tag/readback. Passing was verified
on the public entry on 2026-10-01; update and read back its answers when the
underlying evidence changes. No Silver/Gold, human security
audit, multi-maintainer continuity, independent reproducible build, or unexecuted
fuzzing claim is made. No additional workflow, coverage gate, or ADR gate is
introduced for the badge.

## Primary sources

Retrieved 2026-10-01:

- [Passing criteria and details](https://www.bestpractices.dev/en/criteria/0?details=true).
- [Pinned criterion identifiers, levels, and N/A rules](https://github.com/ossf/best-practices-badge/blob/e1b85623fd6ccad3283943db5957f3130e6bb84b/criteria/criteria.yml).
- [Pinned English definitions](https://github.com/ossf/best-practices-badge/blob/e1b85623fd6ccad3283943db5957f3130e6bb84b/config/locales/en.yml).
- [Badge scoring and explanation rules](https://www.bestpractices.dev/en/criteria_discussion#achieving-a-badge).
- [GitHub default-setup scan triggers](https://docs.github.com/en/code-security/concepts/code-scanning/setup-types#about-default-setup).
- [Doka evidence structure](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/openssf-best-practices.md).
- [SafeMigrations evidence structure](https://github.com/doka-labs/Doka.EntityFrameworkCore.SafeMigrations/blob/main/docs/openssf-best-practices.md).

The criteria are licensed by their upstream project. This mapping paraphrases
them for NestedSet; the official definitions remain authoritative.
