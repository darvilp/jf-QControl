#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/../.." && pwd)"
container_name="qcontrol-test-webseed"
image="nginx:1.28.0-alpine@sha256:30f1c0d78e0ad60901648be663a710bdadf19e4c10ac6782c235200619158284"

cleanup() {
    if docker container inspect "${container_name}" >/dev/null 2>&1; then
        docker container rm --force "${container_name}" >/dev/null
    fi
}
trap cleanup EXIT

"${project_root}/scripts/test-env.sh" down >/dev/null
docker container create \
    --name "${container_name}" \
    --label io.qcontrol.fixture=false \
    "${image}" >/dev/null

if output="$("${project_root}/scripts/test-env.sh" down 2>&1)"; then
    printf 'Teardown unexpectedly accepted a non-project container.\n' >&2
    exit 1
fi
if [[ "${output}" != *'Refusing to act on non-QControl container'* ]]; then
    printf 'Teardown failed without its ownership diagnostic: %s\n' "${output}" >&2
    exit 1
fi
docker container inspect "${container_name}" >/dev/null

printf 'Verified teardown refuses a same-name non-project container.\n'
