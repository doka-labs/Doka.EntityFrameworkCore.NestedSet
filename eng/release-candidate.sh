#!/usr/bin/env bash
set -euo pipefail

# WHY: Local, CI, and RC runs must execute the same checks from a fresh output directory.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
cd "$repo_root"
exec python3 -m eng.release.qualification "$@"
