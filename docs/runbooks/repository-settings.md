# Repository security settings

Repository files cannot create or prove organization rulesets, environment
reviewers, trusted publishing, immutable releases, secret scanning, or CodeQL
default setup. An administrator must configure and periodically read back these
controls for `doka-labs/Doka.EntityFrameworkCore.NestedSet`.
The settings below are the required target state, not a claim that GitHub has
already been configured or checked.

## Public repository and default branch

- The repository is public and active.
- `main` is the default branch.
- Force pushes and branch deletion are blocked.
- Pull requests are required for changes to `main`.
- Require linear history for `main`; allow only squash or rebase merges, not
  merge commits. See GitHub's
  [linear-history rule](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets#require-linear-history).
- Required checks apply to administrators as well as contributors.
- Require the PR branch to be current with `main` before the required checks
  permit a merge. This protects the tested PR result when `main` advances.
- Conversation resolution and current-head review are required.
- Stale approval behavior is chosen deliberately and documented with the
  organization's reviewer model.

Required status checks should use stable aggregate names rather than every
matrix cell:

- `Repository qualification` from `ci.yml`;
- `Dependency Review` from `dependency-review.yml`; and
- any organization-owned policy check explicitly required by Doka Labs.

Do not make scheduled-only or release-only jobs required for every pull
request. Re-read exact check names after the first hosted run before saving the
ruleset.

## Code scanning

Configure GitHub CodeQL **default setup** for C# and GitHub Actions after the
repository exists. This repository intentionally has no `codeql.yml`; Doka and
SafeMigrations use hosted default setup, and a second workflow would duplicate
analysis and ownership.

Enable:

- dependency graph;
- Dependabot alerts and security updates;
- secret scanning and push protection where the organization plan supports
  them; and
- private vulnerability reporting.

The Scorecard workflow is an additional public signal. It does not replace
CodeQL, dependency review, or human review.

## Actions policy

- Permit only actions allowed by the Doka Labs organization policy.
- Keep every third-party action reference pinned to a full commit SHA.
- Keep workflow permissions read-only by default.
- Grant `id-token`, `attestations`, `packages`, or `contents: write` only to the
  job that needs it.
- Do not allow pull-request jobs from untrusted forks to receive release
  credentials or write permissions.
- Retain qualification and publication evidence for the documented period.

The repository workflow linter and review must reject floating action tags.
Dependabot may propose action digest updates; each digest still requires review.

## NuGet environment

Create a protected GitHub environment named `nuget`:

- require the project owner or designated release maintainer to approve;
- prevent self-review when another authorized reviewer exists;
- restrict deployment branches/tags to the release policy;
- keep secrets empty when NuGet trusted publishing is used; and
- review environment changes with the same care as workflow changes.

The release workflow reaches this environment after candidate qualification
and package/SBOM attestations. The operator verifies and creates the signed tag
before approving the waiting publish job. GitHub starts that job only after
environment protection passes; its first steps then revalidate source and tag
and stage the draft release before obtaining NuGet credentials. Approval
authorizes that exact waiting job, not future runs. See GitHub's
[environment protection rules](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/control-deployments).

## NuGet trusted publishing

Configure one NuGet.org trusted publishing policy for:

- owner: `doka-labs`;
- repository: `Doka.EntityFrameworkCore.NestedSet`;
- workflow file: `release-candidate.yml` (NuGet.org asks for the filename only;
  the repository path is `.github/workflows/release-candidate.yml`);
- environment: `nuget`; and
- both package IDs: `Doka.NestedSet` and
  `Doka.EntityFrameworkCore.NestedSet`.

The workflow uses the pinned official `NuGet/login` action to exchange the
GitHub OIDC identity for a short-lived credential. No long-lived NuGet API key
belongs in repository or environment secrets.

Configure the repository secret `NUGET_USER` with the NuGet.org profile name of
the account that owns the trusted-publishing policy. It is a username, not an
email address or API key. The workflow passes it to `NuGet/login` as `user`;
without it the token exchange cannot identify the account. Keep the protected
`nuget` environment free of publishing keys. See NuGet's
[trusted-publishing setup](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing#github-actions-setup).

Read the policy back before the first release and after an organization,
repository, workflow filename, environment, or package ownership change.

## Release settings

- Enable immutable releases when available for the repository.
- Restrict tag creation/deletion through an organization ruleset or release
  process that permits only authorized signed annotated tags.
- Do not allow a workflow to move or replace an existing release tag.
- Keep release assets immutable after publication.
- Preserve GitHub artifact attestations and public release assets required by
  [Release verification](../security/release-verification.md).

The workflow verifies the tag and immutable published release state; hosted
settings remain an independent administrative control.

## SSH release signer

Set the protected environment variable `RELEASE_ALLOWED_SIGNERS` to the
reviewed Git allowed-signers content used for release tag verification. It must
name the authorized principal and public SSH signing key. Do not put a private
key in a variable or repository file.

The operator creates and pushes the signed annotated tag only after reversible
candidate jobs pass. See [Release publication](../operations/release-publication.md).

## Read-only confirmation

Use GitHub UI or authenticated read-only API calls to confirm:

- repository visibility, archived state, and default branch;
- `main` protection/ruleset status and required check names;
- enabled security features;
- workflow permissions and allowed-action policy;
- `nuget` environment protection;
- immutable release policy; and
- latest successful CI, dependency review, Scorecard, and release runs.

NuGet.org trusted publisher and package-owner settings must be confirmed in
NuGet.org. A workflow file cannot prove that remote policy exists.

Record the review date, reviewer, and observed settings in the organization's
operator system. Do not copy access tokens or full secret values into evidence.

## First-publication checklist

- [ ] Repository and `main` rules are active.
- [ ] `main` requires linear history and only squash/rebase PR merges are enabled.
- [ ] Stable required check names have completed once and are required.
- [ ] CodeQL default setup and repository security features are enabled.
- [ ] `nuget` environment approval is configured.
- [ ] NuGet trusted publishing matches repository, workflow, environment, and
      both package IDs.
- [ ] Release signer principal and public key are reviewed.
- [ ] Immutable release and tag controls are active.
- [ ] A release candidate was qualified on exact current `main`.
- [ ] Publication and public readback were exercised without bypassing a gate.

Do not infer any unchecked item from repository documentation. A missing hosted
control blocks publication until it is configured and read back.
