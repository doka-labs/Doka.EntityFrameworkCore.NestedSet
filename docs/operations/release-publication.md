# Release publication operations

Use this runbook for every RC and stable release. The operator sequence follows
Doka and SafeMigrations: prepare the current authorized branch, run
`./eng/pre-tag-check.sh`,
start one untagged candidate, wait for qualification and attestations, create
the signed tag, and approve the same waiting publication job. Run each command
separately and stop on the first failure.

The package set is `Doka.NestedSet`, `Doka.EntityFrameworkCore.NestedSet`, and
its `.MySql`, `.Postgres`, `.Sqlite`, and `.SqlServer` adapters at one version:
six primary packages and six symbol packages. Evidence requirements
are defined in [Release governance](../release-governance.md) and
[Release process](../release-process.md). The
[release-line runbook](release-lines.md) governs preparation, activation and
ongoing servicing. Main/10 is currently active; maintenance/10.x and optional
preview/11.x are registered inactive. This procedure activates none of them.

## Release identity

The source uses `VersionPrefix=10.1.0` without a prerelease suffix and the
`## 10.1.0 (2026-10-29)` changelog entry. The reviewed stable APIs are recorded
in all six shipped baselines; their unshipped files contain no pending changes.
SDK package validation still compares the two original packages with `10.0.0`
and retains only the approved options-registration removal exception. The
breaking transition is documented in the
[upgrade guide](../upgrading-provider-packages.md).
Prior release artifacts remain evidence for their exact version, not for the
changed source or packages.

Use the reviewed PR and publication sequence below for each release.

Before starting the candidate:

1. Confirm the reviewed `## 10.1.0 (2026-10-29)` entry in `CHANGELOG.md` and
   retain that exact version/date heading for qualification. If the publication
   date changes before qualification, update the changelog and this release
   identity together. Do not change the qualified source afterward.
2. Review the stable API, package READMEs, provider qualification, and
   [Passing evidence](../openssf-best-practices.md) against the candidate source.
3. Retain maintainer knowledge/report-history confirmations separately from
   source evidence. The achieved Passing badge does not replace stable qualification.
4. Inspect the matching PR CI provider-lane durations and occupied-heap output
   for the capacity cases before starting RC. Local import observations approach
   the 512 MiB ceiling and do not establish hosted GC headroom. RC qualification
   runs the same cells with coverage and all five engines; successful PR cells
   do not replace the RC's own run of them. Retain the actual RC result as
   qualification evidence without relaxing these correctness or memory checks.

Prepare the versioned root/package READMEs, Getting Started, Support, and
Security policy in the release PR before starting the candidate. They describe
the candidate source's stable contract and installation commands; they must
not claim that public readback has already succeeded. Do not require a second
source change after publication just to document the version already qualified.

After successful public readback, confirm public package availability and the
immutable GitHub release. Retain dated qualification evidence; the presence of
a tag does not replace it. Add the release/tag/package URLs to the OpenSSF
entry and verify the saved badge state.

The stable candidate must be built and qualified on its then-current protected
authorized line using that branch's SDK, framework and dependency graph. Do not reuse the RC candidate, tag, or package bytes as a stable
publication. SQL Server release evidence must come from a supported Linux
x86-64 host; successful local emulation on Arm does not establish that boundary.

## One-time configuration

Recheck the controls in
[Repository security settings](../runbooks/repository-settings.md) before
starting the release:

- protect `main` and every prepared/active source line, require reviewed pull
  requests and the applicable CI and code scanning checks;
- protect `v*` release tags against update and deletion;
- enable immutable GitHub Releases;
- retain environment `nuget`, restrict deployments to exact approved active
  branch names, retain the existing reviewer/administrator arrangement and
  store the NuGet profile name as `NUGET_USER`;
- configure the NuGet Trusted Publishing policy below; and
- configure SSH tag signing and review the public keys in
  [`.github/allowed_signers`](../../.github/allowed_signers).

| Trusted Publishing field | Value |
| --- | --- |
| Repository owner | `doka-labs` |
| Repository | `Doka.EntityFrameworkCore.NestedSet` |
| Workflow file | `release-candidate.yml` |
| Environment | `nuget` |

The selected NuGet owner must be allowed to publish all six package IDs. Do not
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
build success does not establish hosted permissions or settings. Readiness
requires authenticated GitHub CLI access to the selected protected branch and
current protected-main policy; this introduces no new publishing credential.

## Publication procedure

### 1. Prepare the reviewed source line

Merge the complete release preparation through the protected authorized branch. It must
contain the intended `VersionPrefix`, one dated changelog section for the exact
version, current package metadata, dependencies, support documentation, and
public API baselines. Prerelease preparation keeps new declarations in
`PublicAPI.Unshipped.txt`. Stable preparation moves accepted declarations to
`PublicAPI.Shipped.txt` before qualification.

Update the checkout:

```bash
release_branch=main
git fetch origin "${release_branch}" --tags
git switch "${release_branch}"
git merge --ff-only "origin/${release_branch}"
git status --porcelain --untracked-files=all

release_commit="$(git rev-parse HEAD)"
test "${release_commit}" = "$(git rev-parse "origin/${release_branch}")"
```

For a later authorized maintenance release, set `release_branch` to its exact
active branch instead. The status command must print nothing. Keep this terminal and checkout
unchanged until the tag is pushed. Record `release_commit`; every later
identity check refers to this exact commit.

### 2. Verify local pre-tag readiness

```bash
./eng/pre-tag-check.sh
```

This checks the clean current authorized branch against the remote and current
protected-main policy, rejects a semantic release
tag already identifying the candidate commit, and checks SSH signing
configuration. File-based signing keys must exist. It reads remote refs without
fetching or changing the checkout, creates no tag, and requests no credentials
or hosted runner.

Success prints `Ready to start Release candidate for <commit>.` A failure
identifies the condition to correct and returns a nonzero exit code. Stop on
failure. This readiness check does not replace version, changelog, package
availability, or signing-authority checks in the hosted workflow.

### 3. Start the untagged candidate and wait

In GitHub Actions, start **Release candidate**. Select the same authorized
branch as `release_branch` and enter:

```text
version: 10.1.0
qualification-only: false
```

Use `10.1.0` for this stable release. Other release versions use
`X.Y.Z-rc.N` or `X.Y.Z`, without a leading `v`. Wait for:

1. `Verify source and unused version`;
2. the shared `Qualification / ...` jobs that pull-request CI also runs: C#
   style, engineering regressions, the release build with package inspection,
   package consumers, SBOMs, thirteen test cells, three SQLite samples, and
   `Qualification / Test evidence and coverage`;
3. `Qualify exact release candidate`; and
4. `Sign and verify candidate provenance`.

Qualification runs the complete source, test, package, consumer, and SBOM
checks from this selected commit through the shared
[quality workflow](../../.github/workflows/quality-gates.yml). The build
compiles once with the exact version, packs each shipping package once, and
inspects all primary/symbol archives. Consumers and SBOMs verify those exact
packages; test cells and samples build their own projects from the same commit.
The final `qualify` job requires every prerequisite and complete matching
evidence before sealing; its candidate artifact ID then flows to attestation
and publication. See
[Qualification stages](../release-process.md#qualification-stages) for the
fixed graph and artifact contract. Benchmarks and ADR profile validation are
separate maintainer activities. No earlier CI artifact is required for the
release candidate.

Each job has its own timeout; there is no shared qualification deadline. The
release candidate takes about as long as pull-request CI. Follow each job's live
steps, retained logs, results, and artifact IDs; failed cells retain diagnosis
evidence. The final job checks GitHub's direct dependency results, downloads
the package, consumer, and SBOM evidence by artifact ID, and selects test
evidence only for the current run and attempt. It verifies all required
retained results before sealing. There is no separate job-receipt registry or
artifact-index API to configure. The final summary identifies the candidate and
retained qualification evidence.

For a prepared line or a nonpublishing rehearsal, set `qualification-only=true`.
The same applicable qualification and attestations run, but the entire
environment-bearing publisher is skipped. Create no tag and request no NuGet
approval for that run.

For publication, the final `Publish approved candidate` job must show **Waiting** for approval
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
test "$(git rev-parse "origin/${release_branch}")" = "${release_commit}"
git tag -s "${release_tag}" "${release_commit}" \
  -m "Doka.EntityFrameworkCore.NestedSet ${release_version}"
git tag -v "${release_tag}"
test "$(git rev-list -n 1 "${release_tag}")" = "${release_commit}"
git push origin "refs/tags/${release_tag}"
```

Push only that tag. Never use `git push --tags`, move a pushed release tag, or
reuse a published version. The tag push does not start a second release
workflow. A later merge on the selected line does not select new release bytes:
publication still verifies the recorded candidate commit's reachability from
that protected line, its exact tag and original candidate artifacts. Current
protected-main policy must continue to authorize that line's major/channel.

### 5. Approve the same waiting run

Return to the exact run checked in step 3. Verify the source SHA, version, tag,
run, candidate artifact IDs, and approved signer. Confirm that the hosted
repository, environment, and Trusted Publishing settings have not changed
during review. Approve `Publish approved candidate` for environment `nuget`.

The same workflow run then:

1. independently rereads current protected-main authorization, verifies the
   selected protected source, downloaded candidate/provenance, signed annotated
   tag, approved signer and original hosted run identity;
2. rejects conflicting public package contents;
3. creates or resumes a matching draft GitHub release and verifies its exact
   candidate and provenance assets;
4. obtains a short-lived NuGet credential;
5. publishes all six primary packages in dependency order (pure core, common EF,
   MySql, Postgres, Sqlite, SqlServer), then all six symbol packages in that order;
6. reads all six primary packages and their Portable PDBs back, verifies NuGet
   repository signatures, and compares signed package contents with the
   qualified candidate;
7. publishes and verifies the immutable GitHub release, promoting GitHub
   Latest only when canonical version precedence allows it; and
8. retains the complete publication receipt.

All publisher versions/lines share one non-cancelling queue with `queue: max`.
Current authority is checked again before public writes and credential use; a
prepared, disabled, reassigned or unauthorized line cannot publish with an old
frozen grant. Complete affected publication before changing line policy.

Uploads read the original sealed files under `artifacts/candidate` after
authorization rechecks. Availability and public readback use their verified
copies under `artifacts/packages`; those copies do not replace the sealed
upload source.

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
correct prerelease state, and contain exactly twenty-nine assets:

- six `.nupkg` files and six `.snupkg` files;
- six `<package-id>.spdx.json` documents, one per primary package;
- `candidate.json`, `package-manifest.json`, `qualification-evidence.zip`, and
  `release-notes.md`; and
- `release-provenance.jsonl` plus six `sbom-<package-id>.jsonl` bundles.

The package IDs and dependency order are listed in
[Release process](../release-process.md#release-identity). Confirm all six NuGet
package pages, signature verification, and symbol readback.
Inspect `publication-<run-id>-<attempt>` for package availability, draft,
NuGet readback, immutable-release, and completion receipts. RC releases must
be prereleases and must not be Latest. Stable releases may be Latest only when
no higher stable version already owns it. A 10.x maintenance release or an
out-of-order lower patch stays stable without replacing a higher Latest.
Historical release verification does not require continued Latest status.

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
- Once a candidate is sealed, use **Re-run failed jobs** in its original run.
  Its original artifact IDs and bytes remain the inputs for existing
  attestation/publication recovery, with current authorization rechecked. Do not requalify
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
