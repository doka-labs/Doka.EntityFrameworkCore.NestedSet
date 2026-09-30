"""Verify narrow project selection and valid lockfile patch rendering with standard-library fixtures."""

import importlib.util
import tempfile
import unittest
from pathlib import Path

_SPEC = importlib.util.spec_from_file_location(
    "lockfile_patch", Path(__file__).resolve().parents[1] / "prepare-lockfile-patch.py"
)
_MODULE = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(_MODULE)


class LockfilePatchTests(unittest.TestCase):
    """Keep cache files and unrelated changes out of the diagnostic artifact."""

    def test_solution_selects_only_authored_projects(self):
        """An unrelated cache lockfile does not enter the project-derived snapshot."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            project = root / "src" / "Library.csproj"
            project.parent.mkdir()
            project.write_text("<Project/>\n")
            expected = project.parent / "packages.lock.json"
            expected.write_text('{}\n')
            cache = root / "artifacts" / "packages.lock.json"
            cache.parent.mkdir()
            cache.write_text('{"unrelated": true}\n')
            solution = root / "Library.slnx"
            solution.write_text('<Solution><Project Path="src/Library.csproj"/></Solution>\n')

            selected_root, paths = _MODULE.lockfile_paths(solution)
            captured = _MODULE.snapshot(selected_root, paths)

            self.assertEqual({"src/packages.lock.json": b"{}\n"}, captured)

    def test_missing_final_newlines_are_preserved(self):
        """Both changed EOF lines retain the markers required by patch consumers."""
        before = {"src/packages.lock.json": b'{"version": 1}'}
        after = {"src/packages.lock.json": b'{"version": 2}'}

        patch = _MODULE.create_patch(before, after)

        self.assertEqual(2, patch.count("\\ No newline at end of file\n"))
        self.assertIn('-{"version": 1}\n\\ No newline at end of file\n', patch)
        self.assertIn('+{"version": 2}\n\\ No newline at end of file\n', patch)

    def test_additions_and_deletions_use_null_paths(self):
        """A newly created lock and a removed lock are explicit patch additions and deletions."""
        before = {"old/packages.lock.json": b"{}\n", "new/packages.lock.json": None}
        after = {"old/packages.lock.json": None, "new/packages.lock.json": b"{}\n"}

        patch = _MODULE.create_patch(before, after)

        self.assertIn("--- /dev/null\n+++ b/new/packages.lock.json\n", patch)
        self.assertIn("--- a/old/packages.lock.json\n+++ /dev/null\n", patch)

    def test_unchanged_lockfiles_produce_no_patch(self):
        """A diagnostic must not manufacture a patch for unchanged dependency evidence."""
        before = {"src/packages.lock.json": b"{}\n"}

        patch = _MODULE.create_patch(before, dict(before))

        self.assertEqual("", patch)

    def test_symlinked_lockfile_is_rejected(self):
        """A malicious lockfile link cannot copy unrelated file contents into the artifact."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / "unrelated.txt"
            target.write_text("private fixture content")
            link = root / "packages.lock.json"
            link.symlink_to(target)

            with self.assertRaises(ValueError):
                _MODULE.snapshot(root, [link])

    def test_replaced_parent_directory_cannot_escape_checkout(self):
        """A restore-time directory link cannot redirect the later snapshot to unrelated files."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve() / "checkout"
            root.mkdir()
            parent = root / "src"
            parent.mkdir()
            selected = parent / "packages.lock.json"
            outside = Path(directory) / "outside"
            outside.mkdir()
            (outside / "packages.lock.json").write_text("private fixture content")
            parent.rmdir()
            parent.symlink_to(outside, target_is_directory=True)

            with self.assertRaises(ValueError):
                _MODULE.snapshot(root, [selected])

    def test_replaced_checkout_cannot_move_the_trust_boundary(self):
        """Replacing the checkout itself must not redefine its original canonical boundary."""
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory).resolve()
            root = base / "checkout"
            root.mkdir()
            selected = root / "packages.lock.json"
            outside = base / "outside"
            outside.mkdir()
            (outside / "packages.lock.json").write_text("private fixture content")
            root.rename(base / "original")
            root.symlink_to(outside, target_is_directory=True)

            with self.assertRaises(ValueError):
                _MODULE.snapshot(root, [selected])


if __name__ == "__main__":
    unittest.main()
