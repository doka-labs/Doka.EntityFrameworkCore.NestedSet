"""Prepare a reviewable lockfile-only patch after a failed Dependabot locked restore."""

import argparse
import difflib
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path


def lockfile_paths(solution):
    """Select only lockfiles beside the authored projects listed in the solution."""
    solution = solution.resolve(strict=True)
    root = solution.parent
    paths = []
    for project in ET.parse(solution).iter("Project"):
        path = (root / project.attrib["Path"]).resolve(strict=True)
        path.relative_to(root)
        if path.suffix != ".csproj":
            raise ValueError(f"Unsupported project in lockfile diagnostic: {path}")

        lock = path.parent / "packages.lock.json"
        if lock in paths:
            raise ValueError("Projects sharing a directory require an explicit lockfile mapping.")

        paths.append(lock)

    if not paths:
        raise ValueError("The solution contains no projects.")

    return root, tuple(paths)


def snapshot(root, paths):
    """Read the selected lockfiles without following a lockfile link outside the checkout."""
    result = {}
    for path in paths:
        path.resolve(strict=False).relative_to(root)
        if path.is_symlink():
            raise ValueError(f"A lockfile must not be a symbolic link: {path}")

        name = path.relative_to(root).as_posix()
        if any(ord(character) < 32 for character in name):
            raise ValueError("A lockfile path contains a control character.")

        result[name] = path.read_bytes() if path.exists() else None

    return result


def create_patch(before, after):
    """Render only changed lockfiles, preserving additions, deletions and missing final newlines."""
    output = []
    for name in sorted(before.keys() | after.keys()):
        old = before.get(name)
        new = after.get(name)
        if old == new:
            continue

        lines = difflib.unified_diff(
            (old or b"").decode("utf-8").splitlines(keepends=True),
            (new or b"").decode("utf-8").splitlines(keepends=True),
            fromfile="/dev/null" if old is None else "a/" + name,
            tofile="/dev/null" if new is None else "b/" + name,
        )
        for line in lines:
            output.append(line)
            if not line.endswith("\n"):
                # WHY: NuGet lockfiles can omit the final newline; losing this marker makes the patch invalid.
                output.append("\n\\ No newline at end of file\n")

    return "".join(output)


def main():
    """Reevaluate the complete solution and emit evidence without committing or changing CI's failed outcome."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--solution", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    root, paths = lockfile_paths(args.solution)
    before = snapshot(root, paths)
    subprocess.run(
        ["dotnet", "restore", str(args.solution.resolve()), "--force-evaluate", "-p:RestoreLockedMode=false"],
        cwd=root,
        check=True,
    )
    patch = create_patch(before, snapshot(root, paths))
    if not patch:
        raise RuntimeError("Whole-solution restore produced no lockfile changes; inspect the original failure.")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(patch, encoding="utf-8", newline="")
    print(f"Prepared lockfile-only patch: {args.output}. The original locked restore remains failed.")


if __name__ == "__main__":
    main()
