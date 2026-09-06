#!/usr/bin/env bash

set -euo pipefail

readonly EXPECTED_SHA256="${1:?expected SHA-256 checksum is required}"
readonly ARTIFACT="${2:?artifact path is required}"

if command -v sha256sum >/dev/null 2>&1; then
  checksum_output="$(sha256sum "${ARTIFACT}")"
elif command -v shasum >/dev/null 2>&1; then
  checksum_output="$(shasum -a 256 "${ARTIFACT}")"
else
  echo "No SHA-256 checksum command is available" >&2
  exit 1
fi
readonly checksum_output
readonly ACTUAL_SHA256="${checksum_output%% *}"

if [[ "${ACTUAL_SHA256}" != "${EXPECTED_SHA256}" ]]; then
  echo "SHA-256 checksum mismatch for ${ARTIFACT}" >&2
  exit 1
fi
