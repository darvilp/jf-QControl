#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"
plugin_version="$("${script_dir}/read-build-metadata.sh" version)"
artifact_path="${1:-${project_root}/artifacts/qcontrol_${plugin_version}.zip}"
plugin_root="${project_root}/.testenv/jellyfin/config/plugins"

"${script_dir}/verify-package.sh" "${artifact_path}"

package_version="$(unzip -p "${artifact_path}" meta.json | jq --raw-output .version)"
plugin_directory="${plugin_root}/QControl_${package_version}"

if [[ "${plugin_root}" != "${project_root}/.testenv/jellyfin/config/plugins" ]]; then
    printf 'Refusing to install into unexpected path: %s\n' "${plugin_root}" >&2
    exit 2
fi

mkdir -p "${plugin_directory}"
unzip -o "${artifact_path}" -d "${plugin_directory}" >/dev/null

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
if [[ "${health_status}" != "healthy" ]]; then
    printf 'Jellyfin did not become healthy after plugin installation; status: %s.\n' \
        "${health_status}" >&2
    exit 3
fi

curl --fail --silent http://127.0.0.1:18196/health | grep --fixed-strings 'Healthy' >/dev/null

docker compose \
    --project-name qcontrol-test \
    --project-directory "${project_root}" \
    --file "${project_root}/compose.yaml" \
    logs --since 2m jellyfin \
    | grep --fixed-strings 'Loaded plugin: QControl'

printf 'Installed QControl %s into the isolated Jellyfin server.\n' "${package_version}"
