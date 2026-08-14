#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/../.." && pwd)"
compose_file="${project_root}/compose.yaml"
environment_script="${project_root}/scripts/test-env.sh"

if [[ ! -f "${compose_file}" ]]; then
    printf 'Docker fixture is missing: %s\n' "${compose_file}" >&2
    exit 1
fi

rendered="$(docker compose --project-directory "${project_root}" --file "${compose_file}" config)"

grep --fixed-strings 'jellyfin/jellyfin:10.11.11@sha256:aefb67e6a7ff1debdd154a78a7bbb780fd0c873d8639210a7f6a2016ad2b35db' <<<"${rendered}" >/dev/null
grep --fixed-strings 'lscr.io/linuxserver/qbittorrent:5.2.3@sha256:6816d2b144b1eb97665f886e41e18a14d026ba78c9d0953fc68a1211ea819433' <<<"${rendered}" >/dev/null
grep --fixed-strings 'nginx:1.28.0-alpine@sha256:30f1c0d78e0ad60901648be663a710bdadf19e4c10ac6782c235200619158284' <<<"${rendered}" >/dev/null
grep --fixed-strings 'host_ip: 127.0.0.1' <<<"${rendered}" >/dev/null
grep --fixed-strings 'published: "18196"' <<<"${rendered}" >/dev/null
grep --fixed-strings 'published: "18180"' <<<"${rendered}" >/dev/null
grep --fixed-strings 'internal: true' <<<"${rendered}" >/dev/null

if grep --extended-regexp 'published: "(6881|16881)"' <<<"${rendered}" >/dev/null; then
    printf 'The fixture must not publish a BitTorrent peer port.\n' >&2
    exit 1
fi

test -x "${environment_script}"
grep --fixed-strings 'io.qcontrol.fixture' "${compose_file}" >/dev/null
grep --fixed-strings 'com.docker.compose.project' "${environment_script}" >/dev/null
grep --fixed-strings 'Refusing to act on non-QControl container' "${environment_script}" >/dev/null

printf 'Verified the isolated Docker fixture contract.\n'
