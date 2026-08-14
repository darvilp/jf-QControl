#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/../.." && pwd)"
environment_script="${project_root}/scripts/test-env.sh"
server_url="http://127.0.0.1:18196"
qbit_url="http://127.0.0.1:18180/api/v2"
token_file="${project_root}/.testenv/jellyfin/access-token"
api_key_file="${project_root}/.testenv/secrets/qbittorrent-api-key"
journal_path="${project_root}/.testenv/jellyfin/config/plugins/configurations/Jellyfin.Plugin.QControl.journal.json"
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
    if [[ "${completed}" -ne 1 ]]; then
        mkdir -p "${project_root}/.testenv"
        compose logs --no-color 2>&1 \
            | sed --regexp-extended \
                's/(temporary password is provided for this session: )[[:graph:]]+/\1[REDACTED]/' \
                >"${project_root}/.testenv/last-jellyfin-interruption-failure.log" || true
    fi
    "${environment_script}" down >/dev/null 2>&1 || true
}
trap cleanup EXIT
trap 'printf "Interruption contract failed at line %s.\n" "${LINENO}" >&2' ERR

authenticate_device() {
    local device_id="$1"
    local authorization="MediaBrowser Client=\"QControl Interruption Contract\", Device=\"Docker Fixture\", DeviceId=\"${device_id}\", Version=\"0.1.0.0\""
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
    local play_id="$2"
    curl --fail --silent --show-error \
        --output /dev/null \
        --request POST \
        --header "X-Emby-Token: ${token}" \
        --header 'Content-Type: application/json' \
        --data "{\"CanSeek\":true,\"ItemId\":\"${movie_id}\",\"IsPaused\":true,\"PositionTicks\":0,\"PlayMethod\":\"DirectPlay\",\"PlaySessionId\":\"${play_id}\"}" \
        "${server_url}/Sessions/Playing"
}

report_stop() {
    local token="$1"
    local play_id="$2"
    curl --fail --silent --show-error \
        --output /dev/null \
        --request POST \
        --header "X-Emby-Token: ${token}" \
        --header 'Content-Type: application/json' \
        --data "{\"ItemId\":\"${movie_id}\",\"PositionTicks\":10000000,\"PlaySessionId\":\"${play_id}\",\"Failed\":false}" \
        "${server_url}/Sessions/Playing/Stopped"
}

qbit_get() {
    local endpoint="$1"
    curl --fail --silent --show-error \
        --header "Authorization: Bearer ${api_key}" \
        "${qbit_url}/${endpoint}"
}

admin_get() {
    local endpoint="$1"
    curl --fail --silent --show-error \
        --header "X-Emby-Token: ${admin_token}" \
        "${server_url}/${endpoint}"
}

admin_post() {
    local endpoint="$1"
    curl --fail --silent --show-error \
        --request POST \
        --header "X-Emby-Token: ${admin_token}" \
        "${server_url}/${endpoint}"
}

wait_healthy() {
    local container="$1"
    local health=''
    for _ in {1..60}; do
        health="$(docker inspect "${container}" \
            --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}')"
        [[ "${health}" == 'healthy' ]] && return 0
        sleep 1
    done
    printf '%s did not become healthy; final state: %s.\n' "${container}" "${health}" >&2
    return 1
}

all_expected_protected() {
    local torrents
    torrents="$(qbit_get torrents/info)" || return 1
    jq --exit-status \
        --argjson expected "${expected_hashes_json}" \
        'all(.[]; . as $torrent | (($expected | index($torrent.hash)) == null)
            or (($torrent.state | startswith("stopped"))
                and ($torrent.tags | split(", ") | index("jfStopped") != null)))' \
        <<<"${torrents}" >/dev/null
}

all_expected_restored() {
    local torrents
    torrents="$(qbit_get torrents/info)" || return 1
    jq --exit-status \
        --argjson expected "${expected_hashes_json}" \
        'all(.[]; . as $torrent | (($expected | index($torrent.hash)) == null)
            or (($torrent.state | startswith("stopped") | not)
                and ($torrent.tags | split(", ") | index("jfStopped") == null)))' \
        <<<"${torrents}" >/dev/null
}

wait_for_protection() {
    for _ in {1..60}; do
        if all_expected_protected && [[ "$(qbit_get transfer/speedLimitsMode)" == '1' ]]; then
            return 0
        fi
        sleep 1
    done
    return 1
}

wait_for_restoration() {
    for _ in {1..60}; do
        if [[ ! -e "${journal_path}" ]] \
            && all_expected_restored \
            && [[ "$(qbit_get transfer/speedLimitsMode)" == "${initial_limits}" ]]; then
            return 0
        fi
        sleep 1
    done
    return 1
}

"${environment_script}" reset --confirm
"${environment_script}" up
"${project_root}/scripts/configure-test-server.sh"
"${environment_script}" fixtures
artifact_path="$("${project_root}/scripts/package.sh" | tail -n 1)"
"${project_root}/scripts/install-local-plugin.sh" "${artifact_path}"

admin_token="$(<"${token_file}")"
api_key="$(<"${api_key_file}")"
admin_items="$(admin_get 'Items?Recursive=true&IncludeItemTypes=Movie')"
movie_id="$(jq --exit-status --raw-output '.Items[0].Id' <<<"${admin_items}")"

candidate="$(jq --null-input \
    '{expectedRevision:0,qbittorrentBaseAddress:"http://qbittorrent:18180",credentialMode:1,secretFilePath:"/run/secrets/qbittorrent-api-key",apiKeyReplacement:"",clearStoredApiKey:false,alternativeLimitsEnabled:true,stopTorrentsEnabled:true,stopScope:0,selectedCategories:[],includeIncomplete:true,includeCompleted:true,markerTag:"jfStopped",neverTouchTag:"jfNeverTouch",releaseGraceSeconds:5}')"
curl --fail --silent --show-error \
    --request POST \
    --header "X-Emby-Token: ${admin_token}" \
    --header 'Content-Type: application/json' \
    --data "${candidate}" \
    "${server_url}/QControl/Connection/Test" \
    | jq --exit-status '(.isConnected // .IsConnected) == true' >/dev/null
curl --fail --silent --show-error \
    --request PUT \
    --header "X-Emby-Token: ${admin_token}" \
    --header 'Content-Type: application/json' \
    --data "${candidate}" \
    "${server_url}/QControl/Configuration" \
    | jq --exit-status '((.outcome // .Outcome) | ascii_downcase) == "accepted"' >/dev/null

initial_torrents="$(qbit_get torrents/info)"
initial_limits="$(qbit_get transfer/speedLimitsMode)"
expected_hashes_json="$(jq \
    '[.[]
      | select((.state | startswith("stopped") | not)
          and (.tags | split(", ") | index("jfNeverTouch") == null))
      | .hash]
     | sort' <<<"${initial_torrents}")"
test "$(jq length <<<"${expected_hashes_json}")" -gt 0
initial_categories="$(jq --sort-keys \
    '[.[] | {key:.hash,value:(.category // "")}] | from_entries' \
    <<<"${initial_torrents}")"

first_player="$(authenticate_device qcontrol-interruption-player)"
report_start "${first_player}" qcontrol-interruption-play
wait_for_protection
test -f "${journal_path}"

docker kill --signal KILL qcontrol-test-jellyfin >/dev/null
compose start jellyfin >/dev/null
wait_healthy qcontrol-test-jellyfin

interrupted_status="$(admin_get QControl/Status)"
jq --exit-status \
    '((.protectionState // .ProtectionState) | ascii_downcase) == "recoveryrequired"
     and (.qualifyingSessionCount // .QualifyingSessionCount) == 0
     and (.canResumeMarkedTorrents // .CanResumeMarkedTorrents) == true
     and (.canRestorePreviousSpeedSetting // .CanRestorePreviousSpeedSetting) == true' \
    <<<"${interrupted_status}" >/dev/null
sleep 7
test -f "${journal_path}"
all_expected_protected
test "$(qbit_get transfer/speedLimitsMode)" = '1'

admin_post QControl/Recovery/RestorePreviousSpeedSetting \
    | jq --exit-status '((.outcome // .Outcome) | ascii_downcase) == "completed"' >/dev/null
test "$(qbit_get transfer/speedLimitsMode)" = "${initial_limits}"
test -f "${journal_path}"
admin_post QControl/Recovery/ResumeMarkedTorrents \
    | jq --exit-status '((.outcome // .Outcome) | ascii_downcase) == "completed"' >/dev/null
wait_for_restoration

restored_categories="$(qbit_get torrents/info \
    | jq --sort-keys '[.[] | {key:.hash,value:(.category // "")}] | from_entries')"
test "${restored_categories}" = "${initial_categories}"

second_player="$(authenticate_device qcontrol-outage-player)"
compose stop qbittorrent >/dev/null
report_start "${second_player}" qcontrol-outage-play
for _ in {1..20}; do
    [[ -f "${journal_path}" ]] && break
    sleep 1
done
test -f "${journal_path}"
outage_status="$(admin_get QControl/Status)"
jq --exit-status \
    '((.protectionState // .ProtectionState) | ascii_downcase) == "protecting"
     and ((.connectivity // .Connectivity) | ascii_downcase) == "failed"
     and (.qualifyingSessionCount // .QualifyingSessionCount) == 1' \
    <<<"${outage_status}" >/dev/null

compose start qbittorrent >/dev/null
wait_healthy qcontrol-test-qbittorrent
wait_for_protection

report_stop "${second_player}" qcontrol-outage-play
release_pending=0
for _ in {1..20}; do
    status="$(admin_get QControl/Status)"
    if jq --exit-status \
        '((.protectionState // .ProtectionState) | ascii_downcase) == "releasepending"' \
        <<<"${status}" >/dev/null; then
        release_pending=1
        break
    fi
    sleep 0.2
done
test "${release_pending}" = '1'
compose stop qbittorrent >/dev/null
sleep 7
test -f "${journal_path}"
restoration_outage_status="$(admin_get QControl/Status)"
jq --exit-status \
    '((.protectionState // .ProtectionState) | ascii_downcase) == "restoring"
     and ((.connectivity // .Connectivity) | ascii_downcase) == "failed"
     and (.qualifyingSessionCount // .QualifyingSessionCount) == 0' \
    <<<"${restoration_outage_status}" >/dev/null

compose start qbittorrent >/dev/null
wait_healthy qcontrol-test-qbittorrent
wait_for_restoration
final_categories="$(qbit_get torrents/info \
    | jq --sort-keys '[.[] | {key:.hash,value:(.category // "")}] | from_entries')"
test "${final_categories}" = "${initial_categories}"

completed=1
printf 'Verified hard-interruption recovery and qBittorrent acquisition/restoration outage retries.\n'
