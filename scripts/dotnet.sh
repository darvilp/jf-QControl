#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"

mkdir -p "${project_root}/.testenv/dotnet-home" "${project_root}/.testenv/nuget"

DOTNET_CLI_HOME="${project_root}/.testenv/dotnet-home" \
NUGET_PACKAGES="${project_root}/.testenv/nuget" \
    dotnet "$@"
