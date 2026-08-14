#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/../.." && pwd)"
environment_script="${project_root}/scripts/test-env.sh"
configure_script="${project_root}/scripts/configure-test-server.sh"
install_script="${project_root}/scripts/install-local-plugin.sh"
token_file="${project_root}/.testenv/jellyfin/access-token"
server_url="http://127.0.0.1:18196"
plugin_id="ab18c878-1856-4853-8f21-5028a1d5a7b2"
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
                >"${project_root}/.testenv/last-jellyfin-failure.log" || true
    fi
    "${environment_script}" down >/dev/null 2>&1 || true
}
trap cleanup EXIT

for required_script in "${configure_script}" "${install_script}"; do
    if [[ ! -x "${required_script}" ]]; then
        printf 'Required Jellyfin fixture entrypoint is missing: %s\n' "${required_script}" >&2
        exit 1
    fi
done

"${environment_script}" reset --confirm
"${environment_script}" up
"${configure_script}"
artifact_path="$("${project_root}/scripts/package.sh" | tail -n 1)"
"${install_script}" "${artifact_path}"

access_token="$(<"${token_file}")"
plugins="$(curl --fail --silent \
    --header "X-Emby-Token: ${access_token}" \
    "${server_url}/Plugins")"
jq --exit-status \
    --arg id "${plugin_id}" \
    '.[] | select((.Id | ascii_downcase | gsub("-"; "")) == ($id | gsub("-"; "")) and .Name == "QControl" and .Status == "Active")' \
    <<<"${plugins}" >/dev/null

configuration="$(curl --fail --silent \
    --header "X-Emby-Token: ${access_token}" \
    "${server_url}/Plugins/${plugin_id}/Configuration")"
jq --exit-status '(.SchemaVersion // .schemaVersion) == 1' <<<"${configuration}" >/dev/null

curl --fail --silent --show-error \
    --output /dev/null \
    --request POST \
    --header "X-Emby-Token: ${access_token}" \
    --header 'Content-Type: application/json' \
    --data '{"SchemaVersion":1}' \
    "${server_url}/Plugins/${plugin_id}/Configuration"
configuration_root="${project_root}/.testenv/jellyfin/config/plugins/configurations"
configuration_path="${configuration_root}/Jellyfin.Plugin.QControl.xml"
journal_path="${configuration_root}/Jellyfin.Plugin.QControl.journal.json"
test -f "${configuration_path}"
printf '%s\n' '{"schemaVersion":1,"phase":"compatibility-sentinel"}' >"${journal_path}"

docker compose \
    --project-name qcontrol-test \
    --project-directory "${project_root}" \
    --file "${project_root}/compose.yaml" \
    restart jellyfin >/dev/null
health_status=''
for _ in {1..45}; do
    health_status="$(docker inspect qcontrol-test-jellyfin \
        --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}')"
    if [[ "${health_status}" == "healthy" ]]; then
        break
    fi
    sleep 1
done
test "${health_status}" = 'healthy'
test -f "${configuration_path}"
test -f "${journal_path}"
find "${configuration_root}" \
    -maxdepth 1 \
    -type f \
    -name 'Jellyfin.Plugin.QControl.journal.json' \
    -delete

configuration="$(curl --fail --silent \
    --header "X-Emby-Token: ${access_token}" \
    "${server_url}/Plugins/${plugin_id}/Configuration")"
jq --exit-status '(.SchemaVersion // .schemaVersion) == 1' <<<"${configuration}" >/dev/null

page="$(curl --fail --silent --get \
    --header "X-Emby-Token: ${access_token}" \
    --data-urlencode 'name=QControl' \
    "${server_url}/web/ConfigurationPage")"
if [[ "${page}" != *'id="qControlPage"'* \
    || "${page}" != *'<h2 class="sectionTitle">QControl</h2>'* ]]; then
    printf 'Jellyfin did not serve the embedded QControl administrator placeholder.\n' >&2
    exit 2
fi

completed=1
printf 'Verified the packaged QControl plugin on Jellyfin 10.11.11.\n'
