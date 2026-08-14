#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"
server_url="http://127.0.0.1:18196"
token_file="${project_root}/.testenv/jellyfin/access-token"
admin_name="qcontrol-admin"
admin_password="qcontrol-local-only"
client_authorization='MediaBrowser Client="QControl Tests", Device="Docker Fixture", DeviceId="qcontrol-tests", Version="0.1.0.0"'

public_info=''
for _ in {1..45}; do
    if public_info="$(curl --fail --silent "${server_url}/System/Info/Public")"; then
        break
    fi
    sleep 1
done
if [[ -z "${public_info}" ]]; then
    printf 'Jellyfin did not accept setup requests after becoming healthy.\n' >&2
    exit 2
fi

wizard_complete="$(jq --raw-output .StartupWizardCompleted <<<"${public_info}")"
if [[ "${wizard_complete}" != "true" ]]; then
    curl --fail --silent --request POST \
        --header 'Content-Type: application/json' \
        --data '{"ServerName":"QControl Test","UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' \
        "${server_url}/Startup/Configuration"

    curl --fail --silent "${server_url}/Startup/User" >/dev/null

    curl --fail --silent --request POST \
        --header 'Content-Type: application/json' \
        --data "{\"Name\":\"${admin_name}\",\"Password\":\"${admin_password}\"}" \
        "${server_url}/Startup/User"

    curl --fail --silent --request POST \
        --header 'Content-Type: application/json' \
        --data '{"EnableRemoteAccess":false,"EnableAutomaticPortMapping":false}' \
        "${server_url}/Startup/RemoteAccess"

    curl --fail --silent --request POST "${server_url}/Startup/Complete"
fi

authentication_result="$(curl --fail --silent --request POST \
    --header "Authorization: ${client_authorization}" \
    --header 'Content-Type: application/json' \
    --data "{\"Username\":\"${admin_name}\",\"Pw\":\"${admin_password}\"}" \
    "${server_url}/Users/AuthenticateByName")"
access_token="$(jq --exit-status --raw-output .AccessToken <<<"${authentication_result}")"

umask 077
printf '%s\n' "${access_token}" >"${token_file}"
chmod 600 "${token_file}"

api_get() {
    curl --fail --silent --header "X-Emby-Token: ${access_token}" "$1"
}

api_post() {
    curl --fail --silent --request POST \
        --header "X-Emby-Token: ${access_token}" \
        --header 'Content-Type: application/json' \
        --data "${2:-{}}" \
        "$1"
}

virtual_folders="$(api_get "${server_url}/Library/VirtualFolders")"
if ! jq --exit-status '.[] | select(.Name == "Movies")' <<<"${virtual_folders}" >/dev/null; then
    api_post "${server_url}/Library/VirtualFolders?name=Movies&collectionType=movies&paths=%2Fmedia%2FMovies&refreshLibrary=false"
fi
api_post "${server_url}/Library/Refresh"

movie_count=0
for _ in {1..30}; do
    items="$(api_get "${server_url}/Items?Recursive=true&IncludeItemTypes=Movie&Fields=Path")"
    movie_count="$(jq '[.Items[] | select(.Type == "Movie")] | length' <<<"${items}")"
    if [[ "${movie_count}" -ge 1 ]]; then
        break
    fi
    sleep 1
done
if [[ "${movie_count}" -lt 1 ]]; then
    printf 'The synthetic Jellyfin movie did not appear before timeout.\n' >&2
    exit 3
fi

printf 'Configured the isolated Jellyfin server and one synthetic movie.\n'
