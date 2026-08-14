#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"
field="${1:-}"

case "${field}" in
    framework | guid | targetAbi | version)
        ;;
    *)
        printf 'Usage: %s {framework|guid|targetAbi|version}\n' "$0" >&2
        exit 2
        ;;
esac

mapfile -t values < <(awk -v key="${field}" '
    $1 == key ":" {
        value = $0
        sub(/^[^:]+:[[:space:]]*/, "", value)
        sub(/[[:space:]]+$/, "", value)
        gsub(/^"|"$/, "", value)
        print value
    }
' "${project_root}/build.yaml")

if [[ "${#values[@]}" -ne 1 || -z "${values[0]}" ]]; then
    printf 'build.yaml must contain exactly one non-empty %s value.\n' "${field}" >&2
    exit 3
fi

printf '%s\n' "${values[0]}"
