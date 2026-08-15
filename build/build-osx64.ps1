# Cross-publishes a self-contained single-file Tabbit for macOS into ..\bin\<rid>.
#
# The shell script beside this is the one to use on a Mac itself, and it reads the
# architecture off the machine. This is for publishing a macOS build from elsewhere, so the
# runtime identifier is a choice rather than something to detect - osx-arm64 by default,
# because every Mac sold since 2020 is one.
#
# PublishTrimmed is deliberately off: NPOI, Newtonsoft.Json and Google.Apis all
# resolve types by reflection, and trimming strips members they need at runtime.
#
# Each runtime identifier gets a directory of its own. They used to share ..\bin, which
# works until two of them are built there: a self-contained publish puts its native
# dependencies beside the executable, and the second publish leaves the first one's behind.

[CmdletBinding()]
param(
    [ValidateSet('osx-arm64', 'osx-x64')]
    [string] $Rid = 'osx-arm64'
)

$ErrorActionPreference = 'Stop'

# Push/Pop rather than Set-Location: the current directory belongs to the whole runspace
# and not to this script, so a plain Set-Location leaves the caller's shell somewhere it
# did not ask to be. The batch file this replaces used pushd/popd for the same reason.
Push-Location $PSScriptRoot

try {
    dotnet publish ..\src\Tabbit.csproj `
        --configuration Release `
        --runtime $Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        --output "..\bin\$Rid"

    # $LASTEXITCODE, not $?: dotnet is a native program, so a non-zero exit is not a
    # PowerShell error and $ErrorActionPreference does not see it.
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    Write-Host "Built bin\$Rid\tabbit"
}
finally {
    Pop-Location
}
