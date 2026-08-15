#!/bin/bash
# Publishes a self-contained single-file Tabbit for win-x64 into ../bin.
#
# PublishTrimmed is deliberately off: NPOI, Newtonsoft.Json and Google.Apis all
# resolve types by reflection, and trimming strips members they need at runtime.
set -euo pipefail

cd "$(dirname "$0")"

dotnet publish ../src/Tabbit.csproj     --configuration Release     --runtime win-x64     --self-contained true     -p:PublishSingleFile=true     --output ../bin
