#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/../.." && pwd)"
environment_script="${project_root}/scripts/test-env.sh"
bootstrap_script="${project_root}/scripts/bootstrap-qbittorrent.sh"
setup_script="${project_root}/scripts/setup-qbittorrent-fixtures.sh"
probe_script="${project_root}/scripts/probe-qbittorrent.sh"
api_key_file="${project_root}/.testenv/secrets/qbittorrent-api-key"
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
                >"${project_root}/.testenv/last-qbittorrent-failure.log" || true
    fi
    "${environment_script}" down >/dev/null 2>&1 || true
}
trap cleanup EXIT

for required_script in "${bootstrap_script}" "${setup_script}" "${probe_script}"; do
    if [[ ! -x "${required_script}" ]]; then
        printf 'Required qBittorrent fixture entrypoint is missing: %s\n' "${required_script}" >&2
        exit 1
    fi
done

"${environment_script}" reset --confirm
"${environment_script}" up
"${environment_script}" fixtures
probe="$(${probe_script} --json)"

jq --exit-status '
    .applicationVersion == "v5.2.3"
    and (.webApiVersion | startswith("2."))
    and .alternativeSpeedLimits == false
    and (.categories | sort == ["radarr", "sonarr"])
    and (.tags | sort == ["fixture", "jfNeverTouch"])
    and (.torrents | length == 6)
    and ([.torrents[] | select(.name == "complete-seeding.bin" and .progress == 1 and (.state | startswith("stopped") | not))] | length == 1)
    and ([.torrents[] | select(.name == "complete-stopped.bin" and .progress == 1 and (.state | startswith("stopped")))] | length == 1)
    and ([.torrents[] | select(.name == "incomplete-stopped.bin" and .progress < 1 and (.state | startswith("stopped")))] | length == 1)
    and ([.torrents[] | select(.name == "incomplete-stalled.bin" and .progress < 1 and .state == "stalledDL")] | length == 1)
    and ([.torrents[] | select(.name == "incomplete-downloading.bin" and .progress > 0 and .progress < 1 and .state == "downloading")] | length == 1)
    and ([.torrents[] | select(.name == "incomplete-queued.bin" and .progress < 1 and .state == "queuedDL")] | length == 1)
' <<<"${probe}" >/dev/null

test "$(stat --format '%a' "${api_key_file}")" = "600"
api_key="$(<"${api_key_file}")"
[[ "${api_key}" =~ ^qbt_[A-Za-z0-9]{28}$ ]]

completed=1
printf 'Verified qBittorrent 5.2 API-key and torrent-state compatibility.\n'
