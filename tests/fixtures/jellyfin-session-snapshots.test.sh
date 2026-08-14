#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/../.." && pwd)"
environment_script="${project_root}/scripts/test-env.sh"
server_url="http://127.0.0.1:18196"
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
                >"${project_root}/.testenv/last-jellyfin-session-failure.log" || true
    fi
    "${environment_script}" down >/dev/null 2>&1 || true
}
trap cleanup EXIT

authenticate_device() {
    local device_id="$1"
    local authorization="MediaBrowser Client=\"QControl Session Probe\", Device=\"Docker Fixture\", DeviceId=\"${device_id}\", Version=\"0.1.0.0\""
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
    local play_session_id="$2"
    curl --fail --silent --show-error \
        --output /dev/null \
        --request POST \
        --header "X-Emby-Token: ${token}" \
        --header 'Content-Type: application/json' \
        --data "{\"CanSeek\":true,\"ItemId\":\"${movie_id}\",\"IsPaused\":false,\"IsMuted\":false,\"PositionTicks\":0,\"PlayMethod\":\"DirectPlay\",\"PlaySessionId\":\"${play_session_id}\"}" \
        "${server_url}/Sessions/Playing"
}

report_progress() {
    local token="$1"
    local play_session_id="$2"
    local paused="$3"
    curl --fail --silent --show-error \
        --output /dev/null \
        --request POST \
        --header "X-Emby-Token: ${token}" \
        --header 'Content-Type: application/json' \
        --data "{\"CanSeek\":true,\"ItemId\":\"${movie_id}\",\"IsPaused\":${paused},\"IsMuted\":false,\"PositionTicks\":10000000,\"PlayMethod\":\"DirectPlay\",\"PlaySessionId\":\"${play_session_id}\"}" \
        "${server_url}/Sessions/Playing/Progress"
}

report_stop() {
    local token="$1"
    local play_session_id="$2"
    curl --fail --silent --show-error \
        --output /dev/null \
        --request POST \
        --header "X-Emby-Token: ${token}" \
        --header 'Content-Type: application/json' \
        --data "{\"ItemId\":\"${movie_id}\",\"PositionTicks\":10000000,\"PlaySessionId\":\"${play_session_id}\",\"Failed\":false}" \
        "${server_url}/Sessions/Playing/Stopped"
}

"${environment_script}" reset --confirm
"${environment_script}" up
"${project_root}/scripts/configure-test-server.sh"
admin_token="$(<"${project_root}/.testenv/jellyfin/access-token")"
items="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${admin_token}" \
    "${server_url}/Items?Recursive=true&IncludeItemTypes=Movie")"
movie_id="$(jq --exit-status --raw-output '.Items[0].Id' <<<"${items}")"

device_a='qcontrol-player-a'
device_b='qcontrol-player-b'
token_a="$(authenticate_device "${device_a}")"
token_b="$(authenticate_device "${device_b}")"
play_a='qcontrol-play-a'
play_b='qcontrol-play-b'

report_start "${token_a}" "${play_a}"
report_start "${token_b}" "${play_b}"
report_progress "${token_b}" "${play_b}" true

sessions="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${admin_token}" \
    "${server_url}/Sessions")"
jq --exit-status \
    --arg first "${device_a}" \
    --arg second "${device_b}" \
    --arg item "${movie_id}" \
    '([.[] | select(.DeviceId == $first and .NowPlayingItem.Id == $item and .PlayState.IsPaused == false)] | length == 1)
     and ([.[] | select(.DeviceId == $second and .NowPlayingItem.Id == $item and .PlayState.IsPaused == true)] | length == 1)' \
    <<<"${sessions}" >/dev/null

report_stop "${token_a}" "${play_a}"
sessions="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${admin_token}" \
    "${server_url}/Sessions")"
jq --exit-status \
    --arg first "${device_a}" \
    --arg second "${device_b}" \
    '([.[] | select(.DeviceId == $first and .NowPlayingItem != null)] | length == 0)
     and ([.[] | select(.DeviceId == $second and .NowPlayingItem != null)] | length == 1)' \
    <<<"${sessions}" >/dev/null

report_stop "${token_b}" "${play_b}"
curl --fail --silent --show-error \
    --output /dev/null \
    --request POST \
    --header "X-Emby-Token: ${token_b}" \
    "${server_url}/Sessions/Logout"

sessions="$(curl --fail --silent --show-error \
    --header "X-Emby-Token: ${admin_token}" \
    "${server_url}/Sessions")"
jq --exit-status \
    --arg second "${device_b}" \
    '([.[] | select(.DeviceId == $second)] | length == 0)' \
    <<<"${sessions}" >/dev/null

completed=1
printf 'Verified playing, paused, stopped, disconnected, and overlapping Jellyfin snapshots.\n'
