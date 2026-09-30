#!/usr/bin/env bash
set -euo pipefail

readonly sbom_tool_version="4.1.5"

usage() {
    echo "Usage: $0 --package-dir <path> --output <path> --version <version>" >&2
}

package_dir=""
output_dir=""
package_version=""

while (($# > 0)); do
    case "$1" in
        --package-dir|--output|--version)
            if (($# < 2)) || [[ -z "$2" ]]; then
                usage
                exit 2
            fi

            case "$1" in
                --package-dir) package_dir="$2" ;;
                --output) output_dir="$2" ;;
                --version) package_version="$2" ;;
            esac

            shift 2
            ;;
        --help)
            usage
            exit 0
            ;;
        *)
            usage
            exit 2
            ;;
    esac
done

if [[ -z "$package_dir" || -z "$output_dir" || -z "$package_version" ]]; then
    usage
    exit 2
fi

case "$(uname -s)-$(uname -m)" in
    Linux-x86_64)
        asset_name="sbom-tool-linux-x64"
        expected_sha256="bf5d4f99bc98c119d549d08fc02ae92598a7a42772f17317c01031a92632e05b"
        ;;
    Darwin-arm64)
        asset_name="sbom-tool-osx-arm64"
        expected_sha256="bb25842fd707fbe78d3ac9de0d2b27ee2f4a97764f3b8a5c2068c826e75f3535"
        ;;
    Darwin-x86_64)
        asset_name="sbom-tool-osx-x64"
        expected_sha256="e9a45e3ffdcab920c7bbd2987ce0a133f275241e080bb48c1a3dbe6b558e8ee6"
        ;;
    *)
        echo "Unsupported SBOM host: $(uname -s)-$(uname -m)" >&2
        exit 1
        ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
work_dir="$(mktemp -d "${TMPDIR:-/tmp}/doka-nestedset-sbom-tool.XXXXXX")"
trap 'rm -rf -- "$work_dir"' EXIT
tool_path="$work_dir/$asset_name"
# WHY: Component detection and NuGet create their own scratch files in TMPDIR; keep those under the same cleanup root.
export TMPDIR="$work_dir/tmp"
mkdir -p "$TMPDIR"
# WHY: Self-contained single-file apps may extract bundled runtime libraries; keep those in the owned workspace too.
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="$work_dir/bundle"

# WHY: The release binary includes its runtime; only this verified temporary asset is executed.
# No dotnet tool manifest, global tool installation, or separately installed runtime is needed.
curl --fail --silent --show-error --location --retry 3 --proto '=https' --tlsv1.2 \
    "https://github.com/microsoft/sbom-tool/releases/download/v$sbom_tool_version/$asset_name" \
    --output "$tool_path"
python3 - "$tool_path" "$expected_sha256" <<'PY'
import hashlib
from pathlib import Path
import sys

digest = hashlib.sha256()
with Path(sys.argv[1]).open("rb") as stream:
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        digest.update(block)

if digest.hexdigest() != sys.argv[2]:
    raise SystemExit("SBOM tool checksum mismatch.")
PY
chmod 0755 "$tool_path"

python3 "$script_dir/verify-package-sbom.py" generate \
    --package-dir "$package_dir" --output "$output_dir" --version "$package_version" --tool "$tool_path"
