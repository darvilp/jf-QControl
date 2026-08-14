#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/../.." && pwd)"
environment_script="${project_root}/scripts/test-env.sh"
server_url="http://127.0.0.1:18180"
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
                >"${project_root}/.testenv/last-qbittorrent-mutation-failure.log" || true
    fi
    "${environment_script}" down >/dev/null 2>&1 || true
}
trap cleanup EXIT

"${environment_script}" reset --confirm
"${environment_script}" up
"${environment_script}" fixtures
api_key="$(<"${api_key_file}")"

api_get() {
    curl --fail --silent --show-error \
        --header "Authorization: Bearer ${api_key}" \
        "${server_url}/api/v2/$1"
}

api_post() {
    local endpoint="$1"
    shift
    curl --fail --silent --show-error \
        --output /dev/null \
        --header "Authorization: Bearer ${api_key}" \
        --request POST \
        "$@" \
        "${server_url}/api/v2/${endpoint}"
}

container_version="$(docker exec qcontrol-test-jellyfin /bin/sh -eu -c '
    api_key="$(cat /run/secrets/qbittorrent-api-key)"
    curl --fail --silent --header "Authorization: Bearer ${api_key}" \
        http://qbittorrent:18180/api/v2/app/version
')"
test "${container_version}" = 'v5.2.3'

test "$(api_get transfer/speedLimitsMode)" = '0'
api_post transfer/toggleSpeedLimitsMode
test "$(api_get transfer/speedLimitsMode)" = '1'
api_post transfer/toggleSpeedLimitsMode
test "$(api_get transfer/speedLimitsMode)" = '0'

torrents="$(api_get torrents/info)"
target_hash="$(jq --exit-status --raw-output \
    '.[] | select(.name == "incomplete-stopped.bin") | .hash' <<<"${torrents}")"

api_post torrents/createCategory \
    --data-urlencode 'category=TV Épisodes' \
    --data-urlencode 'savePath=/downloads'
api_post torrents/createTags --data-urlencode 'tags=fixture space,täg'
api_post torrents/setCategory \
    --data-urlencode "hashes=${target_hash}" \
    --data-urlencode 'category=TV Épisodes'
api_post torrents/addTags \
    --data-urlencode "hashes=${target_hash}" \
    --data-urlencode 'tags=fixture space,täg'

updated="$(api_get "torrents/info?hashes=${target_hash}")"
jq --exit-status '
    .[0].category == "TV Épisodes"
    and (.[0].tags | contains("fixture space"))
    and (.[0].tags | contains("täg"))
' <<<"${updated}" >/dev/null

api_post torrents/start --data-urlencode "hashes=${target_hash}"
started=false
for _ in {1..30}; do
    state="$(api_get "torrents/info?hashes=${target_hash}" | jq --raw-output '.[0].state')"
    if [[ "${state}" != stopped* ]]; then
        started=true
        break
    fi
    sleep 1
done
if [[ "${started}" != "true" ]]; then
    printf 'Explicit start did not leave the stopped state.\n' >&2
    exit 3
fi

api_post torrents/stop --data-urlencode "hashes=${target_hash}"
stopped=false
for _ in {1..30}; do
    state="$(api_get "torrents/info?hashes=${target_hash}" | jq --raw-output '.[0].state')"
    if [[ "${state}" == stopped* ]]; then
        stopped=true
        break
    fi
    sleep 1
done
if [[ "${stopped}" != "true" ]]; then
    printf 'Explicit stop did not reach a stopped state.\n' >&2
    exit 4
fi

api_post torrents/setCategory \
    --data-urlencode "hashes=${target_hash}" \
    --data-urlencode 'category=sonarr'
api_post torrents/removeTags \
    --data-urlencode "hashes=${target_hash}" \
    --data-urlencode 'tags=fixture space,täg'
api_post torrents/removeCategories --data-urlencode 'categories=TV Épisodes'
api_post torrents/deleteTags --data-urlencode 'tags=fixture space,täg'

restored="$(api_get "torrents/info?hashes=${target_hash}")"
jq --exit-status '
    .[0].category == "sonarr"
    and (.[0].tags | contains("fixture space") | not)
    and (.[0].tags | contains("täg") | not)
' <<<"${restored}" >/dev/null

completed=1
printf 'Verified qBittorrent mutations, Unicode names, and Jellyfin-network API-key access.\n'
