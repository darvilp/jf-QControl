#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

xml_value() {
    local element="$1"
    local file="$2"
    sed -n "s:.*<${element}>\([^<]*\)</${element}>.*:\1:p" "${file}"
}

if [[ "$#" -ne 2 && "$#" -ne 4 ]]; then
    printf 'Usage: %s TAG PACKAGE [MANIFEST IMMUTABLE_ASSET_URL]\n' "$0" >&2
    exit 2
fi

tag="$1"
package_path="$2"
manifest_path="${3:-}"
asset_url="${4:-}"
build_file="${project_root}/build.yaml"
props_file="${project_root}/Directory.Build.props"

[[ -f "${build_file}" ]] || fail "Missing build metadata: ${build_file}"
[[ -f "${props_file}" ]] || fail "Missing assembly metadata: ${props_file}"
[[ -f "${package_path}" ]] || fail "Package does not exist: ${package_path}"
[[ "${tag}" =~ ^v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] \
    || fail "Release tag ${tag} is not a four-component v-prefixed plugin version."

version="$("${script_dir}/read-build-metadata.sh" version)"
target_abi="$("${script_dir}/read-build-metadata.sh" targetAbi)"
framework="$("${script_dir}/read-build-metadata.sh" framework)"
plugin_guid="$("${script_dir}/read-build-metadata.sh" guid)"
[[ -n "${version}" && -n "${target_abi}" && -n "${framework}" && -n "${plugin_guid}" ]] \
    || fail 'build.yaml is missing required release metadata.'

tag_version="${tag#v}"
[[ "${tag_version}" == "${version}" ]] \
    || fail "Release tag ${tag} does not match build.yaml version ${version}."

for property in Version AssemblyVersion FileVersion; do
    property_value="$(xml_value "${property}" "${props_file}")"
    [[ "${property_value}" == "${version}" ]] \
        || fail "Directory.Build.props ${property} ${property_value:-<missing>} does not match build.yaml version ${version}."
done

project_framework="$(xml_value TargetFramework "${project_root}/Jellyfin.Plugin.QControl/Jellyfin.Plugin.QControl.csproj")"
[[ "${project_framework}" == "${framework}" ]] \
    || fail "Project target framework ${project_framework:-<missing>} does not match build.yaml framework ${framework}."

jellyfin_package_version="${target_abi%.0}"
for package_name in Jellyfin.Common Jellyfin.Controller Jellyfin.Model; do
    pinned_version="$(sed -n \
        "s:.*<PackageVersion Include=\"${package_name}\" Version=\"\([^\"]*\)\".*:\1:p" \
        "${project_root}/Directory.Packages.props")"
    [[ "${pinned_version}" == "${jellyfin_package_version}" ]] \
        || fail "${package_name} ${pinned_version:-<missing>} does not match target ABI package line ${jellyfin_package_version}."
done

test_server_version="$(sed -n \
    's|^[[:space:]]*image:[[:space:]]*jellyfin/jellyfin:\([^@[:space:]]*\).*|\1|p' \
    "${project_root}/compose.yaml")"
[[ "${test_server_version}" == "${jellyfin_package_version}" ]] \
    || fail "Test server ${test_server_version:-<missing>} does not match target ABI server version ${jellyfin_package_version}."

sdk_major="$(jq --raw-output '.sdk.version | split(".")[0]' "${project_root}/global.json")"
[[ "${framework}" == "net${sdk_major}.0" ]] \
    || fail "Pinned SDK major ${sdk_major} does not match build.yaml framework ${framework}."

package_version="$(unzip -p "${package_path}" meta.json | jq --raw-output .version)"
[[ "${package_version}" == "${version}" ]] \
    || fail "Package version ${package_version} does not match build.yaml version ${version}."
package_target_abi="$(unzip -p "${package_path}" meta.json | jq --raw-output .targetAbi)"
[[ "${package_target_abi}" == "${target_abi}" ]] \
    || fail "Package targetAbi ${package_target_abi} does not match build.yaml targetAbi ${target_abi}."

"${script_dir}/verify-package.sh" "${package_path}" "${version}" "${target_abi}"

if [[ -z "${manifest_path}" ]]; then
    printf 'Verified release contract for %s before manifest generation.\n' "${tag}"
    exit 0
fi

[[ -f "${manifest_path}" ]] || fail "Manifest does not exist: ${manifest_path}"
[[ -n "${asset_url}" ]] || fail 'An immutable asset URL is required with a manifest.'

version_entry_count="$(jq --raw-output \
    --arg guid "${plugin_guid}" \
    --arg version "${version}" \
    '[.[]
      | select((.guid | ascii_downcase) == ($guid | ascii_downcase))
      | .versions[]
      | select(.version == $version)]
     | length' "${manifest_path}")"
[[ "${version_entry_count}" == '1' ]] \
    || fail "Manifest must contain exactly one ${plugin_guid} version ${version} entry."

manifest_value() {
    local property="$1"
    jq --raw-output \
        --arg guid "${plugin_guid}" \
        --arg version "${version}" \
        --arg property "${property}" \
        '.[]
         | select((.guid | ascii_downcase) == ($guid | ascii_downcase))
         | .versions[]
         | select(.version == $version)
         | .[$property]' "${manifest_path}"
}

manifest_abi="$(manifest_value targetAbi)"
[[ "${manifest_abi}" == "${target_abi}" ]] \
    || fail "Manifest targetAbi ${manifest_abi} does not match build.yaml targetAbi ${target_abi}."
manifest_url="$(manifest_value sourceUrl)"
[[ "${manifest_url}" == "${asset_url}" ]] \
    || fail 'Manifest sourceUrl does not match the immutable release asset URL.'
expected_manifest_checksum="$(md5sum "${package_path}" | awk '{print $1}')"
manifest_checksum="$(manifest_value checksum)"
[[ "${manifest_checksum}" == "${expected_manifest_checksum}" ]] \
    || fail 'Manifest checksum does not match the release package.'

printf 'Verified tag, build, package, and manifest release contract for %s.\n' "${tag}"
