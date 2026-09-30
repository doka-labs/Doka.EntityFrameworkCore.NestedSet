#!/usr/bin/env bash

set -euo pipefail

if (($# != 0)); then
    echo "Usage: $0" >&2
    exit 2
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
cd "$repo_root"

# WHY: Operator readiness shares the existing release checks instead of maintaining a second Git/signing policy.
exec python3 -m eng.release.publication pre-tag
