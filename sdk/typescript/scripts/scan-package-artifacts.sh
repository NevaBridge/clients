#!/usr/bin/env bash

set -euo pipefail

readonly DEFAULT_PACKAGE_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly PACKAGE_ROOT="${1:-${DEFAULT_PACKAGE_ROOT}}"
readonly TEMPORARY_DIRECTORY="$(mktemp -d)"
readonly EXTRACTED_PACKAGE_PATH="${TEMPORARY_DIRECTORY}/package"
readonly FORBIDDEN_PATTERN='@nevabridge/shared|AKIA[0-9A-Z]{16}|ASIA[0-9A-Z]{16}|Bearer [A-Za-z0-9_=-]{20,}|client[_-]?secret|sk-workos-[A-Za-z0-9_-]{8,}'

cleanup() {
  rm -rf "${TEMPORARY_DIRECTORY}"
}
trap cleanup EXIT

cd "${PACKAGE_ROOT}"
readonly PACKAGE_ARCHIVE="$(
  bun pm pack \
    --destination "${TEMPORARY_DIRECTORY}" \
    --ignore-scripts \
    --quiet | tail -n 1
)"
tar -xzf "${PACKAGE_ARCHIVE}" -C "${TEMPORARY_DIRECTORY}"

SCAN_PATHS=("${EXTRACTED_PACKAGE_PATH}")
for candidate in src examples README.md LICENSE; do
  if [[ -e "${PACKAGE_ROOT}/${candidate}" ]]; then
    SCAN_PATHS+=("${PACKAGE_ROOT}/${candidate}")
  fi
done

# grep, not ripgrep: ripgrep is not a documented prerequisite of this repo, and an
# absent scanner inside an `if` condition would exit 0 and pass the gate on anything.
# grep -r reads hidden files and ignores .gitignore, so no rg-style flags are needed.
SCAN_STATUS=0
SCAN_OUTPUT="$(grep --recursive --binary-files=text --ignore-case --line-number \
  --extended-regexp "${FORBIDDEN_PATTERN}" "${SCAN_PATHS[@]}")" || SCAN_STATUS=$?

if [[ "${SCAN_STATUS}" -eq 0 ]]; then
  printf '%s\n' "${SCAN_OUTPUT}"
  echo "Forbidden pattern found in the packaged artifacts." >&2
  exit 1
fi

# Anything other than "matched" (0) or "no match" (1) means the scan did not run.
if [[ "${SCAN_STATUS}" -ne 1 ]]; then
  echo "Artifact scan did not run: grep exited ${SCAN_STATUS}." >&2
  exit 2
fi
