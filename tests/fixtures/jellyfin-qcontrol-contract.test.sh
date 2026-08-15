#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/../.." && pwd)"
environment_script="${project_root}/scripts/test-env.sh"
server_url="http://127.0.0.1:18196"
qbit_url="http://127.0.0.1:18180/api/v2"
token_file="${project_root}/.testenv/jellyfin/access-token"
api_key_file="${project_root}/.testenv/secrets/qbittorrent-api-key"
completed=0
failed_line=0

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
                >"${project_root}/.testenv/last-jellyfin-qcontrol-failure.log" || true
    fi
    "${environment_script}" down >/dev/null 2>&1 || true
}
trap cleanup EXIT
trap 'failed_line=${LINENO}; printf "QControl contract failed at line %s.\n" "${failed_line}" >&2' ERR

api_status() {
    local method="$1"
    local endpoint="$2"
    local token="${3:-}"
    local body="${4:-}"
    local arguments=(--silent --output /dev/null --write-out '%{http_code}' --request "${method}")
    if [[ -n "${token}" ]]; then
        arguments+=(--header "X-Emby-Token: ${token}")
    fi
    if [[ -n "${body}" ]]; then
        arguments+=(--header 'Content-Type: application/json' --data "${body}")
    fi
    curl "${arguments[@]}" "${server_url}/${endpoint}"
}

authenticate_device() {
    local device_id="$1"
    local authorization="MediaBrowser Client=\"QControl Contract\", Device=\"Docker Fixture\", DeviceId=\"${device_id}\", Version=\"0.1.0.0\""
    curl --fail --silent --show-error \
        --request POST \
        --header "Authorization: ${authorization}" \
        --header 'Content-Type: application/json' \
        --data '{"Username":"qcontrol-admin","Pw":"qcontrol-local-only"}' \
        "${server_url}/Users/AuthenticateByName" \
        | jq --exit-status --raw-output .AccessToken
}

report_start() {
    local token="$1"
    curl --fail --silent --show-error \
        --output /dev/null \
        --request POST \
        --header "X-Emby-Token: ${token}" \
        --header 'Content-Type: application/json' \
        --data "{\"CanSeek\":true,\"ItemId\":\"${movie_id}\",\"IsPaused\":true,\"PositionTicks\":0,\"PlayMethod\":\"DirectPlay\",\"PlaySessionId\":\"qcontrol-contract-play\"}" \
        "${server_url}/Sessions/Playing"
}

report_stop() {
    local token="$1"
    curl --fail --silent --show-error \
        --output /dev/null \
        --request POST \
        --header "X-Emby-Token: ${token}" \
        --header 'Content-Type: application/json' \
        --data "{\"ItemId\":\"${movie_id}\",\"PositionTicks\":10000000,\"PlaySessionId\":\"qcontrol-contract-play\",\"Failed\":false}" \
        "${server_url}/Sessions/Playing/Stopped"
}

qbit_get() {
    local endpoint="$1"
    curl --fail --silent --show-error \
        --header "Authorization: Bearer ${api_key}" \
        "${qbit_url}/${endpoint}"
}

qbit_post() {
    local endpoint="$1"
    shift
    local arguments=(--fail --silent --show-error --request POST
        --header "Authorization: Bearer ${api_key}")
    local value
    for value in "$@"; do
        arguments+=(--data-urlencode "${value}")
    done
    curl "${arguments[@]}" "${qbit_url}/${endpoint}"
}

"${environment_script}" reset --confirm
"${environment_script}" up
"${project_root}/scripts/configure-test-server.sh"
"${environment_script}" fixtures
artifact_path="$("${project_root}/scripts/package.sh" | tail -n 1)"
"${project_root}/scripts/install-local-plugin.sh" "${artifact_path}"

admin_token="$(<"${token_file}")"
api_key="$(<"${api_key_file}")"
test "$(api_status GET QControl/Configuration)" = '401'

jellyfin_ip="$(docker inspect qcontrol-test-jellyfin \
    | jq --exit-status --raw-output \
        '.[0].NetworkSettings.Networks | to_entries[]
         | select(.key | endswith("_testnet")) | .value.IPAddress')"
test -n "${jellyfin_ip}"
bypass_preferences="$(jq --null-input \
    --arg subnet "${jellyfin_ip}/32" \
    '{bypass_auth_subnet_whitelist_enabled:true,bypass_auth_subnet_whitelist:$subnet}')"
qbit_post app/setPreferences --data-urlencode "json=${bypass_preferences}"
qbit_get app/preferences \
    | jq --exit-status \
        --arg subnet "${jellyfin_ip}/32" \
        '.bypass_auth_subnet_whitelist_enabled == true
         and .bypass_auth_subnet_whitelist == $subnet' >/dev/null

unauthenticated_candidate="$(jq --null-input \
    '{expectedRevision:0,qbittorrentBaseAddress:"http://qbittorrent:18180",credentialMode:2,secretFilePath:"",apiKeyReplacement:"",clearStoredApiKey:false,alternativeLimitsEnabled:false,stopTorrentsEnabled:false,stopScope:0,selectedCategories:[],includeIncomplete:true,includeCompleted:true,markerTag:"qcontrol-resume",exclusionTags:["qcontrol-ignore"],releaseGraceSeconds:1}')"
unauthenticated_test="$(curl --fail --silent --show-error \
    --request POST \
    --header "X-Emby-Token: ${admin_token}" \
    --header 'Content-Type: application/json' \
    --data "${unauthenticated_candidate}" \
    "${server_url}/QControl/Connection/Test")"
jq --exit-status \
    '(.isConnected // .IsConnected) == true
     and (.applicationVersion // .ApplicationVersion) == "5.2.3"' \
    <<<"${unauthenticated_test}" >/dev/null
qbit_post app/setPreferences \
    --data-urlencode 'json={"bypass_auth_subnet_whitelist_enabled":false,"bypass_auth_subnet_whitelist":""}'

regular_user="$(curl --fail --silent --show-error \
    --request POST \
    --header "X-Emby-Token: ${admin_token}" \
    --header 'Content-Type: application/json' \
    --data '{"Name":"qcontrol-regular"}' \
    "${server_url}/Users/New")"
regular_user_id="$(jq --exit-status --raw-output .Id <<<"${regular_user}")"
regular_authorization='MediaBrowser Client="QControl Contract", Device="Docker Fixture", DeviceId="qcontrol-regular", Version="0.1.0.0"'
regular_token="$(curl --fail --silent --show-error \
    --request POST \
    --header "Authorization: ${regular_authorization}" \
    --header 'Content-Type: application/json' \
    --data '{"Username":"qcontrol-regular","Pw":""}' \
    "${server_url}/Users/AuthenticateByName" \
    | jq --exit-status --raw-output .AccessToken)"
test -n "${regular_user_id}"

candidate="$(jq --null-input \
    '{expectedRevision:0,qbittorrentBaseAddress:"http://qbittorrent:18180",credentialMode:1,secretFilePath:"/run/secrets/qbittorrent-api-key",apiKeyReplacement:"",clearStoredApiKey:false,alternativeLimitsEnabled:true,stopTorrentsEnabled:true,stopScope:0,selectedCategories:[],includeIncomplete:true,includeCompleted:true,markerTag:"qcontrol-resume",exclusionTags:["qcontrol-ignore"],releaseGraceSeconds:1}')"

for request in \
    'GET QControl/Configuration' \
    'GET QControl/Status' \
    'GET QControl/Connection/Categories' \
    'PUT QControl/Configuration' \
    'POST QControl/Connection/Test' \
    'POST QControl/Recovery/ResumeMarkedTorrents' \
    'POST QControl/Recovery/RestorePreviousSpeedSetting' \
    'POST QControl/Recovery/MarkResolved'; do
    read -r method endpoint <<<"${request}"
    body=''
    if [[ "${method}" = 'PUT' || "${endpoint}" = 'QControl/Connection/Test' ]]; then
        body="${candidate}"
    fi
    test "$(api_status "${method}" "${endpoint}" "${regular_token}" "${body}")" = '403'
done

connection_test="$(curl --fail --silent --show-error \
    --request POST \
    --header "X-Emby-Token: ${admin_token}" \
    --header 'Content-Type: application/json' \
    --data "${candidate}" \
    "${server_url}/QControl/Connection/Test")"
jq --exit-status \
    '(.isConnected // .IsConnected) == true
     and (.applicationVersion // .ApplicationVersion) == "5.2.3"
     and ((.categories // .Categories) | index("radarr") != null)' \
    <<<"${connection_test}" >/dev/null

save_result="$(curl --fail --silent --show-error \
    --request PUT \
    --header "X-Emby-Token: ${admin_token}" \
    --header 'Content-Type: application/json' \
    --data "${candidate}" \
    "${server_url}/QControl/Configuration")"
if ! jq --exit-status \
    '((.outcome // .Outcome) | ascii_downcase) == "accepted"
     and ((.configuration // .Configuration).revision // (.configuration // .Configuration).Revision) == 1' \
    <<<"${save_result}" >/dev/null; then
    printf 'Unexpected credential-safe configuration save response: %s\n' \
        "$(jq --compact-output . <<<"${save_result}")" >&2
    exit 7
fi

safe_configuration="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${admin_token}" \
    "${server_url}/QControl/Configuration")"
jq --exit-status \
    '(.hasStoredApiKey // .HasStoredApiKey) == false
     and (.connectionValidated // .ConnectionValidated) == true' \
    <<<"${safe_configuration}" >/dev/null
if grep --fixed-strings --quiet 'qbt_' <<<"${safe_configuration}"; then
    printf 'Credential content escaped through the safe configuration API.\n' >&2
    exit 5
fi

category_result="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${admin_token}" \
    "${server_url}/QControl/Connection/Categories")"
jq --exit-status '((.categories // .Categories) | sort) == ["radarr", "sonarr"]' \
    <<<"${category_result}" >/dev/null

admin_items="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${admin_token}" \
    "${server_url}/Items?Recursive=true&IncludeItemTypes=Movie")"
movie_id="$(jq --exit-status --raw-output '.Items[0].Id' <<<"${admin_items}")"
player_token="$(authenticate_device qcontrol-contract-player)"

initial_torrents="$(qbit_get torrents/info)"
initial_limits="$(qbit_get transfer/speedLimitsMode)"
expected_hashes="$(jq --raw-output \
    '[.[] | select((.state | startswith("stopped") | not) and (.tags | contains("qcontrol-ignore") | not)) | .hash] | sort | .[]' \
    <<<"${initial_torrents}")"
test -n "${expected_hashes}"
initial_categories="$(jq --sort-keys '[.[] | {key:.hash,value:(.category // "")}] | from_entries' \
    <<<"${initial_torrents}")"

report_start "${player_token}"
protected=0
for _ in {1..30}; do
    current_torrents="$(qbit_get torrents/info)"
    if jq --exit-status \
        --argjson expected "$(jq --raw-input --slurp 'split("\n")[:-1]' <<<"${expected_hashes}")" \
        'all(.[]; . as $torrent | (($expected | index($torrent.hash)) == null)
            or (($torrent.state | startswith("stopped")) and ($torrent.tags | contains("qcontrol-resume"))))' \
        <<<"${current_torrents}" >/dev/null; then
        protected=1
        break
    fi
    sleep 1
done
test "${protected}" = '1'
test "$(qbit_get transfer/speedLimitsMode)" = '1'

active_status="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${admin_token}" \
    "${server_url}/QControl/Status")"
jq --exit-status \
    '((.protectionState // .ProtectionState) | ascii_downcase) == "protecting"
     and (.qualifyingSessionCount // .QualifyingSessionCount) == 1
     and ((.connectivity // .Connectivity) | ascii_downcase) == "connected"
     and (.alternativeLimitsActionEnabled // .AlternativeLimitsActionEnabled) == true
     and (.stopTorrentsActionEnabled // .StopTorrentsActionEnabled) == true' \
    <<<"${active_status}" >/dev/null

journal_path="${project_root}/.testenv/jellyfin/config/plugins/configurations/Jellyfin.Plugin.QControl.journal.json"
test -f "${journal_path}"
if grep --fixed-strings --quiet 'qbt_' "${journal_path}"; then
    printf 'Credential content escaped into the activation journal.\n' >&2
    exit 6
fi

report_stop "${player_token}"
restored=0
for _ in {1..30}; do
    current_torrents="$(qbit_get torrents/info)"
    if [[ ! -e "${journal_path}" ]] \
        && jq --exit-status \
            --argjson expected "$(jq --raw-input --slurp 'split("\n")[:-1]' <<<"${expected_hashes}")" \
            'all(.[]; . as $torrent | (($expected | index($torrent.hash)) == null)
                or (($torrent.state | startswith("stopped") | not) and ($torrent.tags | contains("qcontrol-resume") | not)))' \
            <<<"${current_torrents}" >/dev/null; then
        restored=1
        break
    fi
    sleep 1
done
test "${restored}" = '1'
test "$(qbit_get transfer/speedLimitsMode)" = "${initial_limits}"
current_categories="$(jq --sort-keys '[.[] | {key:.hash,value:(.category // "")}] | from_entries' \
    <<<"${current_torrents}")"
test "${current_categories}" = "${initial_categories}"

inactive_status="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${admin_token}" \
    "${server_url}/QControl/Status")"
jq --exit-status \
    '((.protectionState // .ProtectionState) | ascii_downcase) == "inactive"
     and (.qualifyingSessionCount // .QualifyingSessionCount) == 0
     and (.configurationChangesPending // .ConfigurationChangesPending) == false' \
    <<<"${inactive_status}" >/dev/null

manual_hash="$(jq --exit-status --raw-output \
    '[.[] | select(.state | startswith("stopped"))
        | select(.tags | contains("qcontrol-ignore") | not)][0].hash' \
    <<<"${current_torrents}")"
manual_category="$(jq --exit-status --raw-output --arg hash "${manual_hash}" \
    '.[] | select(.hash == $hash) | (.category // "")' \
    <<<"${current_torrents}")"
qbit_post torrents/addTags "hashes=${manual_hash}" 'tags=qcontrol-resume'

marker_status="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${admin_token}" \
    "${server_url}/QControl/Status")"
jq --exit-status \
    '((.protectionState // .ProtectionState) | ascii_downcase) == "inactive"
     and (.markedTorrentCount // .MarkedTorrentCount) >= 1
     and (.canResumeMarkedTorrents // .CanResumeMarkedTorrents) == true' \
    <<<"${marker_status}" >/dev/null

manual_recovery="$(curl --fail --silent --show-error \
    --request POST \
    --header "X-Emby-Token: ${admin_token}" \
    "${server_url}/QControl/Recovery/ResumeMarkedTorrents")"
jq --exit-status \
    '((.outcome // .Outcome) | ascii_downcase) == "completed"' \
    <<<"${manual_recovery}" >/dev/null

manual_restored=0
for _ in {1..30}; do
    manual_torrent="$(qbit_get torrents/info \
        | jq --exit-status --arg hash "${manual_hash}" '.[] | select(.hash == $hash)')"
    if [[ ! -e "${journal_path}" ]] \
        && jq --exit-status \
            '(.state | startswith("stopped") | not)
             and (.tags | contains("qcontrol-resume") | not)' \
            <<<"${manual_torrent}" >/dev/null; then
        manual_restored=1
        break
    fi
    sleep 1
done
test "${manual_restored}" = '1'
test "$(jq --raw-output '.category // ""' <<<"${manual_torrent}")" = "${manual_category}"

completed=1
printf 'Verified administrator APIs, playback protection, and explicit marked recovery through the packaged plugin.\n'
