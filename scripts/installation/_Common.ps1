# Shared helpers for scripts\installation\*.ps1. Dot-sourced by every
# numbered step and by Install-ADDS-PIM.ps1 itself. Deliberately
# self-contained: nothing here calls out to scripts\*.ps1 or
# dev-scripts\*.ps1 - those trees are reference material only, not
# runtime dependencies of this installer (see .agent/plans for why).

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-AddsPimElevated {
    if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this installer from an elevated Windows PowerShell session.'
    }
}

function ConvertTo-AddsPimNormalizedThumbprint {
    param([Parameter(Mandatory)] [string] $Thumbprint)
    ($Thumbprint -replace '\s').ToUpperInvariant()
}

function Read-AddsPimValue {
    <# Prompts for a value with an optional default; keeps prompting until
       a non-empty value is available (either typed or the default). #>
    param(
        [Parameter(Mandatory)] [string] $Prompt,
        [string] $Default
    )
    $suffix = if ($Default) { " [$Default]" } else { '' }
    while ($true) {
        $response = Read-Host "$Prompt$suffix"
        if ([string]::IsNullOrWhiteSpace($response)) {
            if ($Default) { return $Default }
            Write-Host 'A value is required.' -ForegroundColor Yellow
            continue
        }
        return $response.Trim()
    }
}

function Read-AddsPimOptionalValue {
    <# Same as Read-AddsPimValue but an empty response is a valid answer
       (returns $null), used for "existing thumbprint or leave blank to
       generate one" style prompts. #>
    param(
        [Parameter(Mandatory)] [string] $Prompt,
        [string] $Default
    )
    $suffix = if ($Default) { " [$Default]" } else { ' [leave blank to generate]' }
    $response = Read-Host "$Prompt$suffix"
    if ([string]::IsNullOrWhiteSpace($response)) {
        if ($Default) { return $Default }
        return $null
    }
    return $response.Trim()
}

function Confirm-AddsPimYesNo {
    param(
        [Parameter(Mandatory)] [string] $Prompt,
        [bool] $DefaultYes = $false
    )
    $suffix = if ($DefaultYes) { '(Y/n)' } else { '(y/N)' }
    $response = Read-Host "$Prompt $suffix"
    if ([string]::IsNullOrWhiteSpace($response)) { return $DefaultYes }
    return $response -match '^[yYjJ]'
}

function Write-AddsPimStepHeader {
    param([Parameter(Mandatory)] [string] $Title)
    Write-Host ''
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

function Assert-AddsPimGmsaUsable {
    <# Validates the named gMSA is installed on this host and can retrieve
       its managed password. Never creates or delegates a gMSA - that
       remains separately-approved AD administration. #>
    param([Parameter(Mandatory)] [string] $Account)
    if ($Account -notmatch '^[^\\]+\\[^\\]+\$$') {
        throw "gMSA account '$Account' does not look like DOMAIN\name`$."
    }
    if (-not (Get-Module -ListAvailable -Name ActiveDirectory)) {
        throw 'RSAT Active Directory PowerShell tools are required to validate gMSAs. Run 01_HostPrerequisites.ps1 first.'
    }
    Import-Module ActiveDirectory -ErrorAction Stop
    $samAccountName = ($Account -split '\\', 2)[1]
    # Test-ADServiceAccount itself supports ShouldProcess, and $WhatIfPreference
    # flows to every ShouldProcess-aware cmdlet in scope - without this override,
    # a read-only validation check would silently no-op (and look like a
    # failure) whenever the calling step script is run with -WhatIf.
    $originalWhatIfPreference = $WhatIfPreference
    try {
        $WhatIfPreference = $false
        if (-not (Test-ADServiceAccount -Identity $samAccountName)) {
            throw "The gMSA $Account is not installed or cannot retrieve its managed password on this host. Install it (Install-ADServiceAccount) before continuing."
        }
    }
    finally { $WhatIfPreference = $originalWhatIfPreference }
}

function Resolve-AddsPimWritableDomainController {
    <# Returns the FQDN of a reachable, writable domain controller for the
       given domain. With -Preferred set, that DC is only validated (must
       exist, answer AD queries, and be writable - an RODC is rejected).
       Otherwise the AD DS locator discovers one, preferring this host's
       own AD site and then the next-closest site. Requires the RSAT
       ActiveDirectory module, which 01_HostPrerequisites.ps1 installs and
       which is needed anyway to validate the gMSAs. #>
    param(
        [Parameter(Mandatory)] [string] $DomainDnsName,
        [string] $Preferred
    )
    if (-not (Get-Module -ListAvailable -Name ActiveDirectory)) {
        throw 'RSAT Active Directory PowerShell tools are required to locate a domain controller. Run 01_HostPrerequisites.ps1 first.'
    }
    Import-Module ActiveDirectory -ErrorAction Stop

    if ($Preferred) {
        try { $controller = Get-ADDomainController -Identity $Preferred -Server $Preferred -ErrorAction Stop }
        catch { throw "The specified domain controller '$Preferred' could not be contacted: $_" }
        if ($controller.IsReadOnly) {
            throw "Domain controller '$Preferred' is a read-only domain controller (RODC). ADDS-PIM needs a writable controller for TTL membership writes and read-back verification."
        }
        $hostName = [string] $controller.HostName
    }
    else {
        Write-Host "Discovering a writable domain controller for $DomainDnsName ..."
        try { $controller = Get-ADDomainController -DomainName $DomainDnsName -Discover -Writable -NextClosestSite -ErrorAction Stop }
        catch { throw "Automatic domain controller discovery for '$DomainDnsName' failed: $_. Re-run the installer and supply a writable domain controller FQDN explicitly." }
        $hostName = @($controller.HostName)[0]
    }
    if ([string]::IsNullOrWhiteSpace($hostName)) {
        throw "Domain controller resolution for '$DomainDnsName' produced no host name."
    }

    try { $domain = Get-ADDomain -Server $hostName -ErrorAction Stop }
    catch { throw "The domain controller '$hostName' did not answer an Active Directory query: $_" }
    if (-not $domain.DNSRoot.Equals($DomainDnsName, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Domain controller '$hostName' serves domain '$($domain.DNSRoot)', not the expected '$DomainDnsName'."
    }
    return $hostName
}

function Assert-AddsPimPamFeatureEnabled {
    <# Confirms the Active Directory Privileged Access Management optional
       feature is enabled in the forest. ADDS-PIM's whole time-limited
       access model depends on it: it is what lets a group member be added
       with a TTL (expiring link), and what makes the KDC cap the Kerberos
       ticket lifetime to that TTL so access actually ends when the
       membership does. Enabling PAM needs forest functional level 2016 or
       higher, so an enabled feature also proves the forest is new enough. #>
    param([Parameter(Mandatory)] [string] $DomainController)
    if (-not (Get-Module -ListAvailable -Name ActiveDirectory)) {
        throw 'RSAT Active Directory PowerShell tools are required. Run 01_HostPrerequisites.ps1 first.'
    }
    Import-Module ActiveDirectory -ErrorAction Stop
    try { $feature = Get-ADOptionalFeature -Filter "Name -eq 'Privileged Access Management Feature'" -Server $DomainController -ErrorAction Stop }
    catch { throw "Could not query the Privileged Access Management optional feature on '$DomainController': $_" }
    if ($null -eq $feature -or @($feature.EnabledScopes).Count -eq 0) {
        throw @'
The Active Directory 'Privileged Access Management Feature' is not enabled in this forest.
ADDS-PIM cannot grant time-limited group memberships without it.
Enable it once (requires forest functional level 2016 or higher), then re-run the installer:
  Enable-ADOptionalFeature 'Privileged Access Management Feature' -Scope ForestOrConfigurationSet -Target (Get-ADForest).Name
'@
    }
    Write-Host "Privileged Access Management optional feature is enabled (scopes: $(@($feature.EnabledScopes) -join ', '))."
}

function Get-AddsPimLocalMachineCertificate {
    <# Validates a certificate exists in LocalMachine\My with a usable
       private key; optionally checks it identifies an expected DNS name. #>
    param(
        [Parameter(Mandatory)] [string] $Thumbprint,
        [string] $ExpectedDnsName
    )
    $normalized = ConvertTo-AddsPimNormalizedThumbprint $Thumbprint
    $certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$normalized" -ErrorAction SilentlyContinue
    if ($null -eq $certificate) { throw "No certificate with thumbprint $normalized was found in Cert:\LocalMachine\My." }
    if (-not $certificate.HasPrivateKey) { throw "Certificate $normalized has no private key in Cert:\LocalMachine\My; the private key must be present on this host." }
    if ($certificate.NotAfter -le (Get-Date)) { throw "Certificate $normalized expired on $($certificate.NotAfter)." }
    if ($ExpectedDnsName -and $certificate.Subject -notmatch [regex]::Escape($ExpectedDnsName)) {
        throw "Certificate $normalized subject '$($certificate.Subject)' does not identify $ExpectedDnsName."
    }
    return $certificate
}

function New-AddsPimSelfSignedCertificate {
    <# Generates a self-signed certificate in LocalMachine\My. Used both
       for the always-self-signed application certificates (Web signing,
       TOTP protection - legitimately self-signed per
       dev-docs/10-operations/web-signing-certificate-operations.md) and,
       when an operator has no CA-issued certificate handy, as an explicit
       opt-in fallback for TLS/mTLS certificates on test hosts. #>
    param(
        [Parameter(Mandatory)] [string] $Subject,
        [Parameter(Mandatory)] [string] $FriendlyName,
        [ValidateSet('DigitalSignature', 'KeyEncipherment', 'ServerAuthentication', 'ClientAuthentication')]
        [string[]] $KeyUsage = @('DigitalSignature'),
        [string[]] $DnsName,
        [int] $KeyLength = 3072,
        [int] $ValidYears = 3
    )
    $params = @{
        Subject          = $Subject
        CertStoreLocation = 'Cert:\LocalMachine\My'
        KeyAlgorithm     = 'RSA'
        KeyLength        = $KeyLength
        HashAlgorithm    = 'SHA256'
        NotAfter         = (Get-Date).AddYears($ValidYears)
        FriendlyName     = $FriendlyName
    }
    if ($DnsName) { $params.DnsName = $DnsName }

    $keyUsageFlags = @()
    if ($KeyUsage -contains 'DigitalSignature') { $keyUsageFlags += 'DigitalSignature' }
    if ($KeyUsage -contains 'KeyEncipherment') { $keyUsageFlags += 'KeyEncipherment' }
    if ($keyUsageFlags.Count -gt 0) { $params.KeyUsage = $keyUsageFlags }

    $ekus = @()
    if ($KeyUsage -contains 'ServerAuthentication') { $ekus += '1.3.6.1.5.5.7.3.1' }
    if ($KeyUsage -contains 'ClientAuthentication') { $ekus += '1.3.6.1.5.5.7.3.2' }
    if ($ekus.Count -gt 0) { $params.TextExtension = @("2.5.29.37={text}$($ekus -join ',')") }

    New-SelfSignedCertificate @params
}

function Grant-AddsPimPrivateKeyRead {
    <# Grants Read access on a certificate's private key file to the named
       account. Works for CNG-backed keys (the default for
       New-SelfSignedCertificate on modern Windows). #>
    param(
        [Parameter(Mandatory)] [string] $Thumbprint,
        [Parameter(Mandatory)] [string] $Account
    )
    $normalized = ConvertTo-AddsPimNormalizedThumbprint $Thumbprint
    $certutilOutput = & certutil -store My $normalized
    $containerLine = $certutilOutput | Select-String 'Unique container name:'
    if (-not $containerLine) { throw "Could not determine the private key container for certificate $normalized via certutil." }
    $container = $containerLine.ToString().Split(':')[1].Trim()
    $keyPath = Join-Path $env:ProgramData "Microsoft\Crypto\Keys\$container"
    if (-not (Test-Path -LiteralPath $keyPath)) {
        $keyPath = Join-Path $env:ProgramData "Microsoft\Crypto\RSA\MachineKeys\$container"
    }
    if (-not (Test-Path -LiteralPath $keyPath)) { throw "Could not locate the private key file for certificate $normalized (container $container)." }
    & icacls $keyPath /grant "${Account}:R" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls failed to grant $Account read access to $keyPath." }
}

function Invoke-AddsPimSqlCmd {
    param([Parameter(Mandatory)] [string[]] $Arguments)
    $sqlcmd = Get-Command sqlcmd.exe -ErrorAction Stop
    & $sqlcmd.Source @Arguments | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with exit code $LASTEXITCODE." }
}
