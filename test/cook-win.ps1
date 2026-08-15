# Runs a locally published Tabbit over the recipe beside this script.
#
# The path follows build\build-win64.ps1, which publishes per runtime identifier so two
# platforms built on one machine do not overwrite each other's native dependencies.

$ErrorActionPreference = 'Stop'

# Push/Pop rather than Set-Location: the current directory belongs to the whole runspace
# and not to this script, so a plain Set-Location leaves the caller's shell somewhere it
# did not ask to be. The batch file this replaces used pushd/popd for the same reason.
Push-Location $PSScriptRoot

try {
    ..\bin\win-x64\tabbit.exe --recipe recipe.json

    if ($LASTEXITCODE -ne 0) {
        throw "tabbit failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
