"""Reject empty Cobertura artifacts and require execution of both shipping libraries."""

import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REQUIRED_MODULES = {"Doka.NestedSet", "Doka.EntityFrameworkCore.NestedSet"}


def verify_reports(results_directory: Path) -> int:
    """Validate every discovered report and return the number of complete artifacts.

    Raise ValueError when reports are absent, empty, or lack hits in a shipping module.
    XML and filesystem errors remain failures rather than silently dropping a report.
    """
    reports = sorted(results_directory.rglob("*.cobertura.xml"))
    if not reports:
        raise ValueError(f"No Cobertura reports found under {results_directory}.")

    covered_modules = set()
    for report in reports:
        root = ET.parse(report).getroot()
        packages = root.findall("./packages/package")
        if root.tag != "coverage" or not packages or not any(package.findall(".//line") for package in packages):
            # WHY: A canceled collection can leave instrumented copies that yield an empty report on the next run.
            # Validate each report so a healthy project cannot hide another project's missing coverage.
            raise ValueError(f"Empty or invalid Cobertura report: {report}.")

        for package in packages:
            if any(int(line.get("hits", "0")) > 0 for line in package.findall(".//line")):
                covered_modules.add(package.get("name"))

    missing = REQUIRED_MODULES - covered_modules
    if missing:
        raise ValueError(f"No executed lines recorded for: {', '.join(sorted(missing))}.")

    return len(reports)


def main() -> int:
    """Read the CI result directory and report verification success or a nonzero failure."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("results_directory", type=Path, help="Directory containing Cobertura reports recursively.")
    arguments = parser.parse_args()

    try:
        count = verify_reports(arguments.results_directory)
    except (OSError, ValueError, ET.ParseError) as error:
        print(f"Coverage verification failed: {error}", file=sys.stderr)

        return 1

    print(f"Verified {count} coverage reports with executed lines in both shipping modules.")

    return 0


if __name__ == "__main__":
    sys.exit(main())
