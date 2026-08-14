#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"
artifact_path="${1:-}"
QCONTROL_UID="${QCONTROL_UID:-$(id -u)}"
QCONTROL_GID="${QCONTROL_GID:-$(id -g)}"
export QCONTROL_UID QCONTROL_GID
export DOCKER_CONFIG="${project_root}/.testenv/docker"
compose_arguments=(
    --project-name qcontrol-test
    --project-directory "${project_root}"
    --file "${project_root}/compose.yaml"
    --file "${project_root}/compose.e2e.yaml"
)

cleanup() {
    docker compose "${compose_arguments[@]}" down --remove-orphans >/dev/null 2>&1 || true
}
trap cleanup EXIT

mkdir -p "${DOCKER_CONFIG}"
"${script_dir}/test-env.sh" reset --confirm
"${script_dir}/test-env.sh" up
"${script_dir}/configure-test-server.sh"
"${script_dir}/test-env.sh" fixtures
if [[ -z "${artifact_path}" ]]; then
    artifact_path="$("${script_dir}/package.sh" | tail -n 1)"
fi
"${script_dir}/install-local-plugin.sh" "${artifact_path}"

journal_path="${project_root}/.testenv/jellyfin/config/plugins/configurations/Jellyfin.Plugin.QControl.journal.json"
printf '{ interrupted dashboard fixture\n' >"${journal_path}"
mkdir -p "${project_root}/artifacts/playwright"

docker compose "${compose_arguments[@]}" run --rm --build browser-e2e
