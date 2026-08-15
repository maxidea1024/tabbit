#!/bin/bash
# Publishes a self-contained single-file Tabbit for Windows into ../bin/<rid>.
#
# For cross-publishing from a shell - Git Bash, WSL, a Linux CI runner. The batch file
# beside this does the same thing from cmd.
#
# PublishTrimmed is deliberately off: NPOI, Newtonsoft.Json and Google.Apis all
# resolve types by reflection, and trimming strips members they need at runtime.
#
# Each runtime identifier gets a directory of its own. They used to share ../bin, which
# works until two of them are built there: a self-contained publish puts its native
# dependencies beside the executable, and the second publish leaves the first one's
# behind.
set -euo pipefail

cd "$(dirname "$0")"

rid="${1:-win-x64}"

dotnet publish ../src/Tabbit.csproj \
  --configuration Release \
  --runtime "$rid" \
  --self-contained true \
  -p:PublishSingleFile=true \
  --output "../bin/$rid"

echo "Built ../bin/$rid/tabbit.exe"
