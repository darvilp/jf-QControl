#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"
jprm_environment="${project_root}/.testenv/jprm"
jprm_commit="9497a0a499416cc572ed2e07a391d9f943a37b4d"
plugin_version="$("${script_dir}/read-build-metadata.sh" version)"
plugin_framework="$("${script_dir}/read-build-metadata.sh" framework)"

if [[ ! -x "${jprm_environment}/bin/jprm" ]]; then
    python3 -m venv "${jprm_environment}"
    "${jprm_environment}/bin/python" -m pip install \
        --disable-pip-version-check \
        "git+https://github.com/oddstr13/jellyfin-plugin-repository-manager.git@${jprm_commit}"
fi

mkdir -p \
    "${project_root}/artifacts" \
    "${project_root}/.testenv/dotnet-home" \
    "${project_root}/.testenv/nuget"

artifact_path="$({
    DOTNET_CLI_HOME="${project_root}/.testenv/dotnet-home" \
    NUGET_PACKAGES="${project_root}/.testenv/nuget" \
        "${jprm_environment}/bin/jprm" \
        --verbosity=info \
        plugin build "${project_root}" \
        --output "${project_root}/artifacts" \
        --version "${plugin_version}" \
        --dotnet-configuration Release \
        --dotnet-framework "${plugin_framework}" \
        --max-cpu-count 1
} 2>&1 | tee /dev/stderr | tail -n 1)"

"${script_dir}/verify-package.sh" "${artifact_path}" >&2
printf '%s\n' "${artifact_path}"
