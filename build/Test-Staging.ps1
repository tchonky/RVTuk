#Requires -Version 7.0
# Checks that every staged add-in folder holds exactly what it should. Without -StagingDir it stages everything itself.
param([string]$StagingDir, [string]$Solution)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Staging.ps1"
$root = Split-Path $PSScriptRoot -Parent
$Solution = Get-AbsolutePath $(if ($Solution) { $Solution } else { "$root\RVTuk.sln" })
$temp = $null
try {
    if ($StagingDir) { $StagingDir = Get-AbsolutePath $StagingDir }
    else {
        $temp = $StagingDir = Join-Path $env:TEMP "rvtuk-staging-check-$([guid]::NewGuid().ToString('N'))"
        New-Staging -StagingDir $StagingDir -Build -Strict | Out-Null
    }

    $fails = [Collections.Generic.List[string]]::new()
    $addIns = @(Get-AddIns -Root $root)
    $own = @($addIns | ForEach-Object { $_.Assemblies } | ForEach-Object { "$_.dll" })
    $thirdParty = @{
        'net48'          = 'e_sqlite3.dll', 'Microsoft.Data.Sqlite.dll', 'OpenMcdf.dll', 'SQLitePCLRaw.batteries_v2.dll', 'SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.dynamic_cdecl.dll'
        'net8.0-windows' = 'e_sqlite3.dll', 'Microsoft.Data.Sqlite.dll', 'OpenMcdf.dll', 'SQLitePCLRaw.batteries_v2.dll', 'SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll', 'System.Drawing.Common.dll'
    }
    $thirdParty['net10.0-windows'] = $thirdParty['net8.0-windows'] | Where-Object { $_ -ne 'System.Drawing.Common.dll' }
    $frameworks = @{ 'net48' = '.NETFramework,Version=v4.8'; 'net8.0-windows' = '.NETCoreApp,Version=v8.0'; 'net10.0-windows' = '.NETCoreApp,Version=v10.0' }
    $theme = [Text.Encoding]::Latin1.GetString([Text.Encoding]::Unicode.GetBytes('shared/themes/darktheme.baml'))
    $catalog = [IO.File]::ReadAllText("$StagingDir\catalog.json") | ConvertFrom-Json
    $folders = 0
    $vendor = [Collections.Generic.List[object]]::new()

    foreach ($e in $catalog.addIns) {
        $a = $addIns | Where-Object Key -eq $e.key
        foreach ($year in @($e.years)) {
            $folders++
            $dir = "$StagingDir\$($e.key)\$year"; $tag = "$($e.key)\$year"
            $tfm = Get-Tfm -Year $year
            $hostName = $a.Assemblies[0]

            # 1. Exact file set.
            $expected = @($a.Assemblies | ForEach-Object { "$_.dll"; "$_.pdb" }) + 'addin.json'
            if ($tfm -ne 'net48') { $expected += "$hostName.deps.json" }
            if ($a.ThirdParty) { $expected += $thirdParty[$tfm] }
            $actual = @(Get-ChildItem $dir -File -ErrorAction SilentlyContinue | ForEach-Object Name)
            $expected | Where-Object { $_ -notin $actual } | ForEach-Object { $fails.Add("FAIL $tag`: missing $_") }
            $actual | Where-Object { $_ -notin $expected } | ForEach-Object { $fails.Add("FAIL $tag`: unexpected $_") }

            # 2. Collect vendor DLLs per year.
            $actual | Where-Object { $_ -like '*.dll' -and $_ -notin $own } | ForEach-Object { $vendor.Add([pscustomobject]@{ Year = $year; Dll = $_; Key = $e.key }) }

            # 3. The host assembly, read from a byte copy (never Assembly.Load: it locks the file).
            if (Test-Path "$dir\$hostName.dll") {
                $pe = [Reflection.PortableExecutable.PEReader]::new([IO.MemoryStream]::new([IO.File]::ReadAllBytes("$dir\$hostName.dll")))
                try {
                    $md = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
                    $types = $md.TypeDefinitions | ForEach-Object { $t = $md.GetTypeDefinition($_); "$($md.GetString($t.Namespace)).$($md.GetString($t.Name))" }
                    if ($e.entryClass -notin $types) { $fails.Add("FAIL $tag`: entry class $($e.entryClass) not found in $hostName.dll") }
                    $api = $md.AssemblyReferences | ForEach-Object { $md.GetAssemblyReference($_) } | Where-Object { $md.GetString($_.Name) -eq 'RevitAPI' }
                    $major = [int]$year - 2000
                    if (-not $api -or $api.Version.Major -ne $major) { $fails.Add("FAIL $tag`: RevitAPI reference is version $($api.Version.Major), expected $major") }
                    $fw = foreach ($h in $md.GetAssemblyDefinition().GetCustomAttributes()) {
                        $ca = $md.GetCustomAttribute($h)
                        if ($ca.Constructor.Kind -ne 'MemberReference') { continue }
                        $mr = $md.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$ca.Constructor)
                        if ($mr.Parent.Kind -ne 'TypeReference' -or $md.GetString($md.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$mr.Parent).Name) -ne 'TargetFrameworkAttribute') { continue }
                        $b = $md.GetBlobBytes($ca.Value)   # prolog (2 bytes), length (1 byte for short strings), UTF-8
                        [Text.Encoding]::UTF8.GetString($b, 3, $b[2])
                    }
                    if ($fw -ne $frameworks[$tfm]) { $fails.Add("FAIL $tag`: target framework is '$fw', expected '$($frameworks[$tfm])'") }
                } finally { $pe.Dispose() }
            }

            # 4. The theme in the UI assembly.
            $ui = $a.Assemblies | Where-Object { $_ -like '*.UI' }
            if (Test-Path "$dir\$ui.dll") {
                if (-not [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes("$dir\$ui.dll")).Contains($theme)) {
                    $fails.Add("FAIL $tag`: $ui.dll does not contain shared/themes/darktheme.baml")
                }
            }

            # 5. addin.json agrees with the catalog.
            try {
                $j = [IO.File]::ReadAllText("$dir\addin.json") | ConvertFrom-Json
                foreach ($f in 'key', 'version', 'manifest') {
                    if ($j.$f -ne $e.$f) { $fails.Add("FAIL $tag`: addin.json $f is '$($j.$f)', catalog says '$($e.$f)'") }
                }
            } catch { $fails.Add("FAIL $tag`: addin.json does not parse") }
        }
    }

    if ($folders -eq 0) { $fails.Add("FAIL nothing was staged") }
    if ($temp) {   # staged here, so every add-in year must be in it
        foreach ($a in $addIns) {
            foreach ($y in $a.Years) {
                if (-not ($catalog.addIns | Where-Object { $_.key -eq $a.Key -and @($_.years) -contains $y })) { $fails.Add("FAIL $($a.Key)\$y`: not staged") }
            }
        }
    }

    # 2. No vendor DLL in two add-in folders of one year.
    $vendor | Group-Object Year, Dll | Where-Object Count -gt 1 | ForEach-Object {
        $fails.Add("FAIL $($_.Group[0].Year): $($_.Group[0].Dll) is in $($_.Group.Key -join ' and ')")
    }

    # 6. The solution builds each add-in for exactly its Years.
    $sln = [IO.File]::ReadAllLines($Solution)
    foreach ($a in $addIns) {
        $line = $sln | Where-Object { $_.StartsWith('Project(') -and $_.Contains("`"$($a.Assemblies[0])`"") } | Select-Object -First 1
        $guid = if ($line) { [regex]::Match($line, '\{[0-9A-Fa-f-]+\}(?="\s*$)').Value }
        $built = @($sln | ForEach-Object { if ($guid -and $_.Trim().StartsWith("$guid.Release") -and $_ -match '\.Release(\d{4})\|Any CPU\.Build\.0') { $Matches[1] } })
        if (Compare-Object @($a.Years) $built) { $fails.Add("FAIL $($a.Key): $(Split-Path $Solution -Leaf) builds it for $($built -join ',') but addin.props names $($a.Years -join ',')") }
    }

    # 7. No hard-wired theme path.
    $grep = git -C $root grep --untracked -n -F "RVTuk.UI;component" -- src
    if ($LASTEXITCODE -eq 0) { $fails.Add("FAIL hard-wired theme path:"); $grep | ForEach-Object { $fails.Add("FAIL   $_") } }
    elseif ($LASTEXITCODE -ne 1) { $fails.Add("FAIL git grep could not run") }

    if ($fails.Count) { $fails | ForEach-Object { Write-Host $_ }; throw "Staging check failed: $($fails.Count)" }
    Write-Host "Staging check passed: $folders folders"
} finally {
    if ($temp) { Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue }
}
$global:LASTEXITCODE = 0
