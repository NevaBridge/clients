#!/usr/bin/env bash

set -euo pipefail

readonly PACKAGE_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly FIXTURE_ROOT="${PACKAGE_ROOT}/tests/consumer-commonjs"
readonly TEMPORARY_CONSUMER="$(mktemp -d)"

cleanup() {
  rm -rf "${TEMPORARY_CONSUMER}"
}
trap cleanup EXIT

mkdir -p "${TEMPORARY_CONSUMER}/node_modules/@nevabridge"
cp "${FIXTURE_ROOT}/index.cts" "${FIXTURE_ROOT}/package.json" "${FIXTURE_ROOT}/tsconfig.json" \
  "${TEMPORARY_CONSUMER}/"
ln -s "${PACKAGE_ROOT}" "${TEMPORARY_CONSUMER}/node_modules/@nevabridge/sdk"
"${PACKAGE_ROOT}/node_modules/.bin/tsc" -p "${TEMPORARY_CONSUMER}/tsconfig.json"
