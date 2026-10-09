# Release publication operations

Use this runbook for every RC and stable release. The operator sequence follows
Doka and SafeMigrations: prepare current `main`, run `./eng/pre-tag-check.sh`,
start one untagged candidate, wait for qualification and attestations, create
the signed tag, and approve the same waiting publication job. Run each command
separately and stop on the first failure.

The package set is `Doka.NestedSet` and `Doka.EntityFrameworkCore.NestedSet` at
one version: two primary packages and two symbol packages. Evidence requirements
are defined in [Release governance](../release-governance.md) and
[Release process](../release-process.md).

## Release identity

Version `10.0.0` is the first stable release. The source uses
`VersionPrefix=10.0.0` without a prerelease suffix. Both
`PublicAPI.Shipped.txt` files contain the reviewed initial API; their unshipped
files retain only the nullable directive. The first RC, `10.0.0-rc.1`, is
already published on GitHub and NuGet. Its immutable release remains evidence
for that version, not for the changed stable source or packages.

Use the reviewed PR and publication sequence below for each release.

Before starting the candidate:

1. Confirm the intended publication date in `CHANGELOG.md` and retain the exact
   `## 10.0.0 (YYYY-MM-DD)` heading expected by the existing release tooling.
   The release date is `2026-10-09`; update it through review if publication
   takes place on a later day.
2. Review the stable API, package READMEs, provider qualification, and
   [Passing evidence](../openssf-best-practices.md) against the candidate source.
3. Retain maintainer knowledge/report-history confirmations separately from
   source evidence. The achieved Passing badge does not replace stable qualification.
4. Inspect the matching PR CI provider-lane durations and occupied-heap output
   for the capacity cases before starting RC. Local import observations approach
   the 512 MiB ceiling and do not establish hosted GC headroom. RC qualification
   includes coverage and all five engines under one shared two-hour deadline;
   successful parallel PR lanes do not by themselves prove that deadline. Retain
   the actual RC result as qualification evidence without relaxing these
   correctness or memory checks.

Prepare the versioned root/package READMEs, Getting Started, Support, and
Security policy in the release PR before starting the candidate. They describe
the candidate source's stable contract and installation commands; they must
not claim that public readback has already succeeded. Do not require a second
source change after publication just to document the version already qualified.

After successful public readback, confirm public package availability and the
immutable GitHub release. Retain dated qualification evidence; the presence of
a tag does not replace it. Add the release/tag/package URLs to the OpenSSF
entry and verify the saved badge state.

The stable candidate must be built and qualified on then-current protected
`main`. Do not reuse the RC candidate, tag, or package bytes as a stable
publication. SQL Server release evidence must come from a supported Linux
x86-64 host; successful local emulation on Arm does not establish that boundary.

## One-time configuration

Recheck the controls in
[Repository security settings](../runbooks/repository-settings.md) before
starting the release:

- protect `main`, require reviewed pull requests, and require the repository's
  CI and code scanning checks;
- protect `v*` release tags against update and deletion;
- enable immutable GitHub Releases;
- create environment `nuget`, restrict deployments to protected `main`, require
  maintainer approval, and store the NuGet profile name as `NUGET_USER`;
- configure the NuGet Trusted Publishing policy below; and
- configure SSH tag signing and review the public keys in
  [`.github/allowed_signers`](../../.github/allowed_signers).

| Trusted Publishing field | Value |
| --- | --- |
| Repository owner | `doka-labs` |
| Repository | `Doka.EntityFrameworkCore.NestedSet` |
| Workflow file | `release-candidate.yml` |
| Environment | `nuget` |

The selected NuGet owner must be allowed to publish both package IDs. Do not
store a long-lived NuGet API key. The protected publication job uses
`NuGet/login` only after candidate, tag, provenance, package, and draft-release
checks pass.

Read back the repository's immutable-release setting with an administrator
token:

```bash
repo=doka-labs/Doka.EntityFrameworkCore.NestedSet
gh api \
  -H "Accept: application/vnd.github+json" \
  -H "X-GitHub-Api-Version: 2026-03-10" \
  "repos/${repo}/immutable-releases"
```

The response must report `"enabled": true`. Also verify the environment,
Trusted Publishing policy, package ownership, and signer configuration. Local
build success does not establish hosted permissions or settings.

## Publication procedure

### 1. Prepare reviewed main

Merge the complete release preparation through protected `main`. It must
contain the intended `VersionPrefix`, one dated changelog section for the exact
version, current package metadata, dependencies, support documentation, and
public API baselines. Prerelease preparation keeps new declarations in
`PublicAPI.Unshipped.txt`. Stable preparation moves accepted declarations to
`PublicAPI.Shipped.txt` before qualification.

Update the checkout:

```bash
git fetch origin main --tags
git switch main
git merge --ff-only origin/main
git status --porcelain --untracked-files=all

release_commit="$(git rev-parse HEAD)"
test "${release_commit}" = "$(git rev-parse origin/main)"
```

The status command must print nothing. Keep this terminal and checkout
unchanged until the tag is pushed. Record `release_commit`; every later
identity check refers to this exact commit.

### 2. Verify local pre-tag readiness

```bash
./eng/pre-tag-check.sh
```

This checks clean current `main` against the remote, rejects a semantic release
tag already identifying the candidate commit, and checks SSH signing
configuration. File-based signing keys must exist. It reads remote refs without
fetching or changing the checkout, creates no tag, and requests no credentials
or hosted runner.

Success prints `Ready to start Release candidate for <commit>.` A failure
identifies the condition to correct and returns a nonzero exit code. Stop on
failure. This readiness check does not replace version, changelog, package
availability, or signing-authority checks in the hosted workflow.

### 3. Start the untagged candidate and wait

In GitHub Actions, start **Release candidate**. Select branch `main` and enter:

```text
version: 10.0.0
```

Use `10.0.0` for this stable release. Other release versions use
`X.Y.Z-rc.N` or `X.Y.Z`, without a leading `v`. Wait for:

1. `Verify source and unused version`;
2. independent `C# style and unused imports`, `Release engineering regression
   tests`, and `Build exact candidate and inspect packages` jobs;
3. all nine `RC / ...` test cells, three `RC / SQLite sample / ...` cells,
   `Verify exact package consumers`, and `Generate and verify both SBOMs`;
4. `Qualify exact release candidate`; and
5. `Sign and verify candidate provenance`.

Qualification runs the complete source, test, package, consumer, and SBOM
checks from this selected commit. The build compiles once, packs each shipping
package once, and inspects all primary/symbol archives. Subsequent jobs execute
its compiled test/sample files or verify its exact packages. The final
`qualify` job requires every prerequisite and complete matching evidence before
sealing; its candidate artifact ID then flows to attestation and publication.
See [Qualification stages](../release-process.md#qualification-stages) for the
fixed graph and artifact contract. Benchmarks and ADR profile validation are
separate maintainer activities. No earlier CI artifact or local rehearsal is
required for the release candidate.

Preflight establishes one 7,200-second UTC deadline shared by all qualification
jobs. The budget does not restart for each cell. Follow each job's live steps,
retained logs, results, and artifact IDs; failed cells retain diagnosis evidence.
The final job checks GitHub's direct dependency results, downloads non-matrix
evidence by producer artifact ID, and selects test/sample evidence only for the
current run and attempt. It verifies all required retained results before sealing.
There is no separate job-receipt registry or artifact-index API to configure.
The final summary identifies the candidate and retained qualification evidence.

The final `Publish approved candidate` job must show **Waiting** for approval
on environment `nuget`. Do not approve it yet. Confirm that the run SHA equals
`release_commit`, and record the run URL, run ID, and attempt. Inspect the
qualification summary, exact candidate, and provenance artifacts. Missing,
skipped, canceled, or failed qualification is not approval-ready evidence.

### 4. Create the signed immutable identity

Only after qualification and attestations succeed and the protected wait is
visible, run:

```bash
release_version="<release_version>"
release_tag="v${release_version}"

test "$(git rev-parse HEAD)" = "${release_commit}"
test "$(git rev-parse origin/main)" = "${release_commit}"
git tag -s "${release_tag}" "${release_commit}" \
  -m "Doka.EntityFrameworkCore.NestedSet ${release_version}"
git tag -v "${release_tag}"
test "$(git rev-list -n 1 "${release_tag}")" = "${release_commit}"
git push origin "refs/tags/${release_tag}"
```

Push only that tag. Never use `git push --tags`, move a pushed release tag, or
reuse a published version. The tag push does not start a second release
workflow. A later merge on `main` does not select new release bytes: publication
still verifies the recorded candidate commit's reachability from protected
`main`, its exact tag, and its original candidate artifacts.

### 5. Approve the same waiting run

Return to the exact run checked in step 3. Verify the source SHA, version, tag,
run, candidate artifact IDs, and approved signer. Confirm that the hosted
repository, environment, and Trusted Publishing settings have not changed
during review. Approve `Publish approved candidate` for environment `nuget`.

The same workflow run then:

1. verifies the downloaded candidate and provenance, qualified source, signed
   annotated tag, approved signer, and hosted run identity;
2. rejects conflicting public package contents;
3. creates or resumes a matching draft GitHub release and verifies its exact
   candidate and provenance assets;
4. obtains a short-lived NuGet credential;
5. publishes both primary packages in dependency order (Core, EF), then both
   symbol packages (Core, EF), with duplicate tolerance;
6. reads both primary packages and their Portable PDBs back, verifies NuGet
   repository signatures, and compares signed package contents with the
   qualified candidate;
7. publishes and verifies the immutable GitHub release; and
8. retains the complete publication receipt.

No package is rebuilt after qualification. Public package and symbol readback
has a 120-minute budget inside the 180-minute publication job. An accepted push
and public indexing are separate states; only the completed readback establishes
availability and matching content.

### 6. Confirm completion

Require the complete workflow run to be green. Inspect the published release:

```bash
gh release view "${release_tag}" \
  --json tagName,isDraft,isImmutable,isPrerelease,assets,url
```

The release must be published and immutable, identify the exact tag, have the
correct prerelease state, and contain exactly thirteen assets:

- two `.nupkg` files;
- two `.snupkg` files;
- `Doka.NestedSet.spdx.json` and `Doka.EntityFrameworkCore.NestedSet.spdx.json`;
- `candidate.json`, `package-manifest.json`, `qualification-evidence.zip`, and
  `release-notes.md`; and
- `release-provenance.jsonl`, `sbom-Doka.NestedSet.jsonl`, and
  `sbom-Doka.EntityFrameworkCore.NestedSet.jsonl`.

Confirm both NuGet package pages, signature verification, and symbol readback.
Inspect `publication-<run-id>-<attempt>` for package availability, draft,
NuGet readback, immutable-release, and completion receipts. RC releases must
be prereleases and must not be latest; stable releases must be latest.

Run [Release verification](../security/release-verification.md) independently
from a clean directory. Only then update documentation that declares a version
publicly available.

## Failure and recovery

- Before the candidate is sealed, use **Re-run all jobs** or a new dispatch.
  This refreshes every check and the preflight deadline. A partial rerun can
  retain successful source-quality/engineering logs from the same run and
  commit, but cannot reuse an earlier build for new test/sample jobs. Retaining
  preflight also retains its deadline. See the exact
  [rerun boundaries](../release-process.md#recovery). Correct source failures
  through review and start a new candidate run.
- Once a candidate is sealed, its original artifact IDs and bytes remain the
  inputs for existing attestation/publication recovery. Do not requalify
  selected parts or replace the candidate to repair a later failure.
- After the tag exists, never move or delete it to hide a failure. Rerun only
  the failed publication job in the same workflow run.
- A failed draft staging step requests no NuGet credential and pushes no
  package. The same job may resume only matching draft assets.
- After a partial NuGet publication, repeat the same publication job. Matching
  packages and duplicate-tolerant symbol pushes remain subject to complete
  public content, signature, and PDB readback. Conflicting content fails closed.
- If package or symbol indexing exceeds the 120-minute budget, inspect NuGet
  service status and the retained partial receipt. If the service is healthy,
  follow NuGet's publication guidance and contact support. The release remains
  incomplete until the same job's readback succeeds.
- GitHub release or attestation readback may lag publication. Retry the same
  failed job; do not replace assets or skip verification.
- If original qualification artifacts have expired or the run cannot be
  retried, stop for maintainer recovery. Do not rebuild under the same tag.

Stable releases use the same six steps. Each stable version is independently
qualified; RC archives are never renamed into stable packages.

## Primary sources

- [Signed annotated Git tags](https://git-scm.com/docs/git-tag), retrieved 2026-09-30.
- [GitHub Release inspection](https://cli.github.com/manual/gh_release_view), retrieved 2026-09-30.
- [NuGet publication and indexing](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package), retrieved 2026-09-30.
