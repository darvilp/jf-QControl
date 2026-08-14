#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/../.." && pwd)"
package_script="${project_root}/scripts/package.sh"

if [[ ! -x "${package_script}" ]]; then
    printf 'Package entrypoint is missing or not executable: %s\n' "${package_script}" >&2
    exit 1
fi

artifact_path="$("${package_script}" | tail -n 1)"
if [[ ! -f "${artifact_path}" ]]; then
    printf 'Package was not created: %s\n' "${artifact_path}" >&2
    exit 1
fi

mapfile -t package_entries < <(unzip -Z1 "${artifact_path}" | sort)
expected_entries=(
    "Jellyfin.Plugin.QControl.Domain.dll"
    "Jellyfin.Plugin.QControl.dll"
    "meta.json"
)

if [[ "${package_entries[*]}" != "${expected_entries[*]}" ]]; then
    printf 'Unexpected package entries:\n' >&2
    printf '  %s\n' "${package_entries[@]}" >&2
    exit 1
fi

if ! unzip -p "${artifact_path}" meta.json | jq --exit-status \
    '.name == "QControl"
     and .guid == "ab18c878-1856-4853-8f21-5028a1d5a7b2"
     and .version == "0.1.0.1"
     and .targetAbi == "10.11.11.0"' >/dev/null; then
    printf 'Package metadata does not match the pinned plugin contract.\n' >&2
    exit 1
fi

printf 'Verified QControl package: %s\n' "${artifact_path}"
