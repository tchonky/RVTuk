<#
.SYNOPSIS
    Builds dist\RVTukSetup.exe — a standalone installer for other computers.
.DESCRIPTION
    Stages every add-in (each addin.props, every year) with build\Staging.ps1 into
    installer\payload-staging (catalog.json + <AddIn>\<year>\), zips it into
    installer\RVTuk.Setup\payload.zip, and compiles the RVTuk.Setup console exe with that
    zip embedded. The resulting exe is copied to dist\RVTukSetup.exe and can be run on any
    machine — it self-elevates, detects the installed Revit versions, and copies each
    add-in into the machine-wide Addins folder of each year (ProgramData up to Revit 2026,
    Program Files from 2027).
.EXAMPLE
    .\Build-Installer.ps1
#>
$ErrorActionPreference = "Stop"
$root = Split-Path $MyInvocation.MyCommand.Path -Parent
. "$root\build\Staging.ps1"

$staging  = "$root\installer\payload-staging"
$zipPath  = "$root\installer\RVTuk.Setup\payload.zip"
$setupPrj = "$root\installer\RVTuk.Setup\RVTuk.Setup.csproj"
$distDir  = "$root\dist"

Write-Host ""
Write-Host "  Building RVTukSetup.exe" -ForegroundColor White
Write-Host ""

# Build fresh from a wiped bin so the payload is exactly the current dependency closure —
# a stale bin can carry DLLs from dropped packages (e.g. the old System.Data.SQLite's
# x64\SQLite.Interop.dll) into every installed machine.
foreach ($a in Get-AddIns -Root $root) {
    foreach ($year in $a.Years) {
        $binDir = "$(Split-Path $a.Project -Parent)\bin\$year"
        if (Test-Path $binDir) { Remove-Item $binDir -Recurse -Force }
    }
}

# The folder is this script's own; it may still hold the old layout, which New-Staging refuses to wipe.
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Staging -StagingDir $staging -Build -Strict | ForEach-Object { Write-Host "  Revit $($_.Year)  $($_.Key)  staged" -ForegroundColor DarkGreen }
$commit = (Get-Content "$staging\catalog.json" -Raw | ConvertFrom-Json).commit

# Zip + compile the installer with the payload embedded.
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipPath)

Write-Host ""
Write-Host "  build RVTuk.Setup ..." -NoNewline -ForegroundColor Cyan
$buildLog = & dotnet build $setupPrj -c Release --nologo -v minimal 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host " FAILED" -ForegroundColor Red
    $buildLog | Select-Object -Last 12 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkYellow }
    throw "Installer build failed."
}
Write-Host " ok" -ForegroundColor DarkGreen

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
Copy-Item "$root\installer\RVTuk.Setup\bin\Release\net48\RVTukSetup.exe" "$distDir\RVTukSetup.exe" -Force

$size = [math]::Round((Get-Item "$distDir\RVTukSetup.exe").Length / 1MB, 1)
Write-Host ""
Write-Host "  Done: " -NoNewline
Write-Host "dist\RVTukSetup.exe" -ForegroundColor Green -NoNewline
Write-Host "  ($size MB, source $commit)"
Write-Host "  Copy it to another computer and double-click to install." -ForegroundColor DarkGray
Write-Host ""
