# Every test on net48 (Release2024), net8 (Release2025) and net10 (Release2027). Stops at the first failure.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
$stray = git ls-files --others --exclude-standard -- ':(icase)*.dxf' ':(icase)*.dat' ':(icase)*.dwfx' ':(icase)*.rfa'
if ($stray) { throw "Untracked drawings - commit them as fixtures or ignore them:`n$($stray -join "`n")" }
$projects = @(Select-String -Path RVTuk.sln -Pattern '\.Tests\.csproj"').Count
$summary = foreach ($c in 'Release2024', 'Release2025', 'Release2027') {
    dotnet test RVTuk.sln -c $c | Tee-Object -Variable out | Out-Host
    if ($LASTEXITCODE) { throw "Tests failed: $c" }
    $lines = $out | Select-String '(Passed|Failed)!|Test summary'
    if (@($lines).Count -lt $projects) { throw "Not every test project ran: $c" }
    $lines | ForEach-Object { "$c  $($_.Line.Trim())" }
}
dotnet test installer\RVTuk.Setup.Tests\RVTuk.Setup.Tests.csproj -c Release | Tee-Object -Variable out | Out-Host
if ($LASTEXITCODE) { throw "Tests failed: Setup" }
$lines = $out | Select-String '(Passed|Failed)!'
if (-not $lines) { throw "No tests ran: Setup" }
$summary += $lines | ForEach-Object { "Setup  $($_.Line.Trim())" }
$summary
