#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"

"${script_dir}/dotnet.sh" restore "${project_root}/Jellyfin.Plugin.QControl.sln"
"${script_dir}/dotnet.sh" build \
    "${project_root}/Jellyfin.Plugin.QControl.sln" \
    --configuration Release \
    --no-restore
"${script_dir}/dotnet.sh" test \
    "${project_root}/Jellyfin.Plugin.QControl.sln" \
    --configuration Release \
    --no-build \
    --no-restore

shellcheck "${project_root}"/scripts/*.sh \
    "${project_root}"/tests/fixtures/*.sh \
    "${project_root}"/tests/packaging/*.sh

"${project_root}/tests/fixtures/environment-contract.test.sh"
"${project_root}/tests/packaging/package-contract.test.sh"
"${project_root}/tests/fixtures/teardown-guard.test.sh"
"${project_root}/tests/fixtures/qbittorrent-compatibility.test.sh"
"${project_root}/tests/fixtures/qbittorrent-mutations.test.sh"
"${project_root}/tests/fixtures/jellyfin-session-snapshots.test.sh"
"${project_root}/tests/fixtures/jellyfin-compatibility.test.sh"
"${script_dir}/test-env.sh" reset --confirm

printf 'Issue 001 full gate passed.\n'
