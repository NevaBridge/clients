#!/usr/bin/env bash

set -euo pipefail

readonly GENERATOR_VERSION="7.24.0"
readonly GENERATOR_SHA256="4b83ccc6fd43056c8c631cd0195e5100bd0550912502527bab09ac76152dab0c"
readonly REPOSITORY_ROOT="$(git rev-parse --show-toplevel)"
readonly DEFAULT_GENERATOR_JAR="${REPOSITORY_ROOT}/.tools/openapi-generator-cli-${GENERATOR_VERSION}.jar"
readonly GENERATOR_JAR="${OPENAPI_GENERATOR_JAR:-${DEFAULT_GENERATOR_JAR}}"
readonly GENERATOR_URL="https://github.com/OpenAPITools/openapi-generator/releases/download/v${GENERATOR_VERSION}/openapi-generator-cli-${GENERATOR_VERSION}.jar"
readonly CONTRACT_PATH="${REPOSITORY_ROOT}/contracts/nevabridge-v1.yaml"
readonly CONFIG_PATH="${REPOSITORY_ROOT}/sdk/generation/typescript-config.json"
readonly GENERATED_PATH="${REPOSITORY_ROOT}/sdk/typescript/src/generated"
readonly TEMPORARY_DIRECTORY="$(mktemp -d)"
readonly STAGED_GENERATED_PATH="${TEMPORARY_DIRECTORY}/output/src"
readonly REPLACEMENT_PATH="$(mktemp -d "${GENERATED_PATH}.replacement.XXXXXX")"
readonly BACKUP_PATH="${GENERATED_PATH}.previous.$$"

cleanup() {
  if [[ -e "${BACKUP_PATH}" && ! -e "${GENERATED_PATH}" ]]; then
    mv "${BACKUP_PATH}" "${GENERATED_PATH}"
  fi
  rm -rf "${TEMPORARY_DIRECTORY}"
  rm -rf "${REPLACEMENT_PATH}"
  rm -rf "${BACKUP_PATH}"
}
trap cleanup EXIT

if [[ ! -f "${GENERATOR_JAR}" ]]; then
  if [[ -n "${OPENAPI_GENERATOR_JAR:-}" ]]; then
    echo "OPENAPI_GENERATOR_JAR does not exist: ${GENERATOR_JAR}" >&2
    exit 1
  fi
  bash "${REPOSITORY_ROOT}/sdk/generation/cache-generator-jar.sh" \
    "${GENERATOR_URL}" \
    "${GENERATOR_SHA256}" \
    "${GENERATOR_JAR}"
fi

bash "${REPOSITORY_ROOT}/sdk/generation/verify-sha256.sh" \
  "${GENERATOR_SHA256}" \
  "${GENERATOR_JAR}"

java -jar "${GENERATOR_JAR}" generate \
  --input-spec "${CONTRACT_PATH}" \
  --generator-name typescript-fetch \
  --config "${CONFIG_PATH}" \
  --model-name-mappings Error=NevaBridgeError \
  --inline-schema-name-mappings appendMessage_409_response=AppendMessageError,inline_object=ApiError,ConnectorDeliveryResult_oneOf_1=ConnectorDeliveryFailure \
  --output "${TEMPORARY_DIRECTORY}/output"

bun "${REPOSITORY_ROOT}/sdk/generation/postprocess-typescript.ts" "${STAGED_GENERATED_PATH}"

# OpenAPI Generator emits trailing whitespace and multiple final newlines. Normalize
# those mechanical artifacts so generated commits pass the repository whitespace gate.
find "${STAGED_GENERATED_PATH}" -type f -name '*.ts' -exec \
  perl -0pi -e 's/[ \t]+\n/\n/g; s/\n+\z/\n/' {} +

cp -R "${STAGED_GENERATED_PATH}/." "${REPLACEMENT_PATH}/"
if [[ -e "${GENERATED_PATH}" ]]; then
  mv "${GENERATED_PATH}" "${BACKUP_PATH}"
fi
if ! mv "${REPLACEMENT_PATH}" "${GENERATED_PATH}"; then
  if [[ -e "${BACKUP_PATH}" ]]; then
    mv "${BACKUP_PATH}" "${GENERATED_PATH}"
  fi
  exit 1
fi
rm -rf "${BACKUP_PATH}"
