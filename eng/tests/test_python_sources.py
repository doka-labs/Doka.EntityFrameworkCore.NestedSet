"""Check every engineering Python source before release tooling executes it."""

import ast
import unittest
from pathlib import Path

ENGINEERING_ROOT = Path(__file__).resolve().parents[1]


class PythonSourceSyntaxTests(unittest.TestCase):
    """Catch syntax errors in scripts that other unit tests do not import."""

    def test_every_engineering_python_source_parses(self):
        """Parse every script without executing release entry points."""
        # Arrange
        # WHY: Unit discovery imports test modules, but release entry points can remain unimported.
        sources = sorted(ENGINEERING_ROOT.rglob("*.py"))

        # Act
        parsed = 0
        for source in sources:
            ast.parse(source.read_text(encoding="utf-8"), filename=str(source))
            parsed += 1

        # Assert
        self.assertGreater(parsed, 0)
        self.assertEqual(len(sources), parsed)


if __name__ == "__main__":
    unittest.main()
