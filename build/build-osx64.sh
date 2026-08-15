#!/bin/bash
# Publishes a self-contained single-file Tabbit for macOS into ../bin/<rid>.
#
# PublishTrimmed is deliberately off: NPOI, Newtonsoft.Json and Google.Apis all
# resolve types by reflection, and trimming strips members they need at runtime.
#
# The architecture is read from the machine rather than fixed at osx-x64, because every
# Mac sold since 2020 is osx-arm64 and a self-contained publish is native code - one
# built for the wrong architecture does not start. Pass a runtime identifier to override
# it.
#
# Each runtime identifier gets a directory of its own. They used to share ../bin, which
# works until two of them are built there: a self-contained publish puts its native
# dependencies beside the executable, and the second publish leaves the first one's
# behind.
set -euo pipefail

cd "$(dirname "$0")"

if [ $# -gt 0 ]; then
  rid="$1"
else
  case "$(uname -m)" in
    arm64 | aarch64) rid=osx-arm64 ;;
    *)               rid=osx-x64   ;;
  esac
fi

dotnet publish ../src/Tabbit.csproj \
  --configuration Release \
  --runtime "$rid" \
  --self-contained true \
  -p:PublishSingleFile=true \
  --output "../bin/$rid"

echo "Built ../bin/$rid/tabbit"
