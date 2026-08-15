# Publishes a self-contained single-file Tabbit for Windows into ..\bin\<rid>.
#
# PublishTrimmed is deliberately off: NPOI, Newtonsoft.Json and Google.Apis all
# resolve types by reflection, and trimming strips members they need at runtime.
#
# Each runtime identifier gets a directory of its own. They used to share ..\bin, which
# works until two of them are built there: a self-contained publish puts its native
# dependencies beside the executable, and the second publish leaves the first one's behind.

[CmdletBinding()]
param(
    # A runtime identifier, for cross-publishing or for an arm64 machine.
    [string] $Rid = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Rid)) {
    # Read from the machine rather than fixed at win-x64: a self-contained publish is
    # native code, and one built for the wrong architecture does not start.
    $Rid = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') {
        'win-arm64'
    } else {
        'win-x64'
    }
}

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

    Write-Host "Built bin\$Rid\tabbit.exe"
}
finally {
    Pop-Location
}
