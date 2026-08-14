#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"

"${project_root}/scripts/test-issue-001.sh"
"${project_root}/tests/fixtures/qbittorrent-client-contract.test.sh"
"${project_root}/scripts/test-env.sh" reset --confirm

printf 'Issue 003 full gate passed.\n'
