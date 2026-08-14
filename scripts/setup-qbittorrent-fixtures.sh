#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"
server_url="http://127.0.0.1:18180"
api_key_file="${project_root}/.testenv/secrets/qbittorrent-api-key"
fixture_root="${project_root}/.testenv/qbittorrent-fixtures"
download_root="${project_root}/.testenv/qbittorrent/downloads"
fixture_names=(
    complete-seeding.bin
    complete-stopped.bin
    incomplete-stopped.bin
    incomplete-stalled.bin
    incomplete-downloading.bin
    incomplete-queued.bin
)

if [[ ! -s "${api_key_file}" ]]; then
    printf 'qBittorrent fixture API key is missing.\n' >&2
    exit 2
fi
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

wait_for_hash() {
    local torrent_name="$1"
    local torrents
    local torrent_hash
    for _ in {1..30}; do
        torrents="$(api_get torrents/info)"
        torrent_hash="$(jq --raw-output --arg name "${torrent_name}" \
            '.[] | select(.name == $name) | .hash' <<<"${torrents}")"
        if [[ -n "${torrent_hash}" ]]; then
            printf '%s\n' "${torrent_hash}"
            return 0
        fi
        sleep 1
    done
    printf 'Torrent did not appear before timeout: %s\n' "${torrent_name}" >&2
    return 1
}

add_torrent() {
    local torrent_name="$1"
    local category="$2"
    local tags="$3"
    local torrent_path="${fixture_root}/torrents/${torrent_name}.torrent"

    curl --fail --silent --show-error \
        --output /dev/null \
        --header "Authorization: Bearer ${api_key}" \
        --request POST \
        --form "torrents=@${torrent_path};type=application/x-bittorrent" \
        --form 'savepath=/downloads' \
        --form "category=${category}" \
        --form "tags=${tags}" \
        --form 'stopped=true' \
        "${server_url}/api/v2/torrents/add"
}

existing_torrents="$(api_get torrents/info)"
fixture_hashes="$(jq --raw-output \
    --argjson names "$(printf '%s\n' "${fixture_names[@]}" | jq --raw-input --slurp 'split("\n")[:-1]')" \
    '[.[] | select(.name as $name | $names | index($name)) | .hash] | join("|")' \
    <<<"${existing_torrents}")"
if [[ -n "${fixture_hashes}" ]]; then
    api_post torrents/delete \
        --data-urlencode "hashes=${fixture_hashes}" \
        --data-urlencode 'deleteFiles=true'
fi

categories="$(api_get torrents/categories)"
for category in radarr sonarr; do
    if ! jq --exit-status --arg category "${category}" 'has($category)' <<<"${categories}" >/dev/null; then
        api_post torrents/createCategory \
            --data-urlencode "category=${category}" \
            --data-urlencode 'savePath=/downloads'
    fi
done
api_post torrents/deleteTags --data-urlencode 'tags=fixture|jfNeverTouch'
api_post torrents/createTags --data-urlencode 'tags=fixture,jfNeverTouch'

mkdir -p "${download_root}"
cp --reflink=auto \
    "${fixture_root}/webseed/complete-seeding.bin" \
    "${download_root}/complete-seeding.bin"
cp --reflink=auto \
    "${fixture_root}/webseed/complete-stopped.bin" \
    "${download_root}/complete-stopped.bin"

add_torrent complete-seeding.bin radarr fixture
add_torrent complete-stopped.bin radarr fixture
add_torrent incomplete-stopped.bin sonarr 'fixture,jfNeverTouch'
add_torrent incomplete-stalled.bin sonarr fixture
add_torrent incomplete-downloading.bin sonarr fixture
add_torrent incomplete-queued.bin sonarr fixture

complete_seeding_hash="$(wait_for_hash complete-seeding.bin)"
complete_stopped_hash="$(wait_for_hash complete-stopped.bin)"
incomplete_stalled_hash="$(wait_for_hash incomplete-stalled.bin)"
incomplete_downloading_hash="$(wait_for_hash incomplete-downloading.bin)"
incomplete_queued_hash="$(wait_for_hash incomplete-queued.bin)"

api_post torrents/recheck \
    --data-urlencode "hashes=${complete_seeding_hash}|${complete_stopped_hash}"

completed_ready=false
for _ in {1..90}; do
    current="$(api_get torrents/info)"
    if jq --exit-status \
        --arg first "${complete_seeding_hash}" \
        --arg second "${complete_stopped_hash}" \
        '([.[] | select((.hash == $first or .hash == $second) and .progress == 1 and (.state | startswith("checking") | not))] | length) == 2' \
        <<<"${current}" >/dev/null; then
        completed_ready=true
        break
    fi
    sleep 1
done
if [[ "${completed_ready}" != "true" ]]; then
    printf 'Completed fixture torrents did not finish rechecking before timeout.\n' >&2
    exit 3
fi

api_post torrents/start --data-urlencode "hashes=${complete_seeding_hash}"
api_post torrents/start --data-urlencode "hashes=${incomplete_stalled_hash}"
api_post torrents/setDownloadLimit \
    --data-urlencode "hashes=${incomplete_downloading_hash}" \
    --data-urlencode 'limit=65536'
api_post torrents/start --data-urlencode "hashes=${incomplete_downloading_hash}"
api_post torrents/start --data-urlencode "hashes=${incomplete_queued_hash}"

states_ready=false
for _ in {1..90}; do
    current="$(api_get torrents/info)"
    if jq --exit-status '
        ([.[] | select(.name == "complete-seeding.bin" and .progress == 1 and (.state | startswith("stopped") | not))] | length == 1)
        and ([.[] | select(.name == "complete-stopped.bin" and .progress == 1 and (.state | startswith("stopped")))] | length == 1)
        and ([.[] | select(.name == "incomplete-stopped.bin" and .progress < 1 and (.state | startswith("stopped")))] | length == 1)
        and ([.[] | select(.name == "incomplete-stalled.bin" and .progress < 1 and .state == "stalledDL")] | length == 1)
        and ([.[] | select(.name == "incomplete-downloading.bin" and .progress > 0 and .progress < 1 and .state == "downloading")] | length == 1)
        and ([.[] | select(.name == "incomplete-queued.bin" and .progress < 1 and .state == "queuedDL")] | length == 1)
    ' <<<"${current}" >/dev/null; then
        states_ready=true
        break
    fi
    sleep 1
done
if [[ "${states_ready}" != "true" ]]; then
    printf 'qBittorrent fixtures did not reach the expected stable states.\n' >&2
    jq '[.[] | {name, state, progress, category, tags}]' <<<"${current}" >&2
    exit 4
fi

printf 'Established six deterministic qBittorrent torrent-state fixtures.\n'
