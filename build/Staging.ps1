# Staging: turns the add-ins' build outputs into a setup payload (catalog.json + <Key>\<year>\).
# Dot-source it; runs in Windows PowerShell 5.1 and PowerShell 7. Used by Build-Installer.ps1, Deploy.ps1 and build\Test-Staging.ps1.

function Get-AbsolutePath([string]$Path) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path) }

# True when no folder between $Base and $Path is named bin or obj or starts with a dot.
function Test-SourcePath([string]$Base, [string]$Path) {
    $rel = Split-Path $Path.Substring($Base.Length) -Parent
    -not ($rel -split '\\' | Where-Object { $_ -eq 'bin' -or $_ -eq 'obj' -or $_.StartsWith('.') })
}

function Get-AddIns([string]$Root = (Split-Path $PSScriptRoot -Parent)) {
    $Root = Get-AbsolutePath $Root
    Get-ChildItem $Root -Recurse -File -Filter addin.props | Where-Object { Test-SourcePath $Root $_.FullName } | ForEach-Object {
        $p = ([xml][IO.File]::ReadAllText($_.FullName)).Project.PropertyGroup
        $dir = $_.DirectoryName
        $years = [string[]]($p.AddInYears -split ';')
        if (-not $years -or @($years | Where-Object { $_ -notmatch '\A\d{4}\z' }).Count) { throw "$dir`: AddInYears '$($p.AddInYears)' is not a ;-list of four-digit years" }
        if ($p.AddInKey -notmatch '\A[A-Za-z0-9._-]+\z') { throw "$dir`: AddInKey '$($p.AddInKey)' is not a plain name" }
        $assemblies = [string[]]($p.AddInAssemblies -split ';')
        $project = @(Get-ChildItem $dir -Recurse -File -Filter "$($assemblies[0]).csproj" | Where-Object { Test-SourcePath $dir $_.FullName })
        if ($project.Count -ne 1) { throw "$dir`: expected one $($assemblies[0]).csproj, found $($project.Count)" }
        [pscustomobject]@{
            Key = $p.AddInKey; Name = $p.AddInName; Manifest = $p.AddInManifest; ClientId = $p.AddInClientId
            EntryClass = $p.AddInEntryClass; Version = $p.AddInVersion
            Years = $years; Assemblies = $assemblies
            ThirdParty = ($p.AddInThirdParty -eq 'true'); Dir = $dir; Project = $project[0].FullName
        }
    }
}

function Get-Tfm([string]$Year) {
    switch ($Year) {
        { $_ -in '2023', '2024' } { 'net48'; break }
        { $_ -in '2025', '2026' } { 'net8.0-windows'; break }
        '2027' { 'net10.0-windows'; break }
        default { throw "Unknown Revit year '$Year'" }
    }
}

function Copy-AddInOutput([string]$OutputDir, [string]$Tfm, [string]$TargetDir) {
    $OutputDir = Get-AbsolutePath $OutputDir
    $TargetDir = Get-AbsolutePath $TargetDir
    if (-not (Test-Path $OutputDir)) { throw "Build output not found: $OutputDir" }
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    Copy-Item "$OutputDir\*.dll", "$OutputDir\*.pdb", "$OutputDir\*.deps.json" $TargetDir -Force -ErrorAction Stop
    if (Test-Path "$OutputDir\x64") { Copy-Item "$OutputDir\x64\*.dll" $TargetDir -Force -ErrorAction Stop }
    $nativeSqlite = "$OutputDir\runtimes\win-x64\native\e_sqlite3.dll"
    if (Test-Path $nativeSqlite) { Copy-Item $nativeSqlite $TargetDir -Force -ErrorAction Stop }

    # net48: strip BCL polyfill DLLs Revit preloads (shipping newer ones causes
    # "conflicts with same preloaded module" API_ERRORs -> 0xc0000005 crashes).
    if ($Tfm -eq 'net48') {
        @(
            "System.Memory.dll", "System.Runtime.CompilerServices.Unsafe.dll",
            "System.Buffers.dll", "System.Numerics.Vectors.dll",
            "Microsoft.Bcl.HashCode.dll", "System.ValueTuple.dll",
            "System.Text.Json.dll", "System.Text.Encodings.Web.dll",
            "EntityFramework.dll", "EntityFramework.SqlServer.dll",
            "System.Data.SQLite.EF6.dll", "System.Data.SQLite.Linq.dll"
        ) | ForEach-Object { if (Test-Path "$TargetDir\$_") { Remove-Item "$TargetDir\$_" -Force } }
    }
}

function Write-Json([string]$Path, $Value) {
    [IO.File]::WriteAllText((Get-AbsolutePath $Path), ($Value | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding $false))
}

function New-Staging {
    param(
        [Parameter(Mandatory)][string]$StagingDir,
        [string[]]$AddIn, [string[]]$Years,
        [switch]$Build, [switch]$Strict, [switch]$CatalogOnly,
        [string]$Root = (Split-Path $PSScriptRoot -Parent)
    )
    $ErrorActionPreference = 'Stop'
    $StagingDir = Get-AbsolutePath $StagingDir
    $Root = Get-AbsolutePath $Root

    if ((Test-Path $StagingDir) -and (Get-ChildItem $StagingDir -Force | Select-Object -First 1) -and -not (Test-Path "$StagingDir\catalog.json")) {
        throw "$StagingDir does not look like a staging folder"
    }
    if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }
    New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

    $built = [DateTime]::UtcNow.ToString('s') + 'Z'
    $commit = git -C $Root rev-parse --short HEAD
    if (git -C $Root status --porcelain) { $commit += '-dirty' }

    $addIns = @(Get-AddIns -Root $Root | Where-Object { -not $AddIn -or $AddIn -contains $_.Key })
    $staged = @()
    foreach ($a in $addIns) {
        foreach ($year in $a.Years | Where-Object { -not $Years -or $Years -contains $_ }) {
            if (-not $CatalogOnly) {
                $tfm = Get-Tfm $year
                $projectDir = Split-Path $a.Project -Parent
                $outputDir = "$projectDir\bin\$year\Release$year\$tfm"
                $ok = $true; $log = $null
                if ($Build) {
                    $log = & { $ErrorActionPreference = 'Continue'; dotnet build $a.Project -c "Release$year" --nologo -v q 2>&1 }
                    $ok = $LASTEXITCODE -eq 0
                }
                if (-not $ok -or -not (Test-Path "$outputDir\$($a.Assemblies[0]).dll")) {
                    $why = "$($a.Key) $year`: build failed or $($a.Assemblies[0]).dll missing in $outputDir"
                    if ($log) { $why += "`n" + (($log | Select-Object -Last 12) -join "`n") }
                    if ($Strict) { throw $why }
                    Write-Warning $why
                    continue
                }
                $target = "$StagingDir\$($a.Key)\$year"
                Copy-AddInOutput -OutputDir $outputDir -Tfm $tfm -TargetDir $target
                Write-Json "$target\addin.json" ([ordered]@{
                    key = $a.Key; version = $a.Version; commit = $commit; built = $built; manifest = $a.Manifest; dev = $true })
            }
            $staged += [pscustomobject]@{ Key = $a.Key; Year = $year }
        }
    }

    $entries = @(foreach ($a in $addIns) {
        $ys = [string[]]@($staged | Where-Object { $_.Key -eq $a.Key } | ForEach-Object { $_.Year })
        if ($ys.Count -eq 0) { continue }
        [ordered]@{
            key = $a.Key; name = $a.Name; manifest = $a.Manifest; entryClass = $a.EntryClass
            assembly = "$($a.Assemblies[0]).dll"; clientId = $a.ClientId; version = $a.Version
            dev = $true; years = $ys; replaces = @()
        }
    })
    Write-Json "$StagingDir\catalog.json" ([ordered]@{ built = $built; commit = $commit; addIns = $entries })
    $staged
}
