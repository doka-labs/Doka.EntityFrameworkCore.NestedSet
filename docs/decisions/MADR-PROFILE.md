# Doka MADR Enterprise Profile 1.0

## Purpose and upstream basis

NestedSet uses the full MADR 4.0.0 structure and the Doka profile's explicit
metadata, trade-offs, confirmation, history, provenance, and reciprocal decision
relationships. The profile makes optional upstream sections mandatory. Its
identifiers, additional metadata, status rules, and generated indexes are Doka
extensions, not requirements attributed to MADR itself.

MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY express normative requirements here.
The [template](adr-template.md) provides the complete starting structure. The
[decision index](README.md) is generated from the records.

Records preserve their actual recording dates and initial proposal history.
The maintainer accepted the decisions then present on 2026-09-28; later records
preserve their own acceptance dates. Implemented records are confirmed against
linked repository evidence. The release decision is implemented, with the
immutable first RC and both public NuGet package identities confirmed; each
subsequent release follows the same qualification and authorized publication
process.
Implementation existence and passing checks MUST NOT be treated as historical
approval. Acceptance requires a recorded owner decision under
[project governance](../../GOVERNANCE.md).

`decision-makers` identifies the accountable decision authority; its presence
does not establish approval. `consulted` names actual consultation participants.
`informed` names the maintainer-designated audience kept up to date through
one-way communication. NestedSet uses `@doka-labs/core-maintainers`, following
the organization's SafeMigrations audience. An entry does not prove notification
delivery, consultation, or approval. Empty lists are valid when no consultation
participants or informed audience have been designated.

## Identity and source bytes

- Files MUST use `D-NNN-lowercase-version-safe-slug.md` directly in this directory.
  Slugs permit lowercase letters, digits, single dashes, and version dots.
- IDs MUST be unique and contiguous from D-001. Filename, metadata, and H1 MUST
  agree. Never renumber a merged record.
- Source MUST use ASCII, LF, and a final newline. Control characters other than
  tabs and line breaks are forbidden.
- The H1 MUST be `# D-NNN -- Short decision title`. Titles MUST NOT contain
  inline Markdown markup that would break the generated navigation table.
- Fixed headings MUST occur exactly once, at the levels below, in order, with
  a blank line before each heading. Fenced examples are not section headings.

## Flat metadata

Every ADR MUST begin with these thirteen keys in this exact order:

```yaml
---
id: D-NNN
status: proposed
date: YYYY-MM-DD
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Bounded decision scope"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---
```

The intentionally restricted grammar supports one scalar or one inline list per
key, without nested YAML, comments, aliases, tags, or undeclared keys. Scalars
are nonempty double-quoted JSON-compatible strings or plain text consisting of
letters, digits, spaces, periods, underscores, and dashes. Lists contain unique
scalar elements separated by commas. Quoted strings may contain punctuation.
`scope` and `decision-makers` MUST be nonempty. Relationship lists contain only
D-NNN IDs. Dates MUST be real calendar dates in the exact displayed format.

Front matter is the only current metadata store. The body MUST NOT repeat
labeled status, date, or scope, or preserve an original-record-metadata block.
Historical changes belong under Decision History.

## Status and history

| Status | Meaning |
| --- | --- |
| proposed | Open for review; not authoritative |
| accepted | Approved by the accountable owner; confirmation may still be pending |
| implemented | Accepted and confirmed against implementation evidence |
| rejected | Reviewed and declined before becoming authoritative |
| deprecated | Historically relevant but no longer recommended |
| superseded | Replaced by a named successor decision |

Legal transitions are `proposed -> accepted`, `proposed -> rejected`,
`accepted -> implemented`, and `accepted` or `implemented` to `deprecated` or
`superseded`. Metadata contains the final state of the recorded history.

Every history entry MUST be a dated bullet. The first MUST match the recording
date and use `Decision recorded with status proposed.`. Later status changes
MUST use exactly `Status changed from <old> to <new>.`; explanations belong in
separate dated entries. Dates MUST be nondecreasing. Ordinary dated entries may
record implementation, confirmation evidence, amendments, or reasons without
inventing a state transition. Later edits do not replace the recording date.

## Full section contract

```text
## Context and Problem Statement
## Decision Drivers
## Considered Options
## Decision Outcome
### Consequences
### Confirmation
## Pros and Cons of the Options
### Exact first considered option
### Exact second considered option
## More Information
### Re-evaluation Triggers
### Decision History
### Implementation References
### Sources
```

Every fixed section MUST have meaningful content. Pros and Cons of the Options
contains the option subsections; its introductory paragraph is optional. At
least two distinct, credible options MUST be listed as `- Exact option title`.
Each option MUST have exactly one same-named H3 in the same order. Additional,
duplicate, or incorrectly nested option headings are rejected.

Decision Outcome MUST state `Chosen option: "Exact option title", because ...`.
For a proposal, this is the author's proposed choice, not evidence of approval.
Consequences and every option MUST each include both `- Good, because ...` and
`- Bad, because ...`. Rejected alternatives deserve real benefits and costs.

Confirmation MUST name reproducible commands, tests, gates, or linked inspection
paths with expected outcomes. Relevant negative cases MUST be included. A
command written in an ADR is a procedure, not a claim it ran. Trigger-dependent
confirmation MUST identify the observable trigger and the gate required when
it fires. Re-evaluation triggers MUST identify an observable contract change.

## Links, relationships, and provenance

Implementation References MUST include concrete inline links to local evidence.
Local links MUST resolve inside this repository, including after symlink and
percent-escape resolution. Use relative paths without query strings. Inline
Markdown links are supported; reference-style links are intentionally rejected
so hidden link definitions cannot evade provenance checks. Link fragments are
navigation hints; this validator checks the path, not Markdown anchor spelling.

Relations MUST name existing IDs, never themselves. `supersedes` /
`superseded-by` and `amends` / `amended-by` MUST be reciprocal. A superseded
record requires a successor; a successor requires the old record to have
`superseded` status. Topical connections are ordinary local links, not invented
amendments. Record relationship changes and their reason in decision history.

External URLs MUST appear only under Sources. Every external entry MUST use:

```text
- [Primary source title](https://authoritative.example/path) (primary source; retrieved YYYY-MM-DD)
```

Use actual retrieval dates and publishers authoritative for the specific claim:
official specifications, vendor documentation, first-party source, or release
records. Prefer version-pinned sources for versioned contracts. External links
require HTTPS and MUST NOT contain credentials. Repository-only decisions MUST
use exactly `- No external sources; repository evidence only.` and MUST NOT mix
that marker with external entries.

The mechanical check verifies provenance syntax, dates, and placement. Human
review MUST still check source authority, claim support, credible alternatives,
and whether consultation or acceptance actually happened.

## Tooling and document validation

Run `eng/validate-adrs.sh` for read-only validation. Run
`eng/validate-adrs.sh --write-index` to update README.md and decision-index.json
only after the corpus validates. Both indexes MUST match deterministic metadata
rendering. The relationship graph is generated only when real amendment or
supersession edges exist. An empty corpus, missing index, stale index, malformed
record, or unresolved local link fails validation.

The implementation is [one standard-library Python module](../../eng/quality/adr.py)
behind a [stable shell entry point](../../eng/validate-adrs.sh). Maintainers run
this document check separately; CI and release qualification do not invoke it.
This does not add a Python invocation to consumer MSBuild or a shipping dependency.
Run fixture regressions with `python3 -m unittest discover -s eng/quality/tests -p test_adrs.py`.
Unknown command options and missing option values fail with usage exit code 2;
corpus failures return 1 with diagnostics. Checks do not execute ADR commands,
fetch external links, or establish semantic approval.

## Attribution

The template structure is adapted from MADR 4.0.0 using its CC0-1.0 alternative;
project-specific prose follows the [repository license](../../LICENSE).
This attribution does not imply upstream endorsement. Doka's stricter profile
is adapted for NestedSet's existing Python engineering tools.

Primary upstream sources, retrieved 2026-09-19:

- [MADR 4.0.0 full template](https://github.com/adr/madr/blob/4.0.0/template/adr-template.md)
- [MADR 4.0.0 license](https://github.com/adr/madr/blob/4.0.0/LICENSE)
- [MADR 4.0.0 changelog](https://github.com/adr/madr/blob/4.0.0/CHANGELOG.md)
