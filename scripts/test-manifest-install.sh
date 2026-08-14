#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"
environment_script="${script_dir}/test-env.sh"
server_url="http://127.0.0.1:18196"
token_file="${project_root}/.testenv/jellyfin/access-token"
jprm="${JPRM_BIN:-${project_root}/.testenv/jprm/bin/jprm}"
plugin_version="$("${script_dir}/read-build-metadata.sh" version)"
target_abi="$("${script_dir}/read-build-metadata.sh" targetAbi)"
artifact="${1:-}"
plugin_id="ab18c878-1856-4853-8f21-5028a1d5a7b2"
manifest_relative="qcontrol-manifest"
manifest_dir="${project_root}/.testenv/qbittorrent-fixtures/webseed/${manifest_relative}"
manifest_base_url="http://webseed/${manifest_relative}"
manifest_url="${manifest_base_url}/manifest.json"
original_repositories=''
access_token=''
completed=0

compose() {
    docker compose \
        --project-name qcontrol-test \
        --project-directory "${project_root}" \
        --file "${project_root}/compose.yaml" \
        "$@"
}

cleanup() {
    set +e
    if [[ -n "${original_repositories}" && -n "${access_token}" ]]; then
        curl --silent --request POST \
            --header "X-Emby-Token: ${access_token}" \
            --header 'Content-Type: application/json' \
            --data "${original_repositories}" \
            "${server_url}/Repositories" >/dev/null
    fi
    if [[ "${completed}" -ne 1 ]]; then
        compose logs --no-color 2>&1 \
            | sed --regexp-extended \
                's/(temporary password is provided for this session: )[[:graph:]]+/\1[REDACTED]/' \
                >"${project_root}/.testenv/last-manifest-install-failure.log" || true
    fi
    "${environment_script}" down >/dev/null 2>&1 || true
}
trap cleanup EXIT
trap 'printf "Manifest installation failed at line %s.\n" "${LINENO}" >&2' ERR

"${environment_script}" reset --confirm
"${environment_script}" up
"${script_dir}/configure-test-server.sh"
if [[ -z "${artifact}" ]]; then
    artifact="$("${script_dir}/package.sh" | tail -n 1)"
fi
"${script_dir}/verify-package.sh" "${artifact}"
if [[ ! -x "${jprm}" ]]; then
    printf 'JPRM is unavailable after package preparation: %s\n' "${jprm}" >&2
    exit 2
fi

access_token="$(<"${token_file}")"
if [[ "${manifest_dir}" != "${project_root}/.testenv/qbittorrent-fixtures/webseed/qcontrol-manifest" ]]; then
    printf 'Refusing to replace unexpected manifest fixture path: %s\n' "${manifest_dir}" >&2
    exit 3
fi
mkdir -p "${manifest_dir}"
find "${manifest_dir}" -mindepth 1 -delete
"${jprm}" repo init "${manifest_dir}"
"${jprm}" repo add \
    --url "${manifest_base_url}" \
    "${manifest_dir}" \
    "${artifact}"

compose exec -T jellyfin curl --fail --silent "${manifest_url}" >/dev/null
original_repositories="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${access_token}" \
    "${server_url}/Repositories")"
temporary_repository="$(jq --arg url "${manifest_url}" \
    '[{"Name":"QControl Temporary","Url":$url,"Enabled":true}]' \
    <<<"${original_repositories}")"
curl --fail --silent --show-error \
    --request POST \
    --header "X-Emby-Token: ${access_token}" \
    --header 'Content-Type: application/json' \
    --data "${temporary_repository}" \
    "${server_url}/Repositories" >/dev/null

packages="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${access_token}" \
    "${server_url}/Packages")"
jq --exit-status \
    --arg plugin_id "${plugin_id}" \
    --arg version "${plugin_version}" \
    --arg target_abi "${target_abi}" \
    'any(.[];
      ((.guid | ascii_downcase | gsub("-"; "")) == ($plugin_id | gsub("-"; "")))
      and any(.versions[]; .version == $version and .targetAbi == $target_abi))' \
    <<<"${packages}" >/dev/null

plugins_before="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${access_token}" \
    "${server_url}/Plugins")"
if jq --exit-status \
    --arg plugin_id "${plugin_id}" \
    'any(.[]; ((.Id | ascii_downcase | gsub("-"; "")) == ($plugin_id | gsub("-"; ""))))' \
    <<<"${plugins_before}" >/dev/null; then
    printf 'Cannot prove clean catalog installation: QControl is already installed.\n' >&2
    exit 4
fi

curl --fail --silent --show-error \
    --get \
    --request POST \
    --header "X-Emby-Token: ${access_token}" \
    --data-urlencode "assemblyGuid=${plugin_id}" \
    --data-urlencode "version=${plugin_version}" \
    --data-urlencode "repositoryUrl=${manifest_url}" \
    "${server_url}/Packages/Installed/QControl" >/dev/null

compose restart jellyfin >/dev/null
for _ in {1..45}; do
    health="$(docker inspect qcontrol-test-jellyfin \
        --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}')"
    [[ "${health}" == 'healthy' ]] && break
    sleep 1
done
[[ "${health:-}" == 'healthy' ]]

plugins="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${access_token}" \
    "${server_url}/Plugins")"
jq --exit-status \
    --arg plugin_id "${plugin_id}" \
    --arg version "${plugin_version}" \
    'any(.[];
      ((.Id | ascii_downcase | gsub("-"; "")) == ($plugin_id | gsub("-"; "")))
      and .Version == $version
      and .Status == "Active")' \
    <<<"${plugins}" >/dev/null

candidate='{"expectedRevision":0,"qbittorrentBaseAddress":"","credentialMode":0,"secretFilePath":"","apiKeyReplacement":"","clearStoredApiKey":false,"alternativeLimitsEnabled":false,"stopTorrentsEnabled":false,"stopScope":0,"selectedCategories":[],"includeIncomplete":true,"includeCompleted":true,"markerTag":"jfManifestStopped","neverTouchTag":"jfManifestNeverTouch","releaseGraceSeconds":60}'
save_result="$(curl --fail --silent --show-error \
    --request PUT \
    --header "X-Emby-Token: ${access_token}" \
    --header 'Content-Type: application/json' \
    --data "${candidate}" \
    "${server_url}/QControl/Configuration")"
jq --exit-status \
    '((.outcome // .Outcome) | ascii_downcase) == "accepted"' \
    <<<"${save_result}" >/dev/null

compose restart jellyfin >/dev/null
for _ in {1..45}; do
    health="$(docker inspect qcontrol-test-jellyfin \
        --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}')"
    [[ "${health}" == 'healthy' ]] && break
    sleep 1
done
[[ "${health:-}" == 'healthy' ]]

retained="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${access_token}" \
    "${server_url}/QControl/Configuration")"
jq --exit-status \
    '(.revision // .Revision) == 1
     and (.markerTag // .MarkerTag) == "jfManifestStopped"
     and (.neverTouchTag // .NeverTouchTag) == "jfManifestNeverTouch"' \
    <<<"${retained}" >/dev/null
if grep --fixed-strings --quiet 'qbt_' <<<"${retained}"; then
    printf 'Credential content appeared after manifest-install restart.\n' >&2
    exit 5
fi

completed=1
printf 'Loaded QControl %s from a temporary manifest and retained configuration across restart.\n' \
    "${plugin_version}"
