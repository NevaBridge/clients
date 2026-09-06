#!/usr/bin/env bash

set -euo pipefail

readonly DEFAULT_REPOSITORY_ROOT="$(git rev-parse --show-toplevel)"
readonly REPOSITORY_ROOT="${1:-${DEFAULT_REPOSITORY_ROOT}}"
readonly GENERATED_PATH="${2:-sdk/typescript/src/generated}"
readonly GENERATED_STATUS="$(
  git -C "${REPOSITORY_ROOT}" status \
    --porcelain \
    --untracked-files=all \
    -- "${GENERATED_PATH}"
)"

if [[ -n "${GENERATED_STATUS}" ]]; then
  echo "${GENERATED_STATUS}" >&2
  exit 1
fi
