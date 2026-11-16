# Every test on net48 (Release2024), net8 (Release2025) and net10 (Release2027). Stops at the first failure.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
$stray = git ls-files --others --exclude-standard -- ':(icase)*.dxf' ':(icase)*.dat' ':(icase)*.dwfx' ':(icase)*.rfa'
if ($stray) { throw "Untracked drawings - commit them as fixtures or ignore them:`n$($stray -join "`n")" }
$summary = foreach ($c in 'Release2024', 'Release2025', 'Release2027') {
    dotnet test RVTuk.sln -c $c | Tee-Object -Variable out | Out-Host
    if ($LASTEXITCODE) { throw "Tests failed: $c" }
    $lines = $out | Select-String '(Passed|Failed)!|Test summary'
    if (-not $lines) { throw "No tests ran: $c" }
    $lines | ForEach-Object { "$c  $($_.Line.Trim())" }
}
$summary
