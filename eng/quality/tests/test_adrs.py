"""Regression tests for the full MADR document and generated-index contract."""

import contextlib
import importlib.util
import io
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPOSITORY = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("nestedset_adr", REPOSITORY / "eng/quality/adr.py")
ADR = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = ADR
SPEC.loader.exec_module(ADR)


def record(identifier="D-001"):
    """Create a complete, independent decision with a real local confirmation target."""
    return f'''---
id: {identifier}
status: proposed
date: 2026-09-19
decision-makers: [Accountable Owner]
consulted: []
informed: []
scope: "Fixture contract"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# {identifier} -- Preserve a verifiable contract

## Context and Problem Statement

The fixture needs one authoritative metadata source.

## Decision Drivers

- Prevent history and navigation drift.

## Considered Options

- Validate the corpus
- Review manually

## Decision Outcome

Chosen option: "Validate the corpus", because structured drift must fail a gate.

### Consequences

- Good, because malformed history becomes observable.
- Bad, because the validator requires maintenance.

### Confirmation

- Inspect [local evidence](../../evidence.txt) and expect a complete record.

## Pros and Cons of the Options

### Validate the corpus

- Good, because drift is detected consistently.
- Bad, because semantic review still needs a person.

### Review manually

- Good, because prose can evolve without parser changes.
- Bad, because subtle index drift can be missed.

## More Information

This is a proposed fixture, not evidence of approval.

### Re-evaluation Triggers

- A real consumer needs another source format.

### Decision History

- 2026-09-19: Decision recorded with status proposed.

### Implementation References

- [Local evidence](../../evidence.txt)

### Sources

- No external sources; repository evidence only.
'''


class AdrFixture(unittest.TestCase):
    """Exercise corrupted document and index boundaries in disposable repositories."""

    def setUp(self):
        """Give every test independent source bytes and output paths."""
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.directory = self.root / "docs/decisions"
        self.directory.mkdir(parents=True)
        (self.root / "evidence.txt").write_text("fixture evidence\n", encoding="ascii")
        (self.directory / "MADR-PROFILE.md").write_text("# Fixture profile\n", encoding="ascii")
        (self.directory / "adr-template.md").write_text("# Fixture template\n", encoding="ascii")
        self.path = self.directory / "D-001-fixture.md"
        self.path.write_text(record(), encoding="ascii")

    def change(self, old, new):
        """Change only the source fragment explicitly named by the scenario."""
        text = self.path.read_text(encoding="ascii")
        self.assertIn(old, text)
        self.path.write_text(text.replace(old, new), encoding="ascii")

    def validate(self, write_index=True):
        """Run the real validator against the fixture without executing any ADR command."""
        return ADR.validate_repository(self.root, write_index)

    def accepted_history(self):
        """Prepare a real proposal-to-acceptance chain for state-transition probes."""
        self.change("status: proposed", "status: accepted")
        self.change("- 2026-09-19: Decision recorded with status proposed.",
                    "- 2026-09-19: Decision recorded with status proposed.\n"
                    "- 2026-09-20: Status changed from proposed to accepted.")



class AdrIndexGenerationTests(AdrFixture):
    """Verify deterministic generation and normal read-only behavior."""

    def test_complete_corpus_generates_both_indexes(self):
        """Valid metadata produces one consistent machine and human navigation entry."""
        # Arrange
        expected_id = "D-001"

        # Act
        errors = self.validate()

        # Assert
        self.assertEqual([], errors)
        index = json.loads((self.directory / "decision-index.json").read_text())
        self.assertEqual(expected_id, index["decisions"][0]["id"])
        self.assertIn("[D-001](D-001-fixture.md)", (self.directory / "README.md").read_text())

    def test_regeneration_is_byte_identical(self):
        """Index regeneration does not introduce timestamps or unstable ordering."""
        # Arrange
        self.assertEqual([], self.validate())
        before = {name: (self.directory / name).read_bytes() for name in ("README.md", "decision-index.json")}

        # Act
        errors = self.validate()

        # Assert
        self.assertEqual([], errors)
        self.assertEqual(before, {name: (self.directory / name).read_bytes() for name in before})

    def test_normal_validation_is_read_only(self):
        """A valid generated corpus passes without rewriting source or index files."""
        # Arrange
        self.assertEqual([], self.validate())
        before = {path.name: (path.read_bytes(), path.stat().st_mtime_ns) for path in self.directory.iterdir()}

        # Act
        errors = self.validate(write_index=False)

        # Assert
        self.assertEqual([], errors)
        self.assertEqual(before, {path.name: (path.read_bytes(), path.stat().st_mtime_ns)
                                  for path in self.directory.iterdir()})

    def test_empty_corpus_is_rejected(self):
        """Removing every record cannot produce a successful empty index."""
        # Arrange
        self.path.unlink()

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("decision corpus is missing or empty", "\n".join(errors))



class AdrMetadataTests(AdrFixture):
    """Enforce the flat metadata and canonical source-byte contracts."""

    def test_unknown_metadata_is_rejected(self):
        """A misspelled or speculative field is not silently ignored."""
        # Arrange
        self.change("scope: \"Fixture contract\"", "scope: \"Fixture contract\"\nowner: Someone")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("unknown or duplicate metadata key: owner", "\n".join(errors))

    def test_duplicate_metadata_is_rejected(self):
        """A later field cannot override an earlier approved-looking status."""
        # Arrange
        self.change("status: proposed", "status: proposed\nstatus: accepted")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("unknown or duplicate metadata key: status", "\n".join(errors))

    def test_metadata_order_is_enforced(self):
        """The flat grammar has one canonical metadata key order."""
        # Arrange
        self.change("consulted: []\ninformed: []", "informed: []\nconsulted: []")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("profile order", "\n".join(errors))

    def test_nested_metadata_is_rejected(self):
        """Nested YAML cannot be interpreted differently by a downstream parser."""
        # Arrange
        self.change("consulted: []", "consulted:\n  - Someone")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("one flat key: value", "\n".join(errors))

    def test_duplicate_list_participants_are_rejected(self):
        """The same participant cannot be counted twice in one metadata role."""
        # Arrange
        self.change("consulted: []", "consulted: [Person, Person]")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("duplicate list element", "\n".join(errors))

    def test_quoted_comma_stays_one_participant(self):
        """Quoted commas are data rather than silently split identities."""
        # Arrange
        value = '["Owner, Team", "Another Person"]'

        # Act
        actual = ADR.metadata_value("consulted", value)

        # Assert
        self.assertEqual(["Owner, Team", "Another Person"], actual)

    def test_empty_list_element_is_rejected(self):
        """A missing inline-list value cannot disappear during parsing."""
        # Arrange
        self.change("consulted: []", "consulted: [Person,, Other]")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("malformed inline list", "\n".join(errors))

    def test_empty_owner_and_scope_are_rejected_together(self):
        """Validation accumulates independent required-metadata failures."""
        # Arrange
        self.change("decision-makers: [Accountable Owner]", "decision-makers: []")
        self.change('scope: "Fixture contract"', 'scope: ""')

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("invalid scope", "\n".join(errors))
        self.assertIn("must not be empty", "\n".join(errors))

    def test_external_url_cannot_hide_in_metadata(self):
        """A quoted scope string cannot bypass the dated source contract."""
        # Arrange
        self.change('scope: "Fixture contract"', 'scope: "https://example.org/spec"')

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("Sources, not metadata", "\n".join(errors))

    def test_invalid_calendar_date_is_rejected(self):
        """A date-shaped but impossible recording date fails the corpus."""
        # Arrange
        self.change("date: 2026-09-19", "date: 2026-02-30")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("invalid recording date", "\n".join(errors))

    def test_unsupported_profile_version_is_rejected(self):
        """An unimplemented schema version cannot pass with old validation rules."""
        # Arrange
        self.change('madr-version: "4.0.0"', 'madr-version: "5.0.0"')

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("madr-version must be 4.0.0", "\n".join(errors))

    def test_source_bytes_are_ascii_lf_with_final_newline(self):
        """CRLF and a missing final newline are observable byte-contract failures."""
        # Arrange
        self.path.write_bytes(record().replace("\n", "\r\n").encode("ascii").rstrip(b"\r\n"))

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("LF line endings and a final newline", "\n".join(errors))

    def test_non_ascii_source_is_rejected(self):
        """Invalid source encoding produces diagnostics rather than a decoder crash."""
        # Arrange
        self.path.write_bytes(record().encode("ascii") + b"\xc3\xa4\n")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("cannot read ASCII source", "\n".join(errors))

    def test_escaped_non_ascii_metadata_is_rejected(self):
        """Unicode escapes cannot bypass the ASCII output contract."""
        # Arrange
        self.change('scope: "Fixture contract"', 'scope: "\\u00e4"')

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("invalid scope", "\n".join(errors))



class AdrIdentityTests(AdrFixture):
    """Require one consistent, contiguous decision identity inventory."""

    def test_invalid_filename_is_rejected(self):
        """An unexpected filename cannot hide a source record from validation."""
        # Arrange
        self.path.rename(self.directory / "D-001-Uppercase.md")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("invalid decision filename", "\n".join(errors))

    def test_nested_decision_file_is_not_ignored(self):
        """A misplaced record remains in discovery and fails its location contract."""
        # Arrange
        nested = self.directory / "hidden"
        nested.mkdir()
        (nested / "D-002-hidden.md").write_text(record("D-002"), encoding="ascii")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("nested decision location", "\n".join(errors))

    def test_gapped_identifiers_are_rejected(self):
        """Identifiers must remain contiguous even when every record parses."""
        # Arrange
        (self.directory / "D-003-gap.md").write_text(record("D-003"), encoding="ascii")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("unique and contiguous", "\n".join(errors))

    def test_duplicate_identifiers_are_rejected(self):
        """Two filenames cannot both define the same decision identity."""
        # Arrange
        (self.directory / "D-001-duplicate.md").write_text(record(), encoding="ascii")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("unique and contiguous", "\n".join(errors))

    def test_title_identity_must_match_metadata(self):
        """An H1 referring to another decision cannot enter the generated index."""
        # Arrange
        self.change("# D-001 --", "# D-002 --")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("H1 must match", "\n".join(errors))



class AdrSectionTests(AdrFixture):
    """Preserve complete sections, symmetric alternatives, and confirmation."""

    def test_wrong_section_level_is_rejected(self):
        """Confirmation is nested below Decision Outcome, not a peer H2."""
        # Arrange
        self.change("### Confirmation", "## Confirmation")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("section hierarchy", "\n".join(errors))

    def test_heading_requires_blank_line(self):
        """Readable section boundaries are validated independently of a formatter."""
        # Arrange
        self.change("verifiable contract\n\n## Context", "verifiable contract\n## Context")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("preceding blank line", "\n".join(errors))

    def test_fenced_headings_do_not_change_section_structure(self):
        """Example headings remain content rather than phantom duplicate sections."""
        # Arrange
        self.change("This is a proposed fixture", "```text\n### Confirmation\n```\n\nThis is a proposed fixture")

        # Act
        errors = self.validate()

        # Assert
        self.assertEqual([], errors)

    def test_missing_option_alternative_is_rejected(self):
        """A single-option decision fails even if its remaining trade-offs are symmetric."""
        # Arrange
        self.change("- Review manually\n", "")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("at least two distinct considered options", "\n".join(errors))

    def test_empty_confirmation_is_rejected(self):
        """A heading without an acceptance procedure cannot count as confirmation."""
        # Arrange
        self.change("- Inspect [local evidence](../../evidence.txt) and expect a complete record.", "")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("empty or missing section: Confirmation", "\n".join(errors))

    def test_confirmation_requires_expected_outcome(self):
        """A command reference alone does not state what would prove the decision."""
        # Arrange
        self.change("and expect a complete record", "for background")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("concrete evidence and an expected result", "\n".join(errors))

    def test_unbalanced_option_cost_is_rejected(self):
        """The chosen option cannot be presented with benefits alone."""
        # Arrange
        self.change("- Bad, because semantic review still needs a person.", "")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("Validate the corpus requires a Bad, because", "\n".join(errors))

    def test_undocumented_option_heading_is_rejected(self):
        """Extra option subsections cannot bypass the considered-options inventory."""
        # Arrange
        self.change("## More Information", "### Hidden option\n\n- Good, because a benefit exists.\n"
                    "- Bad, because a cost exists.\n\n## More Information")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("exact option headings", "\n".join(errors))

    def test_duplicate_option_heading_is_rejected(self):
        """Repeated headings cannot overwrite a preceding option during parsing."""
        # Arrange
        self.change("### Review manually", "### Validate the corpus")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("exact option headings", "\n".join(errors))

    def test_chosen_option_must_be_considered(self):
        """The outcome cannot quietly choose an unreviewed alternative."""
        # Arrange
        self.change('Chosen option: "Validate the corpus"', 'Chosen option: "Skip the decision"')

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("outcome must choose", "\n".join(errors))

    def test_multiline_outcome_is_supported(self):
        """Prose wrapping does not change the identity of the selected option."""
        # Arrange
        self.change('Chosen option: "Validate the corpus"', 'Chosen option: "Validate the\ncorpus"')

        # Act
        errors = self.validate()

        # Assert
        self.assertEqual([], errors)

    def test_body_metadata_is_rejected(self):
        """A body-level status cannot conflict with the authoritative front matter."""
        # Arrange
        self.change("This is a proposed fixture", "Status: accepted\n\nThis is a proposed fixture")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("body must not duplicate", "\n".join(errors))



class AdrHistoryTests(AdrFixture):
    """Require explicit chronological transitions for every approval claim."""

    def test_missing_history_acceptance_cannot_approve_record(self):
        """Changing metadata alone cannot claim maintainer acceptance."""
        # Arrange
        self.change("status: proposed", "status: accepted")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("history final state", "\n".join(errors))

    def test_illegal_transition_is_rejected(self):
        """A proposal cannot jump directly to implemented by passing source checks."""
        # Arrange
        self.change("status: proposed", "status: implemented")
        self.change("- 2026-09-19: Decision recorded with status proposed.",
                    "- 2026-09-19: Decision recorded with status proposed.\n"
                    "- 2026-09-20: Status changed from proposed to implemented.")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("illegal history transition", "\n".join(errors))

    def test_history_dates_must_be_chronological(self):
        """A later-listed event cannot backdate an approval transition."""
        # Arrange
        self.accepted_history()
        self.change("2026-09-20: Status changed", "2026-09-18: Status changed")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("history dates must be nondecreasing", "\n".join(errors))

    def test_legal_acceptance_transition_passes(self):
        """An explicitly recorded chronological state transition agrees with metadata."""
        # Arrange
        self.accepted_history()

        # Act
        errors = self.validate()

        # Assert
        self.assertEqual([], errors)



class AdrRelationshipTests(AdrFixture):
    """Require reciprocal real relationships rather than invented graph edges."""

    def test_missing_relationship_target_is_rejected(self):
        """A relationship cannot refer to an unrecorded decision."""
        # Arrange
        self.change("amends: []", "amends: [D-002]")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("target is missing", "\n".join(errors))

    def test_self_relationship_is_rejected(self):
        """A decision cannot amend or supersede itself."""
        # Arrange
        self.change("amends: []", "amends: [D-001]")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("self-referential", "\n".join(errors))

    def test_asymmetric_relationship_is_rejected(self):
        """Both affected records must acknowledge an amendment."""
        # Arrange
        self.change("amends: []", "amends: [D-002]")
        (self.directory / "D-002-target.md").write_text(record("D-002"), encoding="ascii")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("lacks reciprocal amended-by", "\n".join(errors))

    def test_real_reciprocal_edges_generate_a_graph(self):
        """Only actual relationship metadata contributes to the Mermaid graph."""
        # Arrange
        self.change("amends: []", "amends: [D-002]")
        second = record("D-002").replace("amended-by: []", "amended-by: [D-001]")
        (self.directory / "D-002-target.md").write_text(second, encoding="ascii")

        # Act
        errors = self.validate()

        # Assert
        self.assertEqual([], errors)
        self.assertIn('D_001["D-001"] -->|amends| D_002["D-002"]', (self.directory / "README.md").read_text())

    def test_successor_requires_superseded_status(self):
        """An active record cannot simultaneously claim it has been replaced."""
        # Arrange
        self.change("superseded-by: []", "superseded-by: [D-002]")
        second = record("D-002").replace("supersedes: []", "supersedes: [D-001]")
        (self.directory / "D-002-target.md").write_text(second, encoding="ascii")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("superseded status and superseded-by must agree", "\n".join(errors))



class AdrProvenanceTests(AdrFixture):
    """Keep external provenance and local implementation evidence inspectable."""

    def test_external_source_requires_real_retrieval_date(self):
        """Shape-only provenance dates are not sufficient."""
        # Arrange
        self.change(ADR.REPOSITORY_ONLY, "- [Primary](https://example.org/spec) "
                    "(primary source; retrieved 2026-02-30)")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("Sources requires dated HTTPS", "\n".join(errors))

    def test_external_url_outside_sources_is_rejected(self):
        """Inline evidence cannot bypass the dated primary-source section."""
        # Arrange
        self.change("The fixture needs", "[Hidden source](https://example.org/spec)\n\nThe fixture needs")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("external URLs belong only under Sources", "\n".join(errors))

    def test_repository_only_marker_cannot_mix_with_external_sources(self):
        """A repository-only claim cannot conceal an external dependency in the same section."""
        # Arrange
        self.change(ADR.REPOSITORY_ONLY, ADR.REPOSITORY_ONLY + "\n- [Primary](https://example.org/spec) "
                    "(primary source; retrieved 2026-09-19)")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("exclusive repository-only marker", "\n".join(errors))

    def test_dated_primary_source_passes(self):
        """The mechanical gate accepts explicit provenance without claiming publisher review."""
        # Arrange
        self.change(ADR.REPOSITORY_ONLY, "- [Primary](https://example.org/spec) "
                    "(primary source; retrieved 2026-09-19)")

        # Act
        errors = self.validate()

        # Assert
        self.assertEqual([], errors)

    def test_reference_links_are_rejected(self):
        """Hidden reference definitions cannot move URLs outside provenance inspection."""
        # Arrange
        self.change("This is a proposed fixture", "[Hidden][source]\n\n[source]: https://example.org/spec\n\n"
                    "This is a proposed fixture")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("reference links are unsupported", "\n".join(errors))

    def test_malformed_url_is_a_diagnostic(self):
        """Invalid IPv6 URL syntax cannot crash validation of remaining records."""
        # Arrange
        self.change(ADR.REPOSITORY_ONLY, "- [Primary](https://[invalid/spec) "
                    "(primary source; retrieved 2026-09-19)")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("malformed link destination", "\n".join(errors))

    def test_missing_local_evidence_is_rejected(self):
        """A named but nonexistent implementation path cannot prove delivery."""
        # Arrange
        (self.root / "evidence.txt").unlink()

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("local link does not resolve", "\n".join(errors))

    def test_encoded_local_path_escape_is_rejected(self):
        """Percent-escaped traversal is resolved before testing repository containment."""
        # Arrange
        self.change("../../evidence.txt", "%2e%2e/%2e%2e/%2e%2e/%2e%2e/%2e%2e/etc/hosts")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("does not resolve inside repository", "\n".join(errors))

    def test_encoded_null_byte_is_a_diagnostic(self):
        """An encoded NUL cannot crash the validator while resolving a local path."""
        # Arrange
        self.change("../../evidence.txt", "../../evidence%00.txt")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("link destinations must not contain null bytes", "\n".join(errors))

    def test_symlinked_evidence_outside_repository_is_rejected(self):
        """A repository-local symlink is not proof that its external target belongs to the source."""
        # Arrange
        (self.root / "evidence.txt").unlink()
        (self.root / "evidence.txt").symlink_to("/etc/hosts")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("does not resolve inside repository", "\n".join(errors))

    def test_local_path_escape_is_rejected(self):
        """Existing filesystem content outside the repository is not local evidence."""
        # Arrange
        self.change("../../evidence.txt", "../../../../../../etc/hosts")

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("does not resolve inside repository", "\n".join(errors))



class AdrIndexIntegrityTests(AdrFixture):
    """Reject stale navigation and output path attacks without hiding corpus errors."""

    def test_index_symlink_cannot_write_outside_repository(self):
        """Explicit regeneration must not overwrite an external symlink target."""
        # Arrange
        external = self.root.parent / (self.root.name + "-untouched.txt")
        external.write_text("preserve\n", encoding="ascii")
        self.addCleanup(external.unlink)
        (self.directory / "README.md").symlink_to(external)

        # Act
        errors = self.validate()

        # Assert
        self.assertIn("must not be symbolic links", "\n".join(errors))
        self.assertEqual("preserve\n", external.read_text())

    def test_stale_readme_is_rejected(self):
        """Normal mode reports drift without silently fixing the human index."""
        # Arrange
        self.assertEqual([], self.validate())
        (self.directory / "README.md").write_text("stale\n", encoding="ascii")

        # Act
        errors = self.validate(write_index=False)

        # Assert
        self.assertIn("README.md:1: generated index is missing or stale", "\n".join(errors))
        self.assertEqual("stale\n", (self.directory / "README.md").read_text())

    def test_stale_json_is_rejected(self):
        """Machine-readable navigation receives the same drift check as Markdown."""
        # Arrange
        self.assertEqual([], self.validate())
        (self.directory / "decision-index.json").write_text("{}\n", encoding="ascii")

        # Act
        errors = self.validate(write_index=False)

        # Assert
        self.assertIn("decision-index.json:1: generated index is missing or stale", "\n".join(errors))

    def test_invalid_corpus_cannot_overwrite_indexes(self):
        """An explicit write request is still gated by complete source validation."""
        # Arrange
        self.assertEqual([], self.validate())
        before = (self.directory / "README.md").read_bytes()
        self.change("status: proposed", "status: implemented")

        # Act
        errors = self.validate()

        # Assert
        self.assertTrue(errors)
        self.assertEqual(before, (self.directory / "README.md").read_bytes())



class AdrEntryPointTests(AdrFixture):
    """Check real CLI behavior and the stable shell delegation contract."""

    def test_unknown_cli_option_is_usage_failure(self):
        """Unrecognized flags never fall back to an unintended validation mode."""
        # Arrange
        stream = io.StringIO()

        # Act
        with contextlib.redirect_stderr(stream), self.assertRaises(SystemExit) as caught:
            ADR.main(["--unknown"])

        # Assert
        self.assertEqual(2, caught.exception.code)
        self.assertIn("unrecognized arguments", stream.getvalue())

    def test_missing_cli_value_is_usage_failure(self):
        """A missing root path fails before touching the default repository."""
        # Arrange
        stream = io.StringIO()

        # Act
        with contextlib.redirect_stderr(stream), self.assertRaises(SystemExit) as caught:
            ADR.main(["--root"])

        # Assert
        self.assertEqual(2, caught.exception.code)
        self.assertIn("expected one argument", stream.getvalue())

    def test_shell_entry_point_delegates_to_same_validator(self):
        """The actual shell entry point preserves fixture-root selection and exit status."""
        # Arrange
        self.assertEqual([], self.validate())

        # Act
        result = subprocess.run(["bash", str(REPOSITORY / "eng/validate-adrs.sh"), "--root", str(self.root)],
                                capture_output=True, text=True, check=False)

        # Assert
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Validated MADR 4.0.0", result.stdout)


class AdrRepositoryTests(unittest.TestCase):
    """Keep the delivered corpus under the same rules as negative fixture tests."""

    def test_repository_corpus_and_indexes_match(self):
        """All actual records and implementation paths must validate in read-only mode."""
        # Arrange
        root = REPOSITORY

        # Act
        errors = ADR.validate_repository(root)

        # Assert
        self.assertEqual([], errors, "\n".join(errors))


if __name__ == "__main__":
    unittest.main()
