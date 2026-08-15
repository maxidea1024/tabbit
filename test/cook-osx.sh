#!/bin/bash
# Runs a locally published Tabbit over the recipe beside this script.
#
# The path follows build/build-osx64.sh, which publishes per runtime identifier so two
# platforms built on one machine do not overwrite each other's native dependencies.
set -euo pipefail

cd "$(dirname "$0")"

case "$(uname -m)" in
  arm64 | aarch64) rid=osx-arm64 ;;
  *)               rid=osx-x64   ;;
esac

"../bin/$rid/tabbit" --recipe recipe.json
