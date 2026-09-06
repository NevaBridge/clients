#!/usr/bin/env bash

set -euo pipefail

readonly PACKAGE_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ESM_DECLARATIONS="${PACKAGE_ROOT}/dist/types/esm"
readonly COMMONJS_DECLARATIONS="${PACKAGE_ROOT}/dist/types/commonjs"

cd "${PACKAGE_ROOT}"
tsc --emitDeclarationOnly --outDir "${ESM_DECLARATIONS}"

while IFS= read -r declaration; do
  relative_path="${declaration#${ESM_DECLARATIONS}/}"
  commonjs_declaration="${COMMONJS_DECLARATIONS}/${relative_path%.d.ts}.d.cts"
  mkdir -p "$(dirname "${commonjs_declaration}")"
  cp "${declaration}" "${commonjs_declaration}"
  perl -pi -e "s/\\.js'/\\.cjs'/g; s/\\.js\\\"/\\.cjs\\\"/g" "${commonjs_declaration}"
done < <(find "${ESM_DECLARATIONS}" -type f -name '*.d.ts' | sort)
