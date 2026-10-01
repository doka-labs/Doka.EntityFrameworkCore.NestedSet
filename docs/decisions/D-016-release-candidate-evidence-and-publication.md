---
id: D-016
status: accepted
date: 2026-09-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Local qualification, hosted candidate identity, approval, publication, and verification"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-016 -- Qualify immutable candidates before operator-controlled publication

## Context and Problem Statement

A successful source build does not prove that the packages an application restores contain the tested code,
symbols, dependencies, and documentation. Publishing two related packages also creates a partial-failure boundary.
The accepted release path therefore binds qualification to concrete candidate bytes and separates reversible local
checks from operator-controlled hosted publication.

## Decision Drivers

- Check offline release-tooling regressions in pull-request CI while release qualification verifies the complete package contract from source.
- Bind source, dependency, package, symbol, SBOM, and run identity before approval.
- Revalidate an operator-signed tag and candidate immediately before short-lived NuGet authentication.
- Make partial publication, expiry, and mismatched public bytes visible without silently rebuilding the candidate.

## Considered Options

- Shared qualification with immutable candidate evidence and guarded publication
- Build and publish packages directly from a version tag
- Manual package upload after local tests

## Decision Outcome

Chosen option: "Shared qualification with immutable candidate evidence and guarded publication", because the
published package is the consumer contract, and one release runner gives local and hosted RCs the same definition
of readiness. Pull-request CI uses direct .NET and offline engineering checks so release-only services cannot block a product change.
Publication remains a separate authorized action. PR-only diagnostics do not enter the RC qualification run.

CI runs independent source, engineering, build/package, test-project, and sample
jobs. Each executable test project and SQLite sample owns a named matrix cell
and runner. Coverage waits for complete test evidence; the stable Repository
qualification check rejects any failed, skipped, or canceled gate. Project-local
builds avoid transporting or sharing coverage-instrumented binary outputs.
The build job inspects both primary and both symbol archives before uploading
them, using the inspector already built with the solution. This binds package
metadata, XML documentation, source commit, and PE/PDB identity without another
pack or a dependency on consumer restores and SBOM services.
Development builds use the SDK's shared version prefix and suffix so package
and assembly informational versions agree. Release qualification supplies the
exact reviewed version to both the build and pack operations.

Approved SSH release-tag signers are recorded in `.github/allowed_signers`,
following Doka and SafeMigrations. Signer changes require a reviewed protected-main
pull request. Publication verifies the tag directly against this file and retains
GitHub's independent signature verdict, without extra hosted signer configuration.

### Consequences

- Good, because a candidate can be reviewed through explicit manifests and operator evidence before any registry write.
- Bad, because a fully passing local run cannot prove hosted OIDC, environment protection, signer configuration, or a completed public release.

### Confirmation

- Run `python3 -m unittest discover -s eng/tests` and expect identity, archive, fresh-run, and publication-boundary regression tests to pass. These fixtures must reject changed candidate bytes, signer mismatch, stale evidence, and conflicting public packages. ADR validation and its fixtures are separate documentation checks.
- Run the complete local qualification procedure in the linked release runbook and require fresh full provider tests, coverage, sample, package-consumer, SBOM, and deterministic mutation evidence. Syntax or stub checks must not substitute for runtime qualification.
- Before publication, require the linked operator checklist to confirm exact run and attempt identity, approved signed tag, current-main reachability, protected environment approval, and verified public package/symbol readback. These external checks remain unexecuted until a real authorized release.

## Pros and Cons of the Options

### Shared qualification with immutable candidate evidence and guarded publication

- Good, because reviewers can approve the same artifact bytes that passed verification and recover only the matching publication attempt.
- Bad, because operator setup, evidence retention, signed tags, and bounded public readback add release procedure and failure states.

### Build and publish packages directly from a version tag

- Good, because a compact workflow can be sufficient for a small single-package repository.
- Bad, because a tag alone does not bind prior qualification to exact archives or explain mismatched bytes during partial publication.

### Manual package upload after local tests

- Good, because a maintainer can control every external action without hosted credential setup.
- Bad, because reproducibility, artifact provenance, and two-package retry consistency depend on manually preserved evidence.

## More Information

The maintainer accepted this release design; complete RC qualification and hosted publication remain pending.
A dirty local workspace is explicitly non-publishable. NuGet/login exchanges the protected workflow's OIDC identity for
short-lived publish credentials after candidate and tag checks. Binding the trusted publishing policy to the
repository, workflow, and protected environment limits where those credentials can be obtained.

NuGet repository signing can change archive bytes. Public verification must therefore distinguish signed package
content from transport bytes; the original unsigned ZIP hash is not an equality test for its repository-signed
download. Signature verification and comparison with the qualified package content are separate checks in the
linked publication and consumer-verification procedures.

Publish both primary packages in dependency order before either symbol package. A symbol-service failure must not
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

### Implementation References

- [Operator pre-tag entry point](../../eng/pre-tag-check.sh)
- [Local release entry point](../../eng/release-candidate.sh)
- [Shared identity and manifest checks](../../eng/release/common.py)
- [Shared qualification runner](../../eng/release/qualification.py)
- [Publication guards](../../eng/release/publication.py)
- [Approved release signers](../../.github/allowed_signers)
- [NuGet verification](../../eng/release/nuget.py)
- [Hosted continuous integration](../../.github/workflows/ci.yml)
- [Hosted release candidate](../../.github/workflows/release-candidate.yml)
- [Release contract](../../docs/release-process.md)
- [Publication runbook](../../docs/operations/release-publication.md)
- [Repository settings](../../docs/runbooks/repository-settings.md)
- [Consumer verification](../../docs/security/release-verification.md)

### Sources

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
