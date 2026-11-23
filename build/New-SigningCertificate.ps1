<#
.SYNOPSIS
    Makes the office's code-signing certificate for RVTuk, once.

.DESCRIPTION
    Creates a self-signed code-signing certificate in this Windows user's certificate store
    (Cert:\CurrentUser\My) and writes its public part to installer\RVTukSigning.cer, which is
    committed. build\Staging.ps1 signs the add-ins' own DLLs with it; RVTukSetup.exe makes each
    office PC trust the .cer (Trusted Root + Trusted Publishers), so Revit loads RVTuk without the
    "Always Load" prompt, after updates too.

    The private key never leaves this store and cannot be exported: only this Windows user on this
    PC can sign. Lose it (new PC, new Windows profile) and run this again with -Replace; the next
    install then trusts the new certificate, and builds signed with the old one stop being trusted
    only if its .cer is removed from the PCs by hand.

    Anything signed with this key is trusted by every office PC that installed RVTuk. Do not sign
    anything else with it.

.PARAMETER Replace
    Make a new certificate although installer\RVTukSigning.cer exists.
#>
param([switch]$Replace)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cerPath = Join-Path $root 'installer\RVTukSigning.cer'

if ((Test-Path $cerPath) -and -not $Replace) {
    throw "$cerPath exists already. Use -Replace to make a new certificate."
}

$cert = New-SelfSignedCertificate -Type CodeSigningCert `
    -Subject 'CN=Knafo Klimor Architects - RVTuk, O=Knafo Klimor Architects LTD, C=IL' `
    -FriendlyName 'RVTuk code signing' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
    -KeyExportPolicy NonExportable `
    -NotAfter (Get-Date).AddYears(10)

Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
Write-Host "Made the RVTuk signing certificate, valid until $($cert.NotAfter.ToString('yyyy-MM-dd'))."
Write-Host "  Thumbprint: $($cert.Thumbprint)"
Write-Host "  Public part: $cerPath (commit it)"
Write-Host "  Private key: Cert:\CurrentUser\My, this Windows user only, not exportable"
