#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"

"${script_dir}/test-issue-009.sh"
"${project_root}/tests/release/release-tooling.test.sh"
"${project_root}/tests/fixtures/jellyfin-interruption-contract.test.sh"

artifact="$("${script_dir}/package.sh" | tail -n 1)"
"${script_dir}/test-manifest-install.sh" "${artifact}"

printf 'Issue 010 alpha release gate passed.\n'
