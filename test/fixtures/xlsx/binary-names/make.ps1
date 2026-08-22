# Writes the binary-names fixture pair: the same workbook as .xlsx and .xlsb, holding
# every defined-name shape spec/xlsb-defined-names.md section 6 asks for.
#
# Writing an .xlsb needs Excel, so this runs on Windows with Excel installed - once. The
# pair it writes is committed, and the tests only ever read it; nothing in CI runs this.
#
#     powershell -File test/fixtures/xlsx/binary-names/make.ps1
$ErrorActionPreference = 'Stop'

$outDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false
$xl.DisplayAlerts = $false

try {
  $wb = $xl.Workbooks.Add()
  while ($wb.Worksheets.Count -gt 1) { $wb.Worksheets.Item(2).Delete() }

  $alpha = $wb.Worksheets.Item(1)
  $alpha.Name = 'Alpha'
  $beta = $wb.Worksheets.Add([Type]::Missing, $alpha)
  $beta.Name = 'Beta Sheet'
  $temp = $wb.Worksheets.Add([Type]::Missing, $beta)
  $temp.Name = 'Temp'

  # Deterministic cell content so the pair also serves as a tiny cell-parity workbook.
  for ($r = 1; $r -le 5; $r++) {
    for ($c = 1; $c -le 3; $c++) {
      $alpha.Cells.Item($r, $c) = "a$r$c"
    }
  }
  for ($r = 2; $r -le 9; $r++) {
    for ($c = 2; $c -le 4; $c++) {
      $beta.Cells.Item($r, $c) = ($r * 100 + $c)
    }
  }
  $temp.Cells.Item(1, 1) = 'doomed'

  # Workbook-scoped names, several: the basic path.
  $wb.Names.Add('BasicTable', '=Alpha!$A$1:$C$5') | Out-Null
  # A sheet name holding a space: quoting was an XML-side-only concern.
  $wb.Names.Add('SpacedSheet', "='Beta Sheet'!`$B`$2:`$D`$9") | Out-Null
  # A single cell: the 9-byte token rather than the 15-byte one.
  $wb.Names.Add('OneCell', '=Alpha!$B$3') | Out-Null
  # A union: not one rectangle, on both sides for the same reason.
  $wb.Names.Add('TwoParts', '=Alpha!$A$1:$A$3,Alpha!$C$1:$C$3') | Out-Null
  # A whole column: also not one rectangle.
  $wb.Names.Add('WholeColumn', '=Alpha!$D:$D') | Out-Null
  # Sheet-scoped: excluded by scope, without a word, on both sides.
  $alpha.Names.Add('LocalHelper', '=Alpha!$E$1:$E$3') | Out-Null
  # A name whose target is about to be deleted: #REF!, not a range.
  $wb.Names.Add('Dangling', '=Temp!$A$1:$B$2') | Out-Null
  $temp.Delete()

  $wb.SaveAs("$outDir\binary-names.xlsx", 51)
  $wb.SaveAs("$outDir\binary-names.xlsb", 50)
  $wb.Close($false)

  Write-Host 'written:'
  Get-ChildItem $outDir | ForEach-Object { Write-Host ("  {0}  {1} bytes" -f $_.Name, $_.Length) }
}
finally {
  try { $xl.Quit() } catch {}
  [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl)
}
