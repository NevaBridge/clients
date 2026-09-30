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

# Use grep, not ripgrep. This repo does not document ripgrep as a prerequisite, and
# an absent scanner inside an `if` condition would exit 0 and pass the gate on
# anything. grep -r reads hidden files and ignores .gitignore, so no rg-style flags
# are needed.
#
# Use --files-with-matches, never --line-number. This scan exists to stop a
# credential reaching a published artifact, so it must not copy the matched text
# into a build log, where it would outlive any cleanup of the artifact itself. A
# reader needs the file name and must not be handed the value.
SCAN_STATUS=0
SCAN_OUTPUT="$(grep --recursive --binary-files=text --ignore-case --files-with-matches \
  --extended-regexp "${FORBIDDEN_PATTERN}" "${SCAN_PATHS[@]}")" || SCAN_STATUS=$?

if [[ "${SCAN_STATUS}" -eq 0 ]]; then
  echo "Forbidden pattern found in the packaged artifacts. Matching files:" >&2
  while IFS= read -r matching_path; do
    # A path is content too, because a file can be named after the secret it holds.
    # Withhold any path that matches instead of substituting inside it. The pattern
    # contains both "/" and "|", so every sed delimiter is unsafe, and a broken
    # expression would print the path unredacted.
    if grep --quiet --ignore-case --extended-regexp "${FORBIDDEN_PATTERN}" <<< "${matching_path}"; then
      echo "  [path withheld: the file name itself matches a forbidden pattern]" >&2
    else
      echo "  ${matching_path}" >&2
    fi
  done <<< "${SCAN_OUTPUT}"
  echo "Matched content is deliberately not printed. Inspect these files locally." >&2
  exit 1
fi

# Anything other than "matched" (0) or "no match" (1) means the scan did not run.
if [[ "${SCAN_STATUS}" -ne 1 ]]; then
  echo "Artifact scan did not run: grep exited ${SCAN_STATUS}." >&2
  exit 2
fi
