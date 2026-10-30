---
id: D-016
status: implemented
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Local qualification, hosted candidate identity, approval, publication, and verification"
supersedes: []
superseded-by: []
amends: []
amended-by: [D-021, D-022]
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-016 -- Qualify immutable candidates before operator-controlled publication

## Context and Problem Statement

A successful source build does not prove that the packages an application restores contain the tested code,
symbols, dependencies, and documentation. Publishing related packages also creates a partial-failure boundary.
The accepted release path therefore binds qualification to concrete candidate bytes and separates reversible local
checks from operator-controlled hosted publication.

The serial stable qualification on 2026-10-09 took about 91 minutes and passed
6,704 of 6,705 tests. The remaining MySQL ten-million-node case timed out in
the additional `CapacitySeed.MismatchesAsync` oracle after its read and full
validation had passed. This failed qualification motivates clearer independent
jobs; it does not establish a qualified stable candidate or a parallel speedup.

## Decision Drivers

- Check offline release-tooling regressions in pull-request CI while release qualification verifies the complete package contract from source.
- Bind source, dependency, package, symbol, SBOM, and run identity before approval.
- Revalidate an operator-signed tag and candidate immediately before short-lived NuGet authentication.
- Make partial publication, expiry, and mismatched public bytes visible without silently rebuilding the candidate.
- Run independent checks concurrently while testing the exact canonical build and retaining every required result.

## Considered Options

- Shared qualification with immutable candidate evidence and guarded publication
- Build and publish packages directly from a version tag
- Manual package upload after local tests

## Decision Outcome

Chosen option: "Shared qualification with immutable candidate evidence and guarded publication", because the
published package is the consumer contract, and shared checks give pull requests and hosted RCs the same
definition of readiness. Publication remains a separate authorized action. Dependabot lockfile diagnostics run
only for pull requests.

[D-021](D-021-release-lines-and-monotonic-publication.md) amends the main-only
release assumptions with exact branch-owned qualification, current
protected-main publication authority and monotonic Latest. It preserves this
decision's parallel six-package and original-byte recovery boundaries.

[D-022](D-022-shared-ci-and-release-qualification.md) replaces the release-only
job graph: CI and the release candidate call one shared quality workflow. Its
build job restores owned shipping locks, builds the Release solution once with
the selected version, packs each shipping package once and inspects all primary
and symbol archives with the inspector built in that solution. Package
consumers and SBOMs verify that immutable package artifact in every run. Test
and sample cells build their own projects from the same commit; the shared
MySQL/MariaDB project uses complementary filtered cells that must partition it.
Development builds use the SDK's shared version prefix and suffix so package
and assembly informational versions agree. Release qualification supplies the
exact reviewed version to both the build and pack operations.

The final `qualify` job needs the preflight and the complete shared call and
rejects failed, skipped, or canceled results. The package, consumer, and SBOM
artifact IDs pass through workflow outputs; test artifacts are selected by
their current run-and-attempt prefix, one directory per cell. The pinned
artifact action verifies downloaded digests; no second job receipt schema,
artifact-index API, or role/file inventory repeats that transport.

Final qualification restores the build identity of the same run and attempt and
requires passing, non-repeated results for every executable test project, every
required query-plan family, executed lines in all six shipping modules,
successful version-matching consumers whose recorded primary-package hashes
equal the inspected packages being sealed, and independently verified SBOM
evidence. Missing or conflicting required results prevent sealing. It assembles
the existing candidate without building or packing again; the candidate
artifact ID passes unchanged to attestation and publication. The final job does
not restore packages or set up .NET. Repeat all jobs or start a new dispatch
before sealing; build identity and test selection require the current attempt.
Recovery of an already sealed candidate retains its original artifact IDs and
the existing attestation/publication checks.

Approved SSH release-tag signers are recorded in `.github/allowed_signers`,
following Doka and SafeMigrations. Signer changes require a reviewed protected-line
pull request. Publication verifies the tag directly against this file and retains
GitHub's independent signature verdict, without extra hosted signer configuration.

### Consequences

- Good, because a candidate can be reviewed through explicit manifests and operator evidence before any registry write.
- Bad, because a fully passing pull-request run cannot prove hosted OIDC, environment protection, signer configuration, or a completed public release.

### Confirmation

- Run `python3 -m unittest discover -s eng/tests` and expect identity, archive, fresh-run, and publication-boundary regression tests to pass. These fixtures must reject changed candidate bytes, signer mismatch, stale evidence, and conflicting public packages. ADR validation and its fixtures are separate documentation checks.
- Run the shared quality workflow and require fresh full provider tests, coverage, samples, package consumers, SBOMs, and deterministic mutation evidence. Syntax or stub checks must not substitute for runtime qualification.
- Qualify the shared flow on fresh hosted Linux x86-64 runners as [D-022](D-022-shared-ci-and-release-qualification.md) confirms. Require all executable test projects and three samples, complete matching evidence, unchanged packages, and successful candidate handoff.
- Before publication, require the linked operator checklist to confirm exact run and attempt identity, approved signed tag, qualified-line reachability and current independent publication authority, protected environment approval, and verified public package/symbol readback. These external checks remain unexecuted until a real authorized release.

## Pros and Cons of the Options

### Shared qualification with immutable candidate evidence and guarded publication

- Good, because reviewers can approve the same artifact bytes that passed verification and recover only the matching publication attempt.
- Bad, because operator setup, evidence retention, signed tags, and bounded public readback add release procedure and failure states.

### Build and publish packages directly from a version tag

- Good, because a compact workflow can be sufficient for a small single-package repository.
- Bad, because a tag alone does not bind prior qualification to exact archives or explain mismatched bytes during partial publication.

### Manual package upload after local tests

- Good, because a maintainer can control every external action without hosted credential setup.
- Bad, because reproducibility, artifact provenance, and multi-package retry consistency depend on manually preserved evidence.

## More Information

The first RC, `10.0.0-rc.1`, was published on 2026-10-02 as an immutable GitHub release with thirteen
candidate, qualification, package, symbol, SBOM, and provenance assets. Both package identities are publicly
available on NuGet. Readback on 2026-10-06 confirms these states; the sources below record the evidence.
This implements the accepted release design for that RC. Stable `10.0.0` establishes the first stable source
contract and uses the same qualification, approval, and exact-package publication process.
A dirty local workspace is explicitly non-publishable. NuGet/login exchanges the protected workflow's OIDC identity for
short-lived publish credentials after candidate and tag checks. Binding the trusted publishing policy to the
repository, workflow, and protected environment limits where those credentials can be obtained.

NuGet repository signing can change archive bytes. Public verification must therefore distinguish signed package
content from transport bytes; the original unsigned ZIP hash is not an equality test for its repository-signed
download. Signature verification and comparison with the qualified package content are separate checks in the
linked publication and consumer-verification procedures.

Publish all six primary packages in dependency order before any symbol package. A symbol-service failure must not
prevent the EF primary package from reaching NuGet. On a same-run retry, the public payload preflight rejects
conflicts before duplicate-tolerant pushes, and public package and PDB readback still determine completion.

### Re-evaluation Triggers

- A release requires additional shipping packages, another registry, or a different signing identity.
- Hosted artifact retention, attestation, NuGet signing, or symbol readback changes break the evidence contract.
- An actual partial publication reveals a state not covered by the recovery table.

### Decision History

- 2026-09-19: Decision recorded with status proposed.
- 2026-09-19: Existing source and planned delivery boundaries documented without reconstructing historical approval.
- 2026-09-19: Added primary NuGet signing and trusted-publishing sources; clarified the durable credential boundary.

- 2026-09-28: Status changed from proposed to accepted.
- 2026-09-28: The maintainer accepted the current decision and designated the core-maintainers audience. Complete RC qualification and authorized hosted publication remain pending; existing scripts do not establish those outcomes.
- 2026-09-30: Separated ADR profile validation from blocking qualification at the maintainer's request; package and publication evidence requirements remain unchanged.
- 2026-09-30: Limited required pull-request CI to direct C# checks and pack. RC runs the complete package, consumer, SBOM, and provenance qualification from its selected main commit in one fresh pass, without resumable qualification receipts. Publication still verifies the sealed candidate and same-run provenance.
- 2026-09-30: Split CI into independently named parallel checks and project/sample matrices, retaining the complete existing checks and stable required aggregate. RC qualification remains independent of CI artifacts.
- 2026-09-30: Aligned the two-package upload order with the SafeMigrations multi-package release: both primary packages precede symbol uploads. The Core package remains before its dependent EF package; same-run recovery and readback remain mandatory.
- 2026-09-30: Added early source-version and dated-notes checks, PR-time release-tooling regressions, and a 120-minute NuGet readback inside a 180-minute publish job. GitHub asset verification shares a retry budget. Negative SBOM fixtures remain offline while the RC validates the actual candidate.
- 2026-09-30: Aligned the operator runbook and no-argument `eng/pre-tag-check.sh` entry point with Doka and SafeMigrations. Readiness shares the existing source and signing checks; exact version and changelog validation remains mandatory in hosted preflight. Publication order and qualification gates are unchanged.
- 2026-09-30: Restored offline archive inspection to the independent CI build job and retained its manifest. RC result coverage requires only executable test projects; shared specification libraries are excluded. API tokens are scoped to the jobs that use GitHub APIs. ADR validation remains a separate maintainer check.
- 2026-09-30: Aligned development package and assembly informational versions through the SDK's VersionPrefix/VersionSuffix properties. The actual package inspector rejects version drift; the explicit reviewed release-version override remains unchanged.
- 2026-10-02: Replaced the extra hosted signer configuration with the versioned `.github/allowed_signers` file at the maintainer's request, following Doka and SafeMigrations. Local Git and hosted tag-signature verification remain required.
- 2026-10-06: Status changed from accepted to implemented.
- 2026-10-06: Read back the actual immutable RC and both public NuGet identities, preserving the earlier pending statements as dated history. Established stable 10.0.0 metadata, shipped API baselines, and operator guidance without changing the release mechanism or treating RC artifacts as stable evidence.
- 2026-10-09: At the maintainer's direction, evolved hosted qualification into fixed independent jobs sharing one canonical build, one deadline, complete current-attempt evidence, and final sealing. The implemented first-RC status and history remain intact; fresh hosted qualification and a measured timing comparison of this evolution remain pending. Signers, publication permissions, protected approval, OIDC, publication order, and readback budgets are unchanged. The first implementation selected job artifacts through read-only Actions access.
- 2026-10-09: Removed the added job receipt schema, role/file inventories, and artifact-index API at the maintainer's direction. GitHub needs results, scalar producer artifact IDs, current-attempt matrix names, and the pinned download action now own job completion and artifact transport. Final product checks, original identity and byte guards, and publication controls remain required. The final job needs neither GitHub API credentials nor .NET setup. This removes duplicated orchestration without dropping test, coverage, sample, consumer, or SBOM evidence.
- 2026-10-09: Removed unused default NuGet caching from compiled test/sample and isolated consumer/SBOM jobs. Final sealing compares existing consumer primary-package hashes with the inspected build-package manifest; no new receipt schema or validator was introduced. Scalar artifact IDs remain native transport metadata rather than additional portable evidence fields.
- 2026-10-09: Corrected the blanket claim that partial reruns cannot retain evidence from an earlier attempt. Successful same-run, same-commit source/tooling logs may be reused by native job outputs; downloaded build identity, current-attempt test/sample selection, and package-byte checks remain enforced. Full reruns or new dispatches remain the operator recommendation; retaining preflight does not refresh its deadline. No code or additional enforcement layer was introduced.

- 2026-10-28: D-021 records the owner-approved release-line extension, preserving parallel qualification, six packages and original-byte recovery while replacing main-only source and always-Latest assumptions. Extra lines remain inactive.
- 2026-10-30: D-022 replaces the release-only job graph, compiled-byte transport, shared deadline and local serial qualification with the shared quality workflow. Packages, consumers, SBOMs, the sealed candidate, attestation and publication guards are unchanged.

### Implementation References

- [Operator pre-tag entry point](../../eng/pre-tag-check.sh)
- [Shared identity and manifest checks](../../eng/release/common.py)
- [Shared qualification runner](../../eng/release/qualification.py)
- [Publication guards](../../eng/release/publication.py)
- [Approved release signers](../../.github/allowed_signers)
- [NuGet verification](../../eng/release/nuget.py)
- [Hosted continuous integration](../../.github/workflows/ci.yml)
- [Shared quality workflow](../../.github/workflows/quality-gates.yml)
- [Hosted release candidate](../../.github/workflows/release-candidate.yml)
- [Release contract](../../docs/release-process.md)
- [Publication runbook](../../docs/operations/release-publication.md)
- [Repository settings](../../docs/runbooks/repository-settings.md)
- [Consumer verification](../../docs/security/release-verification.md)

### Sources

- [GitHub reruns retain the original commit and ref](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs) (primary source; retrieved 2026-10-09)
- [GitHub reruns failed jobs and their dependents](https://docs.github.com/en/rest/actions/workflow-runs#re-run-failed-jobs-from-a-workflow-run) (primary source; retrieved 2026-10-09)
- [Pinned SDK action cache post-job behavior](https://github.com/actions/setup-dotnet/blob/a98b56852c35b8e3190ac28c8c2271da59106c68/src/cache-save.ts) (primary source; retrieved 2026-10-09)
- [Pinned SDK action optional cache inputs](https://github.com/actions/setup-dotnet/blob/a98b56852c35b8e3190ac28c8c2271da59106c68/action.yml) (primary source; retrieved 2026-10-09)
- [Failed serial stable qualification](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/actions/runs/37912270515) (primary source; retrieved 2026-10-09)
- [GitHub direct job dependencies and results](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts#needs-context) (primary source; retrieved 2026-10-09)
- [Pinned artifact upload and TAR mode preservation](https://github.com/actions/upload-artifact/blob/v7.0.1/README.md) (primary source; retrieved 2026-10-09)
- [Pinned artifact download digests and merged output paths](https://github.com/actions/download-artifact/blob/v8.0.1/README.md) (primary source; retrieved 2026-10-09)
- [Pinned artifact download ID handling](https://github.com/actions/download-artifact/blob/v8.0.1/src/download-artifact.ts#L94-L162) (primary source; retrieved 2026-10-09)
- [VSTest execution of compiled test DLLs](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-vstest) (primary source; retrieved 2026-10-09)
- [Published immutable first RC](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/releases/tag/v10.0.0-rc.1) (primary source; retrieved 2026-10-06)
- [First RC candidate identity](https://github.com/doka-labs/Doka.EntityFrameworkCore.NestedSet/releases/download/v10.0.0-rc.1/candidate.json) (primary source; retrieved 2026-10-06)
- [First RC annotated-tag verification](https://api.github.com/repos/doka-labs/Doka.EntityFrameworkCore.NestedSet/git/tags/b067f7caf94f09f770822d66e5b7185fef34a9d2) (primary source; retrieved 2026-10-06)
- [Core NuGet version inventory](https://api.nuget.org/v3-flatcontainer/doka.nestedset/index.json) (primary source; retrieved 2026-10-06)
- [EF NuGet version inventory](https://api.nuget.org/v3-flatcontainer/doka.entityframeworkcore.nestedset/index.json) (primary source; retrieved 2026-10-06)
- [Git SSH allowed-signers policy](https://git-scm.com/docs/git-config#Documentation/git-config.txt-gpgsshallowedSignersFile) (primary source; retrieved 2026-10-02)
- [NuGet signed packages](https://learn.microsoft.com/en-us/nuget/reference/signed-packages-reference) (primary source; retrieved 2026-09-19)
- [NuGet package content hash design](https://github.com/NuGet/Home/wiki/Nupkg-Metadata-File) (primary source; retrieved 2026-09-19)
- [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) (primary source; retrieved 2026-09-19)
- [NuGet package validation and indexing](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package) (primary source; retrieved 2026-09-30)
- [GitHub Actions job timeouts](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax) (primary source; retrieved 2026-09-30)
- [GitHub Actions job matrices and parallelism](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/run-job-variations) (primary source; retrieved 2026-09-30)
- [GitHub Actions job dependencies and result conditions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idneeds) (primary source; retrieved 2026-09-30)
- [NuGet MSBuild package version defaults](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets#pack-target) (primary source; retrieved 2026-09-30)
- [.NET generated assembly version attributes](https://learn.microsoft.com/en-us/dotnet/standard/assembly/set-attributes-project-file) (primary source; retrieved 2026-09-30)
