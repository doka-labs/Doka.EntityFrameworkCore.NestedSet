# OpenSSF Best Practices evidence

This page maps repository-owned evidence to OpenSSF Best Practices criteria. It
does not claim an official project badge or Silver/Gold achievement. The
repository does not yet have a verified public OpenSSF project entry.

## Repository evidence

| Area | Evidence | Disposition |
| --- | --- | --- |
| Contribution requirements | [CONTRIBUTING.md](../CONTRIBUTING.md) | Documented |
| Governance and roles | [GOVERNANCE.md](../GOVERNANCE.md) | Documented; single-maintainer continuity limitation remains |
| Code of conduct | [CODE_OF_CONDUCT.md](../CODE_OF_CONDUCT.md) | Documented |
| Roadmap and non-goals | [ROADMAP.md](../ROADMAP.md) | Documented |
| Architecture | [Implementation design](implementation-design.md) | Documented |
| Security requirements | [SECURITY.md](../SECURITY.md) and [security design](security/security-design.md) | Documented |
| Assurance argument | [Security assurance case](security/assurance-case.md) | Documented; no external review claimed |
| Vulnerability reporting | [SECURITY.md](../SECURITY.md) | Private channel documented |
| Coding standards | `.editorconfig`, analyzers, [CONTRIBUTING.md](../CONTRIBUTING.md) | Enforced in build/review |
| Dependency control | Central versions, project lockfiles, Dependabot, dependency review | Repository evidence prepared |
| Tests | [Support and qualification](support-and-qualification.md) | Provider and migration matrix documented |
| Release verification | [Release verification](security/release-verification.md) | Procedure prepared; requires a real release |
| SBOM and provenance | [Release process](release-process.md) | Pipeline prepared; requires hosted evidence |

## Claims that require external evidence

The following cannot be established by adding repository text:

- organization two-factor-authentication policy;
- branch/ruleset and environment protection;
- CodeQL default setup, secret scanning, and private vulnerability reporting;
- NuGet trusted publisher identity and package ownership;
- immutable GitHub release configuration;
- a tested maintainer succession arrangement;
- independent review percentages, contributor counts, and bus factor;
- an independent human security review; and
- reproducible bit-for-bit builds on an independent environment.

Their setup and readback procedure is in
[Repository security settings](runbooks/repository-settings.md). Unknown or
unverified state must remain unclaimed.

## Silver and Gold preparation

| Criterion family | Current position |
| --- | --- |
| Signed releases | Verification and pipeline contract exist; first real release must supply evidence |
| Threat model and assurance case | Repository documents exist and need review with material security changes |
| Independent review | Not claimed; repository history must demonstrate it |
| Access continuity/bus factor | Not met by documentation alone |
| Coverage thresholds | Coverage artifacts are validated; no unsupported Silver/Gold percentage is claimed |
| Per-file license/copyright headers | Root MIT license and package metadata exist; no partial header claim |
| Reproducible build | Deterministic settings alone are insufficient; not claimed |
| Dynamic analysis | Provider runtime tests exist; final criterion mapping requires exact official definition review |

## Update procedure

1. Read the current official criteria.
2. Verify repository and external evidence independently.
3. Update this mapping without turning planned controls into completed claims.
4. Update an official project entry only after the repository exists publicly
   and the relevant evidence is accessible.
5. Read back the public entry and badge after saving changes.

## Primary sources

- OpenSSF Best Practices,
  [Passing, Silver, and Gold criteria](https://www.bestpractices.dev/en/criteria/0).
- OpenSSF Scorecard,
  [project documentation](https://github.com/ossf/scorecard).

External criteria are version-sensitive. Record a retrieval date when this
page is used to make an official claim.
