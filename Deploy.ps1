#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Deploys the add-ins from this working tree into this PC's Revit Addins folders.
.DESCRIPTION
    Builds and stages the add-ins with build\Staging.ps1 into installer\payload-staging,
    then hands that folder to the setup exe's install engine (installer\RVTuk.Setup),
    which picks the Addins folder per year, skips a year whose Revit is open or whose
    files are in use, writes the .addin manifests and prints the summary.
.PARAMETER Years
    Optional Revit years (e.g. ".\Deploy.ps1 2024 2027"). Defaults to the years found on the PC.
.PARAMETER AddIn
    Optional add-in keys (e.g. -AddIn KKarea). Defaults to all.
.PARAMETER Clean
    Remove the add-ins instead of deploying them.
.EXAMPLE
    .\Deploy.ps1                # every add-in, every Revit year on the PC
    .\Deploy.ps1 2024           # only Revit 2024
    .\Deploy.ps1 2024 2027      # only Revit 2024 and 2027
    .\Deploy.ps1 -AddIn KKarea  # only KKarea
    .\Deploy.ps1 -Clean 2025    # remove the add-ins from Revit 2025
#>
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$Years,
    [string[]]$AddIn,
    [switch]$Clean
)

$root = Split-Path $MyInvocation.MyCommand.Path -Parent
$staging = "$root\installer\payload-staging"
$exe = "$root\installer\RVTuk.Setup\bin\Release\net48\RVTukSetup.exe"
. "$root\build\Staging.ps1"

$line = "=" * 56
Write-Host ""
Write-Host "  $line" -ForegroundColor DarkCyan
Write-Host "   RVTuk deploy" -ForegroundColor White -NoNewline
Write-Host "  -  $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor DarkGray
Write-Host "  $line" -ForegroundColor DarkCyan
Write-Host ""

# Show the source revision being deployed, so it's obvious *what* is going out.
$branch = (& git -C $root rev-parse --abbrev-ref HEAD 2>$null)
$commit = (& git -C $root rev-parse --short HEAD 2>$null)
if ($branch) {
    $dirty = if (& git -C $root status --porcelain 2>$null) { " +local-changes" } else { "" }
    Write-Host "  Source : " -NoNewline -ForegroundColor DarkGray
    Write-Host "$branch @ $commit$dirty" -ForegroundColor White
    Write-Host ""
}

dotnet build "$root\installer\RVTuk.Setup\RVTuk.Setup.csproj" -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host "  Setup exe build failed." -ForegroundColor Red; exit 1 }

# The folder is this script's own; it may still hold the old layout, which New-Staging refuses to wipe.
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
$yearArgs = if ($Years) { @('--years', ($Years -join ',')) } else { @() }

$buildWarnings = @()
if ($Clean) {
    # The full catalog: the engine also removes an add-in of ours that only exists on disk.
    New-Staging -StagingDir $staging -CatalogOnly -Years $Years | Out-Null
    $keys = if ($AddIn) { $AddIn -join ',' } else { 'all' }
    & $exe --remove $keys @yearArgs --source $staging --no-pause
} else {
    Write-Host "  Building and staging ..."
    $staged = @(New-Staging -StagingDir $staging -Build -AddIn $AddIn -Years $Years -WarningVariable buildWarnings)
    if ($staged.Count -eq 0) { Write-Host "  Nothing staged." -ForegroundColor Red; exit 1 }
    & $exe --install (($staged.Key | Select-Object -Unique) -join ',') @yearArgs --source $staging --no-pause
}
$code = $LASTEXITCODE
if ($buildWarnings) { Write-Host "  Some builds failed and were not deployed - see the warnings above." -ForegroundColor Red; exit 1 }
exit $code
