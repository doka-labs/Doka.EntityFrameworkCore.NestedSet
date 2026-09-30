# Release publication

This is the maintainer runbook for one RC or stable release. It performs no
work merely by being followed; every external action requires the project
owner's authorization for that exact release.

## Preconditions

- Source is reviewed, clean, and current on protected `main`.
- `CHANGELOG.md` contains one dated section for the exact version.
- Both projects use the same reviewed version.
- Stable public API additions have been moved from unshipped to shipped files.
- Required hosted settings in
  [Repository security settings](../runbooks/repository-settings.md) have been
  read back.
- Git SSH signing is configured for an authorized release principal.
- No tag, NuGet package, or GitHub release uses the version.

Set the candidate version locally without a leading `v`:

```sh
version="<X.Y.Z-rc.N-or-X.Y.Z>"
```

## 1. Local preflight

Confirm the exact checkout and version without creating a tag or remote state:

```sh
git status --short
git rev-parse HEAD
git rev-parse origin/main
python3 -m eng.release.publication pre-tag --version "$version"
```

The tree must be clean and `HEAD` must be exact current `origin/main`. Resolve
any mismatch; do not override the preflight. The hosted candidate run performs
the full qualification; a local run is not a release prerequisite.

## 2. Dispatch the release candidate

In GitHub Actions, dispatch `Release candidate` from `main` with the exact
version. Record the run ID and source SHA.

Wait for:

1. preflight;
2. the complete qualification run and sealed candidate;
3. build-provenance and SBOM attestations; and
4. the publish job waiting at the protected `nuget` environment.

Inspect the exact run/attempt artifacts. Do not approve while a reversible job
is missing, skipped, canceled, or failed.

## 3. Create the signed annotated tag

Only after reversible work passes, create one signed annotated tag on the exact
qualified SHA:

```sh
qualified_sha="<sha-from-candidate-run>"
test "$(git rev-parse HEAD)" = "$qualified_sha"

git tag -s -a "v$version" "$qualified_sha" -m "Release $version"
git verify-tag "v$version"
git push origin "refs/tags/v$version"
```

Push that one tag. Do not use `git push --tags`. Do not recreate, force, or move
the tag after push.

## 4. Approve publication

Return to the same run and same waiting publish job. Verify:

- version, run ID, attempt, source SHA, and tag all match;
- tag signature principal/key is the reviewed signer;
- candidate and provenance artifact IDs are from this run;
- both package IDs remain absent or match same-run recovery state; and
- no repository/environment/trusted-publisher setting changed during review.

Approve the `nuget` environment for this job. The workflow then stages the
complete draft release and obtains the short-lived NuGet credential. It
publishes both primary packages in dependency order (Core, EF), then both
symbol packages (Core, EF), verifies public packages/PDBs, and publishes the
immutable GitHub release.

## 5. Verify completion

Require the publish job to retain:

- package availability preflight;
- draft release receipt;
- NuGet public readback and signature verification;
- published immutable release receipt; and
- final completion receipt.

Run the independent public procedure in
[Release verification](../security/release-verification.md) from a clean
directory. Confirm NuGet pages, GitHub release classification, changelog notes,
tag, package dependencies, XML docs, symbols, SBOMs, and provenance.

Only then update documentation that says a version is publicly available.

## Recovery table

| Failure | Action |
| --- | --- |
| Preflight or qualification fails | Keep tag absent; fix source in a new reviewed commit and dispatch again |
| Attestation fails | Keep tag absent; retain evidence and diagnose identity or platform failure |
| Tag verification fails | Do not approve; remove only an unpushed local tag and correct signing setup |
| Draft asset upload interrupted | Rerun the same failed publish job; matching assets are retained |
| Core visible, EF missing | Rerun the same failed publish job; the matching Core payload is accepted, the EF push resumes, and public readback checks both packages |
| Both primary packages visible, symbols incomplete | Rerun the same failed publish job; duplicate-tolerant pushes resume symbols and public PDB readback remains required |
| Public package content conflicts | Stop permanently for that version and investigate as a supply-chain incident |
| Package or symbol indexing exceeds the 120-minute readback window | Check NuGet service status and the partial readback receipt. If the service is healthy, contact NuGet support as its [publication guidance](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package) advises after one hour. Rerun the same failed publish job; do not declare completion |
| GitHub release finalization times out | Rerun same job; it verifies existing state before continuation |

Do not start another candidate run for a partially published version. Do not
manually overwrite release assets or use a long-lived NuGet key as a shortcut.

## RC to stable

An RC and stable version are separate releases with separate review,
qualification, tag, approval, and readback. Stable preparation must incorporate
accepted RC changes, update the dated changelog entry, and finalize public API
baselines before its own qualification. Never relabel RC bytes as stable.
