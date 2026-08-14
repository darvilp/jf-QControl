#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"
server_url="http://127.0.0.1:18180"
api_key_file="${project_root}/.testenv/secrets/qbittorrent-api-key"
output_mode="${1:-}"

if [[ ! -s "${api_key_file}" ]]; then
    printf 'qBittorrent fixture API key is missing. Run scripts/test-env.sh up first.\n' >&2
    exit 2
fi
api_key="$(<"${api_key_file}")"

api_get() {
    curl --fail --silent --show-error \
        --header "Authorization: Bearer ${api_key}" \
        "${server_url}/api/v2/$1"
}

application_version="$(api_get app/version)"
web_api_version="$(api_get app/webapiVersion)"
categories="$(api_get torrents/categories)"
tags="$(api_get torrents/tags)"
torrents="$(api_get torrents/info)"
speed_limits_mode="$(api_get transfer/speedLimitsMode)"
alternative_speed_limits=false
if [[ "${speed_limits_mode}" == "1" ]]; then
    alternative_speed_limits=true
fi

probe="$(jq --null-input \
    --arg application_version "${application_version}" \
    --arg web_api_version "${web_api_version}" \
    --argjson categories "${categories}" \
    --argjson tags "${tags}" \
    --argjson torrents "${torrents}" \
    --argjson alternative_speed_limits "${alternative_speed_limits}" \
    '{
        applicationVersion: $application_version,
        webApiVersion: $web_api_version,
        alternativeSpeedLimits: $alternative_speed_limits,
        categories: ($categories | keys),
        tags: $tags,
        torrents: [
            $torrents[] | {
                hash,
                name,
                state,
                progress,
                category,
                tags
            }
        ]
    }')"

case "${output_mode}" in
    --json)
        printf '%s\n' "${probe}"
        ;;
    '')
        jq . <<<"${probe}"
        ;;
    *)
        printf 'Usage: %s [--json]\n' "$0" >&2
        exit 2
        ;;
esac
