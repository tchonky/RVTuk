<#
.SYNOPSIS
    Builds dist\RVTukSetup.exe — a standalone installer for other computers.
.DESCRIPTION
    Builds every Revit-year config, stages each year's add-in payload (same copy/strip
    rules as Deploy.ps1: flat DLLs, e_sqlite3.dll, net48 BCL-polyfill strip) plus its
    .addin manifest, zips it into installer\RVTuk.Setup\payload.zip, and compiles the
    RVTuk.Setup console exe with that zip embedded. The resulting exe is copied to
    dist\RVTukSetup.exe and can be run on any machine — it self-elevates, detects the
    installed Revit versions, and copies the add-in into ProgramData.

    NOTE: the version table and staging rules below must stay in sync with Deploy.ps1.
.EXAMPLE
    .\Build-Installer.ps1
#>
$ErrorActionPreference = "Stop"
$root = Split-Path $MyInvocation.MyCommand.Path -Parent

$vendorId   = "KnafoKlimor"
$vendorDesc = "Knafo Klimor Architects LTD"

# Same table as Deploy.ps1 — 2023 ships KKarea (Area Calc only), 2024/2025 ship RVTuk.
# ClientIds must never change once deployed.
$versions = [ordered]@{
    "2023" = @{ Config = "Release2023"; Tfm = "net48"
                Addin = "KKarea"; Project = "KKarea.Revit"
                ClassName = "KKarea.Revit.Application"
                ClientId = "9C97B9F2-60F9-432D-92A4-5EC2A0FDAFFC" }
    "2024" = @{ Config = "Release2024"; Tfm = "net48"
                Addin = "RVTuk"; Project = "RVTuk.Revit"
                ClassName = "RVTuk.Revit.Application"
                ClientId = "D71D7480-4A21-474E-A47E-3E8DF8C1BDA5" }
    "2025" = @{ Config = "Release2025"; Tfm = "net8.0-windows"
                Addin = "RVTuk"; Project = "RVTuk.Revit"
                ClassName = "RVTuk.Revit.Application"
                ClientId = "D71D7480-4A21-474E-A47E-3E8DF8C1BDA5" }
}

$staging  = "$root\installer\payload-staging"
$zipPath  = "$root\installer\RVTuk.Setup\payload.zip"
$setupPrj = "$root\installer\RVTuk.Setup\RVTuk.Setup.csproj"
$distDir  = "$root\dist"

Write-Host ""
Write-Host "  Building RVTukSetup.exe" -ForegroundColor White
Write-Host ""

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null

foreach ($ver in $versions.Keys) {
    $info = $versions[$ver]

    # 1) Build fresh from a wiped bin so the payload is exactly the current dependency
    #    closure — a stale bin can carry DLLs from dropped packages (e.g. the old
    #    System.Data.SQLite's x64\SQLite.Interop.dll) into every installed machine.
    $binDir = "$root\src\$($info.Project)\bin\$ver"
    if (Test-Path $binDir) { Remove-Item $binDir -Recurse -Force }
    Write-Host "  Revit $ver  build $($info.Config) ..." -NoNewline -ForegroundColor Cyan
    $proj = "$root\src\$($info.Project)\$($info.Project).csproj"
    $buildLog = & dotnet build $proj -c $info.Config --nologo -v minimal 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host " FAILED" -ForegroundColor Red
        $buildLog | Select-Object -Last 12 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkYellow }
        throw "Build failed for $($info.Config) — installer must contain every year."
    }
    Write-Host " ok" -ForegroundColor DarkGreen

    # 2) Locate build output (SDK TFM subfolder, with flat fallback).
    $assemblyDll = "$($info.Project).dll"
    $srcDir = "$root\src\$($info.Project)\bin\$ver\$($info.Config)\$($info.Tfm)"
    if (-not (Test-Path "$srcDir\$assemblyDll")) { $srcDir = "$root\src\$($info.Project)\bin\$ver\$($info.Config)" }
    if (-not (Test-Path "$srcDir\$assemblyDll")) { throw "Build output not found under bin\$ver\$($info.Config)." }

    # 3) Stage flat, same rules as Deploy.ps1.
    $dllDir = "$staging\$ver\$($info.Addin)"
    New-Item -ItemType Directory -Path $dllDir -Force | Out-Null
    Copy-Item "$srcDir\*.dll" $dllDir -Force
    Copy-Item "$srcDir\*.pdb" $dllDir -Force -ErrorAction SilentlyContinue
    Copy-Item "$srcDir\*.json" $dllDir -Force -ErrorAction SilentlyContinue
    Copy-Item "$srcDir\*.txt" $dllDir -Force -ErrorAction SilentlyContinue   # shared-parameter files
    if (Test-Path "$srcDir\x64") { Copy-Item "$srcDir\x64\*.dll" $dllDir -Force }
    $nativeSqlite = "$srcDir\runtimes\win-x64\native\e_sqlite3.dll"
    if (Test-Path $nativeSqlite) { Copy-Item $nativeSqlite $dllDir -Force }

    # net48: strip BCL polyfill DLLs Revit preloads (shipping newer ones causes
    # "conflicts with same preloaded module" API_ERRORs -> 0xc0000005 crashes).
    if ($info.Tfm -eq "net48") {
        @(
            "System.Memory.dll", "System.Runtime.CompilerServices.Unsafe.dll",
            "System.Buffers.dll", "System.Numerics.Vectors.dll",
            "Microsoft.Bcl.HashCode.dll", "System.ValueTuple.dll",
            "System.Text.Json.dll", "System.Text.Encodings.Web.dll",
            "EntityFramework.dll", "EntityFramework.SqlServer.dll",
            "System.Data.SQLite.EF6.dll", "System.Data.SQLite.Linq.dll"
        ) | ForEach-Object {
            if (Test-Path "$dllDir\$_") { Remove-Item "$dllDir\$_" -Force }
        }
    }

    # 4) .addin manifest next to the folder, as Revit expects.
    @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>$($info.Addin)</Name>
    <Assembly>$($info.Addin)\$assemblyDll</Assembly>
    <FullClassName>$($info.ClassName)</FullClassName>
    <ClientId>$($info.ClientId)</ClientId>
    <VendorId>$vendorId</VendorId>
    <VendorDescription>$vendorDesc</VendorDescription>
  </AddIn>
</RevitAddIns>
"@ | Out-File "$staging\$ver\$($info.Addin).addin" -Encoding utf8 -Force

    $count = (Get-ChildItem "$dllDir\*.dll" | Measure-Object).Count
    Write-Host "    staged $count DLLs" -ForegroundColor DarkGreen
}

# Version stamp shown in the installer banner.
$commit = (& git -C $root rev-parse --short HEAD 2>$null)
"$commit  built $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm')) UTC" |
    Out-File "$staging\version.txt" -Encoding utf8 -NoNewline

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
