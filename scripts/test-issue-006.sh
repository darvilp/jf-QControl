#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"

"${project_root}/scripts/test-issue-005.sh"
"${project_root}/tests/fixtures/qbittorrent-alternative-limits-contract.test.sh"
"${project_root}/scripts/test-env.sh" reset --confirm

printf 'Issue 006 full gate passed.\n'
