#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

if [[ "$#" -ne 3 ]]; then
    printf 'Usage: %s TAG INPUT_PACKAGE OUTPUT_DIRECTORY\n' "$0" >&2
    exit 2
fi

tag="$1"
input_package="$2"
output_directory="$3"
version="${tag#v}"
asset_name="Jellyfin.Plugin.QControl_${version}.zip"

"${script_dir}/verify-release-contract.sh" "${tag}" "${input_package}"
mkdir -p "${output_directory}"
install -m 0644 "${input_package}" "${output_directory}/${asset_name}"
(
    cd -- "${output_directory}"
    sha256sum "${asset_name}" >"${asset_name}.sha256"
)

printf '%s\n' "${output_directory}/${asset_name}"
