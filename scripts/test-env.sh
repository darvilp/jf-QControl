#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "${script_dir}/.." && pwd)"
state_root="${project_root}/.testenv"
jellyfin_root="${state_root}/jellyfin"
qbittorrent_root="${state_root}/qbittorrent"
fixture_root="${state_root}/qbittorrent-fixtures"
secret_root="${state_root}/secrets"
api_key_file="${secret_root}/qbittorrent-api-key"
compose_project="qcontrol-test"
jellyfin_image="jellyfin/jellyfin:10.11.11@sha256:aefb67e6a7ff1debdd154a78a7bbb780fd0c873d8639210a7f6a2016ad2b35db"
fixture_containers=(
    qcontrol-test-gateway
    qcontrol-test-jellyfin
    qcontrol-test-qbittorrent
    qcontrol-test-webseed
)

compose() {
    docker compose \
        --project-name "${compose_project}" \
        --project-directory "${project_root}" \
        --file "${project_root}/compose.yaml" \
        "$@"
}

redact_fixture_credentials() {
    sed --regexp-extended \
        's/(temporary password is provided for this session: )[[:graph:]]+/\1[REDACTED]/'
}

assert_owned_containers() {
    local container_name
    local project_label
    local fixture_label

    for container_name in "${fixture_containers[@]}"; do
        if ! docker container inspect "${container_name}" >/dev/null 2>&1; then
            continue
        fi

        project_label="$(docker container inspect \
            --format '{{index .Config.Labels "com.docker.compose.project"}}' \
            "${container_name}")"
        fixture_label="$(docker container inspect \
            --format '{{index .Config.Labels "io.qcontrol.fixture"}}' \
            "${container_name}")"
        if [[ "${project_label}" != "${compose_project}" || "${fixture_label}" != "true" ]]; then
            printf 'Refusing to act on non-QControl container: %s\n' "${container_name}" >&2
            exit 3
        fi
    done
}

prepare_directories() {
    mkdir -p \
        "${jellyfin_root}/config" \
        "${jellyfin_root}/cache" \
        "${jellyfin_root}/media" \
        "${qbittorrent_root}/config" \
        "${qbittorrent_root}/downloads" \
        "${fixture_root}" \
        "${secret_root}"

    if [[ ! -f "${api_key_file}" ]]; then
        install -m 600 /dev/null "${api_key_file}"
    fi
}

generate_video() {
    local output_path="${jellyfin_root}/media/Movies/QControl Fixture (2026)/QControl Fixture (2026).mkv"
    if [[ -f "${output_path}" ]]; then
        return
    fi

    mkdir -p "$(dirname -- "${output_path}")"
    docker run --rm \
        --network none \
        --user "$(id -u):$(id -g)" \
        --volume "${jellyfin_root}/media:/media" \
        --entrypoint /usr/lib/jellyfin-ffmpeg/ffmpeg \
        "${jellyfin_image}" \
        -hide_banner \
        -loglevel error \
        -f lavfi \
        -i "color=c=0x355c7d:s=320x180:d=4" \
        -f lavfi \
        -i "sine=frequency=440:duration=4" \
        -shortest \
        -c:v libx264 \
        -preset ultrafast \
        -pix_fmt yuv420p \
        -c:a aac \
        "/media/Movies/QControl Fixture (2026)/QControl Fixture (2026).mkv"
}

prepare_fixtures() {
    prepare_directories
    generate_video
    python3 "${script_dir}/generate-qbittorrent-fixtures.py" "${fixture_root}"
}

usage() {
    printf 'Usage: %s {prepare|up|fixtures|probe|down|status|logs|reset --confirm}\n' "$0"
}

command_name="${1:-}"
case "${command_name}" in
    prepare)
        prepare_fixtures
        ;;
    up)
        assert_owned_containers
        prepare_fixtures
        QCONTROL_UID="$(id -u)"
        QCONTROL_GID="$(id -g)"
        export QCONTROL_UID QCONTROL_GID
        compose up --detach --wait
        "${script_dir}/bootstrap-qbittorrent.sh"
        printf 'Jellyfin: http://127.0.0.1:18196\n'
        printf 'qBittorrent: http://127.0.0.1:18180\n'
        ;;
    fixtures)
        assert_owned_containers
        "${script_dir}/bootstrap-qbittorrent.sh"
        "${script_dir}/setup-qbittorrent-fixtures.sh"
        ;;
    probe)
        assert_owned_containers
        "${script_dir}/probe-qbittorrent.sh"
        ;;
    down)
        assert_owned_containers
        compose down --remove-orphans
        ;;
    status)
        compose ps
        ;;
    logs)
        compose logs --follow | redact_fixture_credentials
        ;;
    reset)
        if [[ "${2:-}" != "--confirm" ]]; then
            printf 'Refusing to reset without --confirm.\n' >&2
            exit 2
        fi

        assert_owned_containers
        compose down --remove-orphans
        if [[ "${state_root}" != "${project_root}/.testenv" ]]; then
            printf 'Refusing to reset unexpected path: %s\n' "${state_root}" >&2
            exit 4
        fi

        prepare_directories
        find \
            "${jellyfin_root}/config" \
            "${jellyfin_root}/cache" \
            "${qbittorrent_root}/config" \
            "${qbittorrent_root}/downloads" \
            "${secret_root}" \
            -mindepth 1 -delete
        printf 'Reset project-owned Jellyfin, qBittorrent, and secret state.\n'
        ;;
    *)
        usage
        exit 2
        ;;
esac
