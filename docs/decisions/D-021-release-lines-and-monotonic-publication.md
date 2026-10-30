---
id: D-021
status: implemented
date: 2026-10-28
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Exact framework release lines, current publication authority and monotonic Latest"
supersedes: []
superseded-by: []
amends: [D-016]
amended-by: [D-022]
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-021 -- Separate release-line qualification from current publication authority

## Context and Problem Statement

NestedSet must maintain .NET 10 after main adopts a later .NET/EF Core major.
The main-only source, provenance, package-framework and Latest assumptions
cannot provide that contract. An older stable maintenance release must remain
publishable without displacing a higher stable GitHub Latest release.

The owner requested the release-line contract already established by Doka and
SafeMigrations, retaining NestedSet's parallel qualification and six packages.
The change must extend the existing implementation without a second Python
framework, publisher, state database or per-lane receipt protocol.

## Decision Drivers

- Qualify each exact protected source using its own SDK, TFM and dependencies.
- Keep parallel jobs, one canonical build/pack and unchanged candidate bytes.
- Separate frozen qualification from current permission to publish.
- Preserve six packages, signing, existing nuget approval and Trusted Publishing.
- Keep stable Latest monotonic across majors and out-of-order patches.
- Cover maintained lines with PR checks, dependency updates and real audits.
- Preserve original-byte recovery and historical release verification.
- Leave branch creation, framework adoption and hosted activation separate.

## Considered Options

- Extend the existing pipeline with exact release-line contracts
- Copy qualification and publisher workflows for each framework branch
- Keep release qualification and publication restricted to main

## Decision Outcome

Chosen option: "Extend the existing pipeline with exact release-line contracts",
because it reuses the current qualification, artifact, signing and publication
boundaries while supporting independently maintained framework lines.

The reviewed policy admits exact refs, roles, package majors, channels and
upstream stages. Main/10 starts active; maintenance/10.x and optional
preview/11.x are inactive. The x is literal. Inactive lines receive preparation
PR checks; prepared lines permit complete own-line qualification-only; active
lines additionally permit scheduled servicing and authorized publication.
At most one active publication line owns a package major.

Every line executes its own release workflow and toolchain. The parallel DAG,
single build/pack, immutable artifact-ID handoffs, complete results, shared
qualification deadline and exact producer-attempt guards remain intact.
Inspection, consumers and SBOMs use the approved line's framework expectations.
Current source remains .NET 10 with VersionPrefix 10.1.0; this capability does
not adopt .NET 11 or activate another branch.

Qualification freezes line identity in the existing candidate/evidence graph.
Publication independently reads current policy from protected main after
approval and before irreversible boundaries. A candidate's own policy is not
authority. Exact ref/major/channel, current active permission, protected source,
original run, source, tag and bytes must agree. Missing authority fails closed.
Existing historical schemas retain their original verification contract; they
do not grant current publication rights on another line.

All publishers share one non-cancelling queue with queue max. Stable promotion
uses canonical version precedence. Prereleases never become Latest, and an
older stable major or patch cannot displace a higher stable release.
Historical verification does not require continued Latest status or current
publication eligibility.

Normal operation keeps branch/version selection, one qualification, personal
signed tag and existing protected nuget approval. Qualification-only skips the
entire environment-bearing publisher. Original-run failed-publisher recovery
retains the candidate/provenance artifact identities and rechecks current
permission; qualification reruns do not become new bytes under an old tag.
Complete an affected publication before changing its line's ownership or state.
The queue does not lock policy edits, revoke old workflow code or constrain
administrator access.

Active lines receive branch-native daily advisory audits and weekly full CI.
The coordinator verifies exact child identities and actual successful producers,
including every test/sample matrix cell. Dependabot retains all four existing
ecosystems, existing Docker LTS lines and framework-major/prerelease limits.
Registered preparation bases receive exact-base/head dependency review.
Ordinary feature CI remains nonpublishing and independent of line activation.

### Consequences

- Good, because one qualification/publication contract serves maintained majors.
- Good, because the existing six-package, signer and approval boundaries remain.
- Good, because release evidence cannot become perpetual publication rights.
- Bad, because each maintained line still needs reviewed ports and qualification.
- Bad, because hosted branch grants, dispatch and OIDC require real observations.
- Bad, because unavailable authority or expired original evidence blocks retry.

### Confirmation

- Run `python3 -m unittest discover -s eng/tests -p 'test_*.py'` and require
  positive and rejection cases for exact line selection, toolchains, immutable
  identity, current grants, qualifier isolation, retries, Latest, scheduling,
  complete producer inventories and dependency boundaries.
- Preserve the existing parallel workflow and full six-package contracts;
  verify all jobs still use exact producer artifacts and unchanged bytes.
- Run applicable package-inspector/build and syntax checks without changing
  the current framework or introducing a dependency. A linter's older queue
  schema must not disable other workflow diagnostics.
- Run `bash eng/validate-adrs.sh` separately as a documentation check; it does
  not become a blocking CI or RC gate.
- Before activating a new line, observe its real preparation PR, branch-native
  qualification, hosted protections and dispatch. Before claiming publication,
  require signed-tag, environment, OIDC and public readback evidence. Local
  tests alone establish none of those hosted outcomes.
- Local confirmation on 2026-10-28 passed 457 engineering regressions, seven
  CI-diagnostic regressions, a warning-free Release solution build and a fresh
  complete advisory audit of 21 solution projects. All 61 actual workflow Bash
  steps passed the existing ShellCheck rules. Workflow checks found only the
  installed actionlint 1.7.12 schema gap for the three supported queue keys;
  no other workflow diagnostic was suppressed. These observations establish
  local implementation, not hosted activation or a completed release.

## Pros and Cons of the Options

### Extend the existing pipeline with exact release-line contracts

- Good, because one artifact and trust model serves every authorized line.
- Bad, because current permission and historical qualification need clear bounds.

### Copy qualification and publisher workflows for each framework branch

- Good, because a branch can specialize its execution independently.
- Bad, because copies drift in signing, retry, dependency and Latest behavior.

### Keep release qualification and publication restricted to main

- Good, because only one source-line identity needs authorization.
- Bad, because .NET 10 cannot be serviced after main adopts another framework.

## More Information

The [release-line runbook](../operations/release-lines.md) owns setup, activation,
ordinary releases, maintenance and retirement. The existing
[publication procedure](../operations/release-publication.md) retains the
operator commands, and [release verification](../security/release-verification.md)
retains the independent consumer checks. This amends D-016's main-only and
always-Latest assumptions without replacing its parallel qualification contract.

### Re-evaluation Triggers

- GitHub changes queue, selected-ref, rerun, artifact or OIDC semantics.
- Another framework major is explicitly adopted or a maintained line retired.
- NuGet changes normalized versions, signatures or Trusted Publishing identity.
- A real release exposes a recovery or Latest ambiguity absent from the tests.

### Decision History

- 2026-10-28: Decision recorded with status proposed.
- 2026-10-28: Status changed from proposed to accepted.
- 2026-10-28: Owner requested the complete Doka/SafeMigrations release-line contract with parallel qualification preserved, updated documentation, no new Python framework and no commit. Implementation confirmation and real hosted activation remain distinct evidence boundaries.
- 2026-10-28: Status changed from accepted to implemented.
- 2026-10-28: Local engineering/CI regressions, Release build, complete advisory audit and scoped workflow checks confirmed the implementation. Scope review retained direct uploads from the sealed candidate and preserved original source-ref rejection before consumer asset copying. Extra lines remain inactive; no commit, hosted activation or publication is implied. Standalone full ADR validation still reports the separately proven pre-existing D-018/D-019 history/source-format diagnostics; this record and its D-016 amendment validate independently.
- 2026-10-30: D-022 replaces the retained shared qualification deadline with job timeouts and a per-process budget; every release-line rule is unchanged.

### Implementation References

- [Exact line policy](../../eng/release/lines-policy.json)
- [Line and toolchain validation](../../eng/release/lines.py)
- [Existing qualification](../../eng/release/qualification.py)
- [Existing publication](../../eng/release/publication.py)
- [Parallel release workflow](../../.github/workflows/release-candidate.yml)
- [Branch-native maintenance](../../.github/workflows/release-line-maintenance.yml)
- [Maintenance and dependency helpers](../../eng/release/maintenance.py)
- [Existing CI](../../.github/workflows/ci.yml)
- [Dependency Review](../../.github/workflows/dependency-review.yml)
- [Policy and toolchain regressions](../../eng/tests/test_release_lines.py)
- [Current publication and recovery regressions](../../eng/tests/test_release_line_publication.py)
- [Branch-native qualification regressions](../../eng/tests/test_release_line_qualification.py)
- [Monotonic Latest regressions](../../eng/tests/test_release_latest.py)
- [Maintenance and audit regressions](../../eng/tests/test_release_maintenance.py)
- [Dependency-target regressions](../../eng/tests/test_release_dependencies.py)
- [Release lines runbook](../operations/release-lines.md)

### Sources

- [GitHub workflow dispatch and schedules](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows) (primary source; retrieved 2026-10-28)
- [GitHub same-commit reusable workflows](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows#calling-a-reusable-workflow) (primary source; retrieved 2026-10-28)
- [GitHub concurrency queues](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency) (primary source; retrieved 2026-10-28)
- [GitHub workflow reruns](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs) (primary source; retrieved 2026-10-28)
- [GitHub release Latest controls](https://docs.github.com/en/rest/releases/releases#update-a-release) (primary source; retrieved 2026-10-28)
- [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) (primary source; retrieved 2026-10-28)
- [Dependabot target branches](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference#target-branch) (primary source; retrieved 2026-10-28)
