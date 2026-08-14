#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/../.." && pwd)"
verify_release="${project_root}/scripts/verify-release-contract.sh"
prepare_assets="${project_root}/scripts/prepare-release-assets.sh"
workflow_path="${project_root}/.github/workflows/release.yml"
read_build_metadata="${project_root}/scripts/read-build-metadata.sh"
temp_root="$(mktemp -d /tmp/qcontrol-release-test.XXXXXX)"
trap 'rm -rf -- "${temp_root}"' EXIT

version="$("${read_build_metadata}" version)"
tag="v${version}"
target_abi="$("${read_build_metadata}" targetAbi)"
asset_name="Jellyfin.Plugin.QControl_${version}.zip"
source_url="https://github.com/darvilp/jf-QControl/releases/download/${tag}/${asset_name}"
package_path="${temp_root}/${asset_name}"
staging="${temp_root}/package"

for required in \
    "${verify_release}" \
    "${prepare_assets}" \
    "${project_root}/scripts/test-manifest-install.sh" \
    "${project_root}/scripts/test-issue-010.sh"; do
    test -x "${required}"
done
test -f "${workflow_path}"
test -f "${project_root}/docs/RELEASE.md"
test -f "${project_root}/docs/releases/v${version}-alpha.md"

grep --fixed-strings 'workflow_dispatch:' "${workflow_path}" >/dev/null
grep --fixed-strings -- '- "v*.*.*.*"' "${workflow_path}" >/dev/null
grep --fixed-strings -- '--draft' "${workflow_path}" >/dev/null
grep --fixed-strings "if: github.event_name == 'push'" "${workflow_path}" >/dev/null
grep --fixed-strings 'group: qcontrol-release' "${workflow_path}" >/dev/null
if grep --extended-regexp --line-number 'uses: [^ ]+@(main|master|v[0-9]+)' "${workflow_path}"; then
    printf 'Release workflow actions must be pinned to immutable commit SHAs.\n' >&2
    exit 1
fi
if grep --extended-regexp --line-number -- \
    '--draft=false|gh release edit|git push|gh api.*releases.*PATCH' "${workflow_path}"; then
    printf 'Release workflow must not publish a draft or write a catalog branch.\n' >&2
    exit 1
fi

mkdir -p "${staging}"
printf 'test plugin assembly\n' >"${staging}/Jellyfin.Plugin.QControl.dll"
printf 'test domain assembly\n' >"${staging}/Jellyfin.Plugin.QControl.Domain.dll"
jq --null-input \
    --arg version "${version}" \
    --arg target_abi "${target_abi}" \
    '{
        name: "QControl",
        guid: "ab18c878-1856-4853-8f21-5028a1d5a7b2",
        version: $version,
        targetAbi: $target_abi
    }' >"${staging}/meta.json"
(
    cd -- "${staging}"
    zip --quiet "${package_path}" \
        Jellyfin.Plugin.QControl.Domain.dll \
        Jellyfin.Plugin.QControl.dll \
        meta.json
)

expect_failure() {
    local expected_message="$1"
    shift
    local output

    if output="$("$@" 2>&1)"; then
        printf 'Expected command to fail: %s\n' "$*" >&2
        exit 1
    fi
    grep --fixed-strings "${expected_message}" <<<"${output}" >/dev/null
}

"${verify_release}" "${tag}" "${package_path}"
expect_failure \
    'does not match build.yaml version' \
    "${verify_release}" 'v9.9.9.9' "${package_path}"

assets_dir="${temp_root}/assets"
"${prepare_assets}" "${tag}" "${package_path}" "${assets_dir}"
test -f "${assets_dir}/${asset_name}"
test -f "${assets_dir}/${asset_name}.sha256"
(
    cd -- "${assets_dir}"
    sha256sum --check "${asset_name}.sha256"
)
cmp "${package_path}" "${assets_dir}/${asset_name}"

package_md5="$(md5sum "${package_path}" | awk '{print $1}')"
manifest_path="${temp_root}/manifest.json"
jq --null-input \
    --arg version "${version}" \
    --arg target_abi "${target_abi}" \
    --arg source_url "${source_url}" \
    --arg checksum "${package_md5}" \
    '[{
        guid: "ab18c878-1856-4853-8f21-5028a1d5a7b2",
        name: "QControl",
        versions: [{
            version: $version,
            targetAbi: $target_abi,
            sourceUrl: $source_url,
            checksum: $checksum
        }]
    }]' >"${manifest_path}"

"${verify_release}" \
    "${tag}" \
    "${package_path}" \
    "${manifest_path}" \
    "${source_url}"

jq '.[0].versions[0].sourceUrl = "https://example.invalid/wrong.zip"' \
    "${manifest_path}" >"${temp_root}/wrong-manifest.json"
expect_failure \
    'Manifest sourceUrl does not match the immutable release asset URL.' \
    "${verify_release}" \
    "${tag}" \
    "${package_path}" \
    "${temp_root}/wrong-manifest.json" \
    "${source_url}"

printf 'Release tooling contract tests passed.\n'
