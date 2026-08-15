#!/bin/bash
# Runs a locally published Tabbit over the recipe beside this script.
#
# The path follows build/build-linux64.sh, which publishes per runtime identifier so two
# platforms built on one machine do not overwrite each other's native dependencies.
set -euo pipefail

cd "$(dirname "$0")"

case "$(uname -m)" in
  aarch64 | arm64) rid=linux-arm64 ;;
  *)               rid=linux-x64   ;;
esac

"../bin/$rid/tabbit" --recipe recipe.json
