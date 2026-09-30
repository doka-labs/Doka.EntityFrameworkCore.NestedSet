"""Bounded public-readback protocol tests with deterministic response and clock injection."""

import io
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).parent))
from test_release_packages import capture, make_packages, probe

sys.path.insert(0, str(Path(__file__).parents[1] / "release"))
import nuget
import packages


class ReadbackTests(unittest.TestCase):
    """Reject conflicting bytes while retaining only proven subjects between bounded retries."""

    def setUp(self):
        """Prepare complete candidate archives, signed responses, and an advancing fake clock."""
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.directory = self.root / "packages"
        self.directory.mkdir()
        make_packages(self.directory)
        self.patch = patch.object(packages, "inspect_symbols", side_effect=probe)
        self.patch.start()
        self.addCleanup(self.patch.stop)
        self.responses = {}
        self.calls = []
        self.now = 0
        self.output = self.root / "readback.json"
        self.verifier = Mock()

        for package_id in packages.PACKAGE_IDS:
            archive = self.directory / f"{package_id}.0.1.0-dev.nupkg"
            buffer = io.BytesIO(archive.read_bytes())

            with zipfile.ZipFile(buffer, "a") as signed:
                signed.writestr(".signature.p7s", b"signature fixture")

            url = nuget.package_url("https://api.nuget.org/v3-flatcontainer/", {"id": package_id, "version": "0.1.0-dev"})
            self.responses[url] = [(200, buffer.getvalue())]
            symbol = probe(None, None, package_id)[0]
            self.responses[symbol["url"]] = [(200, package_id.encode("ascii"))]

    def fetch(self, url, headers, timeout):
        """Advance scripted HTTP responses and retain request headers for protocol assertions."""
        self.calls.append((url, headers, timeout))

        if url == nuget.INDEX:
            return 200, json.dumps({"resources": [{"@type": "PackageBaseAddress/3.0.0",
                                                  "@id": "https://api.nuget.org/v3-flatcontainer/"}]}).encode("ascii")

        responses = self.responses[url]

        return responses.pop(0) if len(responses) > 1 else responses[0]

    def sleep(self, seconds):
        """Move the injected monotonic clock without delaying the test process."""
        self.now += seconds

    def readback(self):
        """Exercise the real readback loop with no network or SDK subprocess."""
        return nuget.readback(self.directory, "0.1.0-dev", self.output, 25, fetcher=self.fetch,
                             signature_verifier=self.verifier, clock=lambda: self.now, sleep=self.sleep)

    def primary_url(self):
        """Select one exact primary URL for independent conflict scenarios."""
        return next(url for url in self.responses if url.endswith(".nupkg"))

    def symbol_url(self):
        """Select one exact PDB URL for independent symbol scenarios."""
        return next(url for url in self.responses if url.endswith(".pdb"))

    def test_signed_packages_and_exact_public_symbols_complete(self):
        """Completion requires both signature checks and raw PDB checksums with protocol headers."""
        # Arrange
        expected_header = "SHA256:" + "b" * 64

        # Act
        result = self.readback()

        # Assert
        self.assertTrue(result["success"])
        self.assertEqual(2, self.verifier.call_count)
        self.assertTrue(all(row["signatureVerified"] for row in result["packages"]))
        symbol_calls = [call for call in self.calls if call[0].endswith(".pdb")]
        self.assertTrue(all(call[1] == {"SymbolChecksumValidationSupported": "1", "SymbolChecksum": expected_header}
                            for call in symbol_calls))

    def test_long_indexing_budget_is_accepted(self):
        """The supported readback window covers an observed 90-minute indexing delay."""
        # Arrange
        delayed_url = self.primary_url()

        def delayed_fetch(url, headers, timeout):
            if url == delayed_url and self.now < 5400:
                return 404, b""

            return self.fetch(url, headers, timeout)

        # Act
        result = nuget.readback(self.directory, "0.1.0-dev", self.output, 7200, fetcher=delayed_fetch,
                                signature_verifier=self.verifier, clock=lambda: self.now, sleep=self.sleep,
                                poll_seconds=60)

        # Assert
        self.assertTrue(result["success"])
        self.assertGreaterEqual(self.now, 5400)

    def test_unbounded_indexing_budget_is_rejected(self):
        """A typo cannot create an unbounded publication poll."""
        # Act
        error = capture(lambda: nuget.readback(self.directory, "0.1.0-dev", self.output, 7201,
                                               fetcher=self.fetch, signature_verifier=self.verifier,
                                               clock=lambda: self.now, sleep=self.sleep))

        # Assert
        self.assertIsInstance(error, nuget.NuGetError)
        self.assertRegex(str(error), "1..7200")

    def test_missing_symbols_retry_without_redownloading_verified_packages(self):
        """A delayed symbol index retries only its pending payload."""
        # Arrange
        url = self.symbol_url()
        self.responses[url].insert(0, (404, b""))

        # Act
        result = self.readback()

        # Assert
        self.assertTrue(result["success"])
        self.assertEqual(2, sum(call[0] == url for call in self.calls))
        self.assertEqual(2, sum(call[0].endswith(".nupkg") for call in self.calls))

    def test_ef_indexing_first_preserves_dependency_order_in_completion_receipt(self):
        """Feed indexing order must not change the package order authenticated by the publication gate."""
        # Arrange
        self.responses[self.primary_url()].insert(0, (404, b""))

        # Act
        result = self.readback()

        # Assert
        self.assertTrue(result["success"])
        self.assertEqual(list(packages.PACKAGE_IDS), [row["id"] for row in result["packages"]])

    def test_unsigned_response_is_pending_and_never_cached_as_verified(self):
        """Repository signatures may lag indexing but remain mandatory for completion."""
        # Arrange
        url = self.primary_url()
        primary = self.directory / "Doka.NestedSet.0.1.0-dev.nupkg"
        self.responses[url].insert(0, (200, primary.read_bytes()))

        # Act
        result = self.readback()

        # Assert
        self.assertTrue(result["success"])
        self.assertEqual(2, self.verifier.call_count)
        self.assertIn("Repository signature pending", result["events"][0]["pending"][0])

    def test_invalid_signature_is_terminal(self):
        """A present but invalid signature cannot be retried into a hidden success."""
        # Arrange
        self.verifier.side_effect = nuget.NuGetError("Invalid signature")

        # Act
        error = capture(self.readback)

        # Assert
        self.assertRegex(str(error), "Invalid signature")
        self.assertEqual(1, self.verifier.call_count)
        self.assertFalse(json.loads(self.output.read_text())["success"])

    def test_conflicting_public_primary_is_terminal(self):
        """Public identity reuse with different payload must never enter a polling success path."""
        # Arrange
        url = self.primary_url()
        buffer = io.BytesIO(self.responses[url][0][1])

        with zipfile.ZipFile(buffer, "a") as archive:
            archive.writestr("unexpected.txt", "conflict")

        self.responses[url] = [(200, buffer.getvalue())]

        # Act
        error = capture(self.readback)

        # Assert
        self.assertRegex(str(error), "payload conflicts")
        self.assertEqual(0, self.now)
        self.assertFalse(json.loads(self.output.read_text())["success"])

    def test_conflicting_public_symbols_are_terminal(self):
        """Matching package signatures cannot hide a mismatched public PDB."""
        # Arrange
        self.responses[self.symbol_url()] = [(200, b"wrong PDB")]

        # Act
        error = capture(self.readback)

        # Assert
        self.assertRegex(str(error), "PDB bytes conflict")
        self.assertEqual(0, self.now)

    def test_transient_statuses_are_bounded_by_one_deadline(self):
        """An indefinitely pending feed produces durable failure rather than an empty pass."""
        # Arrange
        self.responses[self.primary_url()] = [(429, b""), (408, b""), (503, b""), (0, b"")]

        # Act
        error = capture(self.readback)

        # Assert
        self.assertRegex(str(error), "deadline exhausted")
        self.assertEqual(25, self.now)
        self.assertFalse(json.loads(self.output.read_text())["success"])

    def test_nontransient_http_status_is_terminal(self):
        """Authentication and protocol rejection cannot prove eventual publication."""
        # Arrange
        self.responses[self.primary_url()] = [(403, b"")]

        # Act
        error = capture(self.readback)

        # Assert
        self.assertRegex(str(error), "HTTP 403")
        self.assertEqual(0, self.now)

    def test_unused_preflight_requires_both_ids_absent(self):
        """One absent identity does not authorize reuse of an existing second package."""
        # Arrange
        self.responses[self.primary_url()] = [(404, b"")]

        # Act
        error = capture(lambda: nuget.check_unused_version("0.1.0-dev", fetcher=self.fetch))

        # Assert
        self.assertRegex(str(error), "Cannot establish unused version")

    def test_unused_preflight_accepts_only_complete_absence(self):
        """A new version may proceed only after both package URLs return 404."""
        # Arrange
        for url in self.responses:
            self.responses[url] = [(404, b"")]

        # Act
        result = nuget.check_unused_version("0.1.0-dev", fetcher=self.fetch)

        # Assert
        self.assertEqual(["absent", "absent"], [row["state"] for row in result["packages"]])

    def test_same_candidate_retry_accepts_matching_signed_payloads(self):
        """Already published bytes remain reusable only after canonical payload equality."""
        # Arrange
        version = "0.1.0-dev"

        # Act
        result = nuget.check_availability(self.directory, version, True, fetcher=self.fetch)

        # Assert
        self.assertEqual(["matching", "matching"], [row["state"] for row in result["packages"]])

    def test_availability_transport_failure_is_not_absence(self):
        """A failed lookup cannot authorize an irreversible package identity."""
        # Arrange
        self.responses[self.primary_url()] = [(0, b"")]

        # Act
        error = capture(lambda: nuget.check_availability(self.directory, "0.1.0-dev", fetcher=self.fetch))

        # Assert
        self.assertRegex(str(error), "HTTP 0")

    def test_invalid_candidate_replaces_any_stale_success_receipt(self):
        """A failed retry cannot leave an old success document looking like its current result."""
        # Arrange
        self.output.write_text('{"success": true}')
        (self.directory / "Doka.NestedSet.0.1.0-dev.snupkg").unlink()

        # Act
        error = capture(self.readback)

        # Assert
        self.assertIsInstance(error, packages.PackageError)
        self.assertFalse(json.loads(self.output.read_text())["success"])
        self.assertEqual([], self.calls)

    def test_foreign_package_endpoint_is_rejected(self):
        """Service discovery cannot redirect public identity verification to an arbitrary feed."""
        # Arrange
        fetcher = Mock(return_value=(200, json.dumps({"resources": [{"@type": "PackageBaseAddress/3.0.0",
                                                                    "@id": "https://other.invalid/packages/"}]}).encode()))

        # Act
        error = capture(lambda: nuget.package_base(fetcher, 30))

        # Assert
        self.assertRegex(str(error), "Unexpected NuGet package endpoint")

    def test_signature_command_verifies_all_signatures_on_retained_bytes(self):
        """The protocol uses the SDK verifier instead of inferring trust from signature file presence."""
        # Arrange
        primary = self.directory / "Doka.NestedSet.0.1.0-dev.nupkg"
        log = self.root / "signature.log"
        result = Mock(returncode=0, stdout="Signature verified", stderr="")

        # Act
        with patch.object(nuget.subprocess, "run", return_value=result) as command:
            nuget.verify_signature(primary, log, 30)

        # Assert
        self.assertEqual(["dotnet", "nuget", "verify", str(primary), "--all", "--verbosity", "normal"],
                         command.call_args.args[0])
        self.assertEqual("Signature verified", log.read_text())

    def test_manifest_readback_requires_no_metadata_helper_execution(self):
        """Hosted completion can reuse authenticated symbol probes while still reading actual public bytes."""
        # Arrange
        manifest = packages.verify_packages(self.directory, "0.1.0-dev")

        # Act
        with patch.object(packages, "inspect_symbols", side_effect=AssertionError("Helper must not execute")):
            result = nuget.readback(self.directory, "0.1.0-dev", self.output, 25, manifest=manifest,
                                    fetcher=self.fetch, signature_verifier=self.verifier,
                                    clock=lambda: self.now, sleep=self.sleep)

        # Assert
        self.assertTrue(result["success"])
        self.assertEqual(2, self.verifier.call_count)

    def test_manifest_availability_requires_no_metadata_helper_execution(self):
        """Pre-push retry checks preserve byte binding without rebuilding the qualification tool."""
        # Arrange
        manifest = packages.verify_packages(self.directory, "0.1.0-dev")

        # Act
        with patch.object(packages, "inspect_symbols", side_effect=AssertionError("Helper must not execute")):
            result = nuget.check_availability(self.directory, "0.1.0-dev", True, manifest=manifest, fetcher=self.fetch)

        # Assert
        self.assertEqual(["matching", "matching"], [row["state"] for row in result["packages"]])


if __name__ == "__main__":
    unittest.main()
