#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"
server_url="http://127.0.0.1:18180"
api_key_file="${project_root}/.testenv/secrets/qbittorrent-api-key"
fixture_password="qcontrol-local-only"
cookie_jar="$(mktemp /tmp/qcontrol-qbittorrent-cookie.XXXXXX)"
trap 'rm -f -- "${cookie_jar}"' EXIT

compose() {
    docker compose \
        --project-name qcontrol-test \
        --project-directory "${project_root}" \
        --file "${project_root}/compose.yaml" \
        "$@"
}

for _ in {1..45}; do
    if curl --fail --silent "${server_url}/" >/dev/null; then
        break
    fi
    sleep 1
done

if ! curl --fail --silent "${server_url}/" >/dev/null; then
    printf 'qBittorrent did not accept WebUI requests before the timeout.\n' >&2
    exit 2
fi

if [[ -s "${api_key_file}" ]]; then
    existing_key="$(<"${api_key_file}")"
    if curl --fail --silent \
        --header "Authorization: Bearer ${existing_key}" \
        "${server_url}/api/v2/app/version" >/dev/null; then
        printf 'Existing qBittorrent fixture API key is valid.\n'
        exit 0
    fi
fi

login() {
    local password="$1"
    curl --fail --silent --show-error \
        --output /dev/null \
        --cookie-jar "${cookie_jar}" \
        --header "Referer: ${server_url}" \
        --data-urlencode 'username=admin' \
        --data-urlencode "password=${password}" \
        "${server_url}/api/v2/auth/login"
}

if ! login "${fixture_password}" 2>/dev/null; then
    temporary_password="$(compose logs --no-color qbittorrent 2>/dev/null | awk '
        /temporary password is provided for this session:/ { password = $NF }
        END { print password }
    ')"
    if [[ -z "${temporary_password}" ]] || ! login "${temporary_password}"; then
        printf 'Could not authenticate to the isolated qBittorrent fixture.\n' >&2
        exit 3
    fi
fi

rotation_response="$(curl --fail --silent --show-error \
    --cookie "${cookie_jar}" \
    --header "Referer: ${server_url}" \
    --request POST \
    "${server_url}/api/v2/app/rotateAPIKey")"
api_key="$(jq --exit-status --raw-output \
    '.apiKey | select(test("^qbt_[A-Za-z0-9]{28}$"))' \
    <<<"${rotation_response}")"

preferences='{
  "web_ui_password": "qcontrol-local-only",
  "dht": false,
  "pex": false,
  "lsd": false,
  "upnp": false,
  "random_port": false,
  "enable_embedded_tracker": false,
  "queueing_enabled": true,
  "max_active_downloads": 2,
  "max_active_torrents": 10,
  "max_active_uploads": 2
}'
curl --fail --silent --show-error \
    --output /dev/null \
    --cookie "${cookie_jar}" \
    --header "Referer: ${server_url}" \
    --request POST \
    --data-urlencode "json=${preferences}" \
    "${server_url}/api/v2/app/setPreferences"

mkdir -p "$(dirname -- "${api_key_file}")"
umask 077
printf '%s\n' "${api_key}" >"${api_key_file}"
chmod 600 "${api_key_file}"

curl --fail --silent --show-error \
    --header "Authorization: Bearer ${api_key}" \
    "${server_url}/api/v2/app/version" >/dev/null

curl --fail --silent --show-error \
    --output /dev/null \
    --cookie "${cookie_jar}" \
    --header "Referer: ${server_url}" \
    --request POST \
    "${server_url}/api/v2/auth/logout"

printf 'Generated and verified an ephemeral qBittorrent fixture API key.\n'
