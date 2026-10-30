"""Verify complete shipping coverage without collecting or executing .NET code."""

import importlib.util
import re
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

from eng.release.common import PACKAGES
from eng.tests.test_release_packages import capture

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("nestedset_coverage", ROOT / "eng/verify-coverage.py")
COVERAGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(COVERAGE)


class CoverageTests(unittest.TestCase):
    """Require actual executed lines from every adapter as well as both shared libraries."""

    def setUp(self):
        """Create one isolated report per actual shipping module."""
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        for name in PACKAGES:
            root = ET.Element("coverage")
            package = ET.SubElement(ET.SubElement(root, "packages"), "package", name=name)
            ET.SubElement(package, "line", number="1", hits="1")
            ET.ElementTree(root).write(self.directory / (name + ".cobertura.xml"))

    def test_all_shipping_modules_have_executed_lines(self):
        """The full six-module result passes independently of which project collected each report."""
        # Arrange
        expected = len(PACKAGES)

        # Act
        count = COVERAGE.verify_reports(self.directory)

        # Assert
        self.assertEqual(expected, count)
        self.assertEqual(set(PACKAGES), COVERAGE.REQUIRED_MODULES)

    def test_missing_final_adapter_is_not_hidden_by_shared_library_coverage(self):
        """Passing coverage of the original two libraries cannot replace a missing adapter."""
        # Arrange
        (self.directory / (PACKAGES[-1] + ".cobertura.xml")).unlink()

        # Act
        error = capture(lambda: COVERAGE.verify_reports(self.directory))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), re.escape(PACKAGES[-1]))

    def test_instrumented_adapter_without_hits_is_rejected(self):
        """An adapter merely present in a report still needs an executed line."""
        # Arrange
        path = self.directory / (PACKAGES[-1] + ".cobertura.xml")
        document = ET.parse(path)
        document.find(".//line").set("hits", "0")
        document.write(path)

        # Act
        error = capture(lambda: COVERAGE.verify_reports(self.directory))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), re.escape(PACKAGES[-1]))

    def test_empty_project_report_is_not_hidden_by_complete_other_reports(self):
        """A canceled project cannot leave a vacuous artifact beside six healthy reports."""
        # Arrange
        (self.directory / "empty.cobertura.xml").write_text("<coverage />", encoding="ascii")

        # Act
        error = capture(lambda: COVERAGE.verify_reports(self.directory))

        # Assert
        self.assertIsInstance(error, ValueError)
        self.assertRegex(str(error), "Empty or invalid")

    def test_collector_includes_shipping_assemblies_and_excludes_test_assemblies(self):
        """The collection filter and required-module contract cover the same exact production names."""
        # Arrange
        filters = [item.text for item in ET.parse(ROOT / "eng/coverage.runsettings").findall(".//ModulePath")]

        # Act
        selected = {name for name in PACKAGES if any(re.fullmatch(pattern, name + ".dll") for pattern in filters)}
        tests_selected = [name for name in PACKAGES
                          if any(re.fullmatch(pattern, name + ".Tests.dll") for pattern in filters)]

        # Assert
        self.assertEqual(set(PACKAGES), selected)
        self.assertEqual([], tests_selected)


if __name__ == "__main__":
    unittest.main()
