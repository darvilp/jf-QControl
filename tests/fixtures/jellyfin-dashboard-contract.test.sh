#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/../.." && pwd)"
environment_script="${project_root}/scripts/test-env.sh"
server_url="http://127.0.0.1:18196"
token_file="${project_root}/.testenv/jellyfin/access-token"
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
                >"${project_root}/.testenv/last-jellyfin-dashboard-failure.log" || true
    fi
    "${environment_script}" down >/dev/null 2>&1 || true
}
trap cleanup EXIT
trap 'printf "Jellyfin dashboard contract failed at line %s.\n" "${LINENO}" >&2' ERR

"${environment_script}" reset --confirm
"${environment_script}" up
"${project_root}/scripts/configure-test-server.sh"
artifact_path="$("${project_root}/scripts/package.sh" | tail -n 1)"
"${project_root}/scripts/install-local-plugin.sh" "${artifact_path}"

access_token="$(<"${token_file}")"
configuration_page="$(curl --fail --silent --show-error --get \
    --header "X-Emby-Token: ${access_token}" \
    --data-urlencode 'name=QControl' \
    "${server_url}/web/ConfigurationPage")"
for required in \
    'data-controller="__plugin/QControl.js"' \
    'id="qControlOperationalStatus"' \
    'id="qControlConnection"' \
    'id="qControlProtection"' \
    'id="qControlRecovery"' \
    'id="qControlRecoveryDialog"' \
    'aria-live="polite"' \
    '@media (max-width: 52rem)'; do
    if [[ "${configuration_page}" != *"${required}"* ]]; then
        printf 'The served administrator page is missing: %s\n' "${required}" >&2
        exit 2
    fi
done
if grep --ignore-case --quiet 'value="qbt_' <<<"${configuration_page}"; then
    printf 'Credential content appeared in the served administrator page.\n' >&2
    exit 3
fi

controller="$(curl --fail --silent --show-error --get \
    --header "X-Emby-Token: ${access_token}" \
    --data-urlencode 'name=QControl.js' \
    "${server_url}/web/ConfigurationPage")"
for required in \
    'export default function createPageController' \
    'QControl/Configuration' \
    'QControl/Connection/Test' \
    'QControl/Status' \
    'QControl/Recovery/ResumeMarkedTorrents' \
    'createAnnouncementGate'; do
    if [[ "${controller}" != *"${required}"* ]]; then
        printf 'The served thin controller is missing: %s\n' "${required}" >&2
        exit 4
    fi
done

safe_configuration="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${access_token}" \
    "${server_url}/QControl/Configuration")"
status="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${access_token}" \
    "${server_url}/QControl/Status")"
jq --exit-status \
    'has("Revision") or has("revision")' <<<"${safe_configuration}" >/dev/null
jq --exit-status \
    'has("ProtectionState") or has("protectionState")' <<<"${status}" >/dev/null
if grep --fixed-strings --quiet 'qbt_' <<<"${safe_configuration}${status}"; then
    printf 'Credential content appeared in a dashboard server response.\n' >&2
    exit 5
fi

completed=1
printf 'Verified the responsive QControl page and thin controller served by Jellyfin 10.11.11.\n'
