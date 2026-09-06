#!/usr/bin/env bash

set -euo pipefail

readonly SOURCE_URL="${1:?source URL is required}"
readonly EXPECTED_SHA256="${2:?SHA-256 checksum is required}"
readonly DESTINATION="${3:?destination is required}"
readonly DESTINATION_DIRECTORY="$(dirname "${DESTINATION}")"

mkdir -p "${DESTINATION_DIRECTORY}"
TEMPORARY_DOWNLOAD="$(mktemp "${DESTINATION}.download.XXXXXX")"
readonly TEMPORARY_DOWNLOAD

cleanup() {
  rm -f "${TEMPORARY_DOWNLOAD}"
}
trap cleanup EXIT

curl -fsSL -L "${SOURCE_URL}" -o "${TEMPORARY_DOWNLOAD}"
bash "$(dirname "${BASH_SOURCE[0]}")/verify-sha256.sh" \
  "${EXPECTED_SHA256}" \
  "${TEMPORARY_DOWNLOAD}"
mv "${TEMPORARY_DOWNLOAD}" "${DESTINATION}"
