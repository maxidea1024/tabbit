@echo off
rem Publishes a self-contained single-file Tabbit for osx-x64 into ..\bin.
rem
rem PublishTrimmed is deliberately off: NPOI, Newtonsoft.Json and Google.Apis all
rem resolve types by reflection, and trimming strips members they need at runtime.
pushd "%~dp0"

dotnet publish ..\src\Tabbit.csproj --configuration Release --runtime osx-x64 --self-contained true -p:PublishSingleFile=true --output ..\bin
if exist ..in\Tabbit move /Y ..in\Tabbit ..in\Tabbit-osx

popd
