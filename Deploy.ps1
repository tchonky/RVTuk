#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Builds-output deployer for the RVTuk Revit add-in (2023 / 2024 / 2025).
.DESCRIPTION
    Copies each version's build output into the per-year Revit Addins folder and
    writes the .addin manifest. Each version deploys independently: if one year's
    Revit is open (locking its DLLs) or its build output is missing, that year is
    skipped with a warning and the others still deploy.
.PARAMETER Only
    Optional list of versions to deploy (e.g. ".\Deploy.ps1 2024 2025"). Defaults to all.
.EXAMPLE
    .\Deploy.ps1            # deploy every version that isn't locked/missing
    .\Deploy.ps1 2024       # deploy only Revit 2024
#>
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$Only
)

# Per-version failures must not abort the run; we handle errors explicitly per year.
$ErrorActionPreference = "Continue"
$root = Split-Path $MyInvocation.MyCommand.Path -Parent

$addinName    = "RVTuk"
$clientId     = "D71D7480-4A21-474E-A47E-3E8DF8C1BDA5"
$className    = "RVTuk.Revit.Application"
$vendorId     = "KnafoKlimor"
$vendorDesc   = "Knafo Klimor Architects LTD"

$versions = [ordered]@{
    "2023" = @{ Config = "Release2023"; Tfm = "net48" }
    "2024" = @{ Config = "Release2024"; Tfm = "net48" }
    "2025" = @{ Config = "Release2025"; Tfm = "net8.0-windows" }
}

$addinsBase = "C:\ProgramData\Autodesk\Revit\Addins"

# --- helpers ---------------------------------------------------------------

function Write-Banner {
    $line = "=" * 56
    Write-Host ""
    Write-Host "  $line" -ForegroundColor DarkCyan
    Write-Host "   RVTuk deploy" -ForegroundColor White -NoNewline
    Write-Host "  -  $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor DarkGray
    Write-Host "  $line" -ForegroundColor DarkCyan
    Write-Host ""
}

# Map each running Revit.exe to its version year via its install path
# (e.g. "...\Revit 2023\Revit.exe"). Lets us skip a year whose Revit is open.
function Get-RunningRevitYears {
    $years = @()
    foreach ($p in Get-Process -Name "Revit" -ErrorAction SilentlyContinue) {
        try {
            if ($p.Path -match "Revit\s+(\d{4})") { $years += $Matches[1] }
        } catch { }  # Access denied reading Path on some processes — ignore
    }
    return ($years | Sort-Object -Unique)
}

# --- run -------------------------------------------------------------------

Write-Banner

$runningYears = Get-RunningRevitYears
if ($runningYears.Count -gt 0) {
    Write-Host "  Revit running: " -NoNewline -ForegroundColor DarkGray
    Write-Host ($runningYears -join ", ") -ForegroundColor Yellow
    Write-Host ""
}

# Resolve which versions to attempt
$targets = $versions.Keys
if ($Only) {
    $targets = $versions.Keys | Where-Object { $Only -contains $_ }
    $unknown = $Only | Where-Object { -not $versions.Contains($_) }
    foreach ($u in $unknown) { Write-Warning "Unknown version '$u' — valid: $($versions.Keys -join ', ')" }
    if (-not $targets) { Write-Host "Nothing to deploy." -ForegroundColor Red; return }
}

# Status of each year for the closing summary
$results = [ordered]@{}

foreach ($ver in $targets) {
    $info   = $versions[$ver]
    $config = $info.Config
    $tfm    = $info.Tfm

    Write-Host ("  Revit {0} " -f $ver) -ForegroundColor Cyan -NoNewline
    Write-Host "[$config]" -ForegroundColor DarkGray

    # 1) Skip if this year's Revit is open — its add-in DLLs are loaded and locked.
    if ($runningYears -contains $ver) {
        Write-Host "    SKIP  Revit $ver is open (files are locked). Close it to update." -ForegroundColor Yellow
        $results[$ver] = "skipped (Revit open)"
        Write-Host ""
        continue
    }

    # 2) Locate build output (SDK TFM subfolder, with flat fallback).
    $srcDir = "$root\src\RVTuk.Revit\bin\$ver\$config\$tfm"
    if (-not (Test-Path "$srcDir\RVTuk.Revit.dll")) {
        $srcDir = "$root\src\RVTuk.Revit\bin\$ver\$config"
    }
    if (-not (Test-Path "$srcDir\RVTuk.Revit.dll")) {
        Write-Host "    SKIP  build output not found. Run: dotnet build -c $config" -ForegroundColor Yellow
        $results[$ver] = "skipped (not built)"
        Write-Host ""
        continue
    }

    $addinsDir = "$addinsBase\$ver"
    $dllDir    = "$addinsDir\$addinName"
    $addinFile = "$addinsDir\$addinName.addin"

    try {
        # Wipe the whole folder so stale DLLs (incl. x64\SQLite.Interop.dll) can't survive.
        if (Test-Path $dllDir) { Remove-Item $dllDir -Recurse -Force -ErrorAction Stop }
        New-Item -ItemType Directory -Path $dllDir -Force -ErrorAction Stop | Out-Null

        Copy-Item "$srcDir\*.dll" $dllDir -Force -ErrorAction Stop
        Copy-Item "$srcDir\*.pdb" $dllDir -Force -ErrorAction SilentlyContinue
        Copy-Item "$srcDir\*.json" $dllDir -Force -ErrorAction SilentlyContinue

        # Native interop DLLs live in x64\; copy flat since Revit is always x64.
        $x64Dir = "$srcDir\x64"
        if (Test-Path $x64Dir) { Copy-Item "$x64Dir\*.dll" $dllDir -Force -ErrorAction Stop }

        # net48: strip BCL polyfill DLLs Revit preloads (shipping newer ones causes
        # "conflicts with same preloaded module" API_ERRORs -> 0xc0000005 crashes).
        if ($tfm -eq "net48") {
            @(
                "System.Memory.dll", "System.Runtime.CompilerServices.Unsafe.dll",
                "System.Buffers.dll", "System.Numerics.Vectors.dll",
                "Microsoft.Bcl.HashCode.dll", "System.ValueTuple.dll",
                "System.Text.Json.dll", "System.Text.Encodings.Web.dll",
                "EntityFramework.dll", "EntityFramework.SqlServer.dll",
                "System.Data.SQLite.EF6.dll", "System.Data.SQLite.Linq.dll"
            ) | ForEach-Object {
                $f = "$dllDir\$_"
                if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
            }
        }

        @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>$addinName</Name>
    <Assembly>$addinName\RVTuk.Revit.dll</Assembly>
    <FullClassName>$className</FullClassName>
    <ClientId>$clientId</ClientId>
    <VendorId>$vendorId</VendorId>
    <VendorDescription>$vendorDesc</VendorDescription>
  </AddIn>
</RevitAddIns>
"@ | Out-File $addinFile -Encoding utf8 -Force -ErrorAction Stop

        $dllCount = (Get-ChildItem "$dllDir\*.dll" -ErrorAction SilentlyContinue | Measure-Object).Count
        Write-Host "    OK    $dllCount DLLs -> $dllDir" -ForegroundColor Green
        Write-Host "          manifest -> $addinFile" -ForegroundColor DarkGreen
        $results[$ver] = "deployed"
    }
    catch [System.IO.IOException] {
        # Almost always a locked file: Revit (this year) has the DLL open.
        Write-Host "    SKIP  files are locked (Revit $ver likely open): $($_.Exception.Message)" -ForegroundColor Yellow
        $results[$ver] = "skipped (locked)"
    }
    catch {
        Write-Host "    FAIL  $($_.Exception.Message)" -ForegroundColor Red
        $results[$ver] = "FAILED"
    }
    Write-Host ""
}

# --- summary ---------------------------------------------------------------

Write-Host "  " ("-" * 54) -ForegroundColor DarkCyan
Write-Host "  Summary" -ForegroundColor White
foreach ($ver in $results.Keys) {
    $status = $results[$ver]
    $color = switch -Wildcard ($status) {
        "deployed"  { "Green" }
        "FAILED"    { "Red" }
        default     { "Yellow" }  # skipped (*)
    }
    Write-Host ("    Revit {0}  : " -f $ver) -NoNewline -ForegroundColor DarkGray
    Write-Host $status -ForegroundColor $color
}
Write-Host ""

if ($results.Values -contains "deployed") {
    Write-Host "  Done. Restart the affected Revit version(s) to load the add-in." -ForegroundColor Yellow
} else {
    Write-Host "  Nothing deployed." -ForegroundColor Red
}
Write-Host ""
