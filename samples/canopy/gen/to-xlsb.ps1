<#
.SYNOPSIS
    Rewrites `xlsx/*.xlsx` as `xlsb/*.xlsb`, the same workbooks in the binary container.

.DESCRIPTION
    The generator writes `.xlsx`, because the library it uses cannot write BIFF12. Excel can,
    and this asks it to - so the corpus exists in both containers with the same values in it.

    That is what makes the pair a gate rather than a demonstration. `recipe.jsonc` reads the
    `.xlsx` and `recipe-xlsb.jsonc` reads the `.xlsb`, and the two conversions must produce
    byte-identical output. A difference is a fault in the reader, because nothing else differs.

    Excel is required, so this is not part of the build. The `.xlsb` files are committed;
    re-run this only after regenerating the workbooks.

.EXAMPLE
    powershell -File samples/canopy/gen/to-xlsb.ps1
#>
[CmdletBinding()]
param(
    [string] $Root
)

$ErrorActionPreference = 'Stop'

# Not a parameter default: `$PSScriptRoot` is not bound yet while the parameters are.
if (-not $Root) { $Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }

$source = Join-Path $Root 'xlsx'
$target = Join-Path $Root 'xlsb'

if (-not (Test-Path $source)) { throw "No such folder: $source" }
if (-not (Test-Path $target)) { [void](New-Item -ItemType Directory -Path $target) }

# 50 is xlExcel12, the binary workbook format.
$xlExcel12 = 50

try {
    $excel = New-Object -ComObject Excel.Application
} catch {
    throw "Excel is needed to write .xlsb and is not available here: $($_.Exception.Message)"
}

$excel.Visible = $false
$excel.DisplayAlerts = $false

$written = 0
try {
    foreach ($file in Get-ChildItem -Path $source -Filter *.xlsx) {
        $out = Join-Path (Resolve-Path $target) ($file.BaseName + '.xlsb')
        $book = $excel.Workbooks.Open($file.FullName, 0, $true)
        try {
            $book.SaveAs($out, $xlExcel12)
        } finally {
            $book.Close($false)
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($book)
        }

        $written++
        "{0,-24} -> {1}" -f $file.Name, (Split-Path $out -Leaf)
    }
} finally {
    $excel.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel)
}

""
"$written workbooks rewritten as .xlsb"
