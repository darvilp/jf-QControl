#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"

"${project_root}/scripts/test-issue-003.sh"

printf 'Issue 004 full gate passed.\n'
