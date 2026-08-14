#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/../.." && pwd)"
environment_script="${project_root}/scripts/test-env.sh"
secret_file="${project_root}/.testenv/secrets/qbittorrent-api-key"
probe_project="${project_root}/tests/Jellyfin.Plugin.QControl.ActionContractProbe/Jellyfin.Plugin.QControl.ActionContractProbe.csproj"
completed=0

cleanup() {
    if [[ "${completed}" -ne 1 ]]; then
        mkdir -p "${project_root}/.testenv"
        docker compose \
            --project-name qcontrol-test \
            --project-directory "${project_root}" \
            --file "${project_root}/compose.yaml" \
            logs --no-color 2>&1 \
            | sed --regexp-extended \
                's/(temporary password is provided for this session: )[[:graph:]]+/\1[REDACTED]/' \
                >"${project_root}/.testenv/last-qbittorrent-action-failure.log" || true
    fi
    "${environment_script}" down >/dev/null 2>&1 || true
}
trap cleanup EXIT

"${environment_script}" reset --confirm
"${environment_script}" up
"${environment_script}" fixtures

"${project_root}/scripts/dotnet.sh" run \
    --project "${probe_project}" \
    --configuration Release \
    -- \
    http://127.0.0.1:18180 \
    "${secret_file}"

completed=1
printf 'Verified journaled Stop Torrents protection and restoration against qBittorrent.\n'
