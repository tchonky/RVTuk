#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"
$root = Split-Path $MyInvocation.MyCommand.Path -Parent

$addinName    = "ReviTchucky"
$clientId     = "D71D7480-4A21-474E-A47E-3E8DF8C1BDA5"
$className    = "ReviTchucky.Revit.Application"
$vendorId     = "KnafoKlimor"
$vendorDesc   = "Knafo Klimor Architects LTD"

$versions = @{
    "2023" = @{ Config = "Release2023"; Tfm = "net48" }
    "2024" = @{ Config = "Release2024"; Tfm = "net48" }
    "2025" = @{ Config = "Release2025"; Tfm = "net8.0-windows" }
}

$addinsBase = "C:\ProgramData\Autodesk\Revit\Addins"

foreach ($ver in $versions.Keys | Sort-Object) {
    $info      = $versions[$ver]
    $config    = $info.Config
    $tfm       = $info.Tfm

    # SDK-style output lands in bin\<year>\<config>\<tfm>\
    $srcDir    = "$root\src\ReviTchucky.Revit\bin\$ver\$config\$tfm"

    # Fall back to flat layout (pre-SDK builds without TFM subfolder)
    if (-not (Test-Path "$srcDir\ReviTchucky.Revit.dll")) {
        $srcDir = "$root\src\ReviTchucky.Revit\bin\$ver\$config"
    }

    $addinsDir = "$addinsBase\$ver"
    $dllDir    = "$addinsDir\$addinName"
    $addinFile = "$addinsDir\$addinName.addin"

    if (-not (Test-Path "$srcDir\ReviTchucky.Revit.dll")) {
        Write-Warning "Build output not found for Revit $ver ($srcDir) — skipping. Run: dotnet build -c $config"
        continue
    }

    Write-Host "Deploying Revit $ver..." -ForegroundColor Cyan

    # Wipe the entire folder so stale DLLs (including x64\SQLite.Interop.dll) can't survive
    if (Test-Path $dllDir) { Remove-Item $dllDir -Recurse -Force }
    New-Item -ItemType Directory -Path $dllDir -Force | Out-Null
    Copy-Item "$srcDir\*.dll" $dllDir -Force
    Copy-Item "$srcDir\*.pdb" $dllDir -Force -ErrorAction SilentlyContinue
    Copy-Item "$srcDir\*.json" $dllDir -Force -ErrorAction SilentlyContinue

    # Native interop DLLs live in x64\ subdir; copy flat since Revit is always x64
    $x64Dir = "$srcDir\x64"
    if (Test-Path $x64Dir) {
        Copy-Item "$x64Dir\*.dll" $dllDir -Force
    }

    # For net48 builds, remove BCL polyfill DLLs that Revit already has preloaded.
    # Shipping newer versions causes "conflicts with same preloaded module" API_ERRORs
    # that result in 0xc0000005 access violations. Let the CLR fall back to Revit's
    # already-loaded versions; any API gap is caught by try/catch, not a native crash.
    if ($tfm -eq "net48") {
        @(
            "System.Memory.dll", "System.Runtime.CompilerServices.Unsafe.dll",
            "System.Buffers.dll", "System.Numerics.Vectors.dll",
            "Microsoft.Bcl.HashCode.dll", "System.ValueTuple.dll",
            "System.Text.Json.dll", "System.Text.Encodings.Web.dll",
            # System.Data.SQLite EF6/Linq extensions — we use raw ADO.NET only
            "EntityFramework.dll", "EntityFramework.SqlServer.dll",
            "System.Data.SQLite.EF6.dll", "System.Data.SQLite.Linq.dll"
        ) | ForEach-Object {
            $f = "$dllDir\$_"
            if (Test-Path $f) { Remove-Item $f -Force }
        }
    }

    @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>$addinName</Name>
    <Assembly>$addinName\ReviTchucky.Revit.dll</Assembly>
    <FullClassName>$className</FullClassName>
    <ClientId>$clientId</ClientId>
    <VendorId>$vendorId</VendorId>
    <VendorDescription>$vendorDesc</VendorDescription>
  </AddIn>
</RevitAddIns>
"@ | Out-File $addinFile -Encoding utf8 -Force

    Write-Host "  DLLs   -> $dllDir" -ForegroundColor Green
    Write-Host "  .addin -> $addinFile" -ForegroundColor Green
}

Write-Host "`nDone. Restart Revit to load the add-in." -ForegroundColor Yellow
