#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"

node --check --experimental-default-type=module \
    "${project_root}/Jellyfin.Plugin.QControl/Configuration/configPage.js"
node --test --experimental-default-type=module \
    "${project_root}/tests/ui/configPage.test.js"
"${project_root}/scripts/test-issue-008.sh"
"${project_root}/tests/fixtures/jellyfin-dashboard-contract.test.sh"
"${project_root}/scripts/test-browser-e2e.sh"
"${project_root}/scripts/test-env.sh" reset --confirm

printf 'Issue 009 full gate passed.\n'
