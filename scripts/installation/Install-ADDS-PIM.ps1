#Requires -Version 5.1
<#
.SYNOPSIS
The single entry point for installing ADDS-PIM on a fresh Windows Server:
host prerequisites through a running, usable instance.

.DESCRIPTION
Asks interactively for every variable it needs (with defaults pre-filled
from a previous run's saved state, so re-running to fix a problem doesn't
mean re-typing everything), then runs each numbered step script in
scripts\installation\ in order:

  01_HostPrerequisites.ps1          IIS, .NET Hosting Bundle, RSAT AD tools, optional SQL Express
  02_CertificatesAndIdentities.ps1  Resolve/validate the writable DC, check the PAM feature, validate gMSAs, prepare certificates
  03_PublishApplication.ps1         dotnet publish Web, API, AD Worker
  04_InitializeDatabase.ps1         EF migrations, DB bootstrap/grants, DirectoryScope
  05_RegisterWebSigningCertificate.ps1  Register Web signing key (ADR-0011)
  06_WriteApplicationSettings.ps1   appsettings.Production.json for Web/API
  07_ConfigureIis.ps1               IIS app pools and HTTPS sites
  08_ConfigureWorkerService.ps1     Worker registry config and Windows service
  09_Verify.ps1                     Smoke test and next-steps summary

Every step script is independently runnable and re-runnable; this
orchestrator is a thin, transparent sequencer over them - nothing it does
is hidden inside a script outside this folder.

Explicitly out of scope, by design: creating gMSAs, delegating Active
Directory rights, DNS records, firewall rules, and CA-issued certificate
procurement remain separately-approved operator actions (see
dev-docs/03-security/least-privilege-and-gmsa.md). This installer
validates those exist; it never creates them.

.PARAMETER StartAtStep
Resume a previous run starting at this step number (1-9), reusing the
saved state from prior steps instead of re-running them. Requires a state
file from a previous run in the same -StatePath.

.PARAMETER StatePath
Where interactive answers and step outputs are persisted between runs, so
a failed install can be resumed without re-answering every prompt or
re-generating certificates. Defaults to
C:\ProgramData\ADDS-PIM\Install-State.json.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateRange(1, 9)] [int] $StartAtStep = 1,
    [ValidateNotNullOrEmpty()] [string] $StatePath = 'C:\ProgramData\ADDS-PIM\Install-State.json'
)

. (Join-Path $PSScriptRoot '_Common.ps1')
Assert-AddsPimElevated

Write-Host ''
Write-Host '#############################################' -ForegroundColor Cyan
Write-Host '#  ADDS-PIM installer                       #' -ForegroundColor Cyan
Write-Host '#############################################' -ForegroundColor Cyan

function Get-AddsPimSavedState {
    param([string] $Path)
    if (Test-Path -LiteralPath $Path) {
        try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
        catch { Write-Warning "Could not read saved state at $Path ($_); starting fresh."; return $null }
    }
    return $null
}

function Save-AddsPimState {
    param([Parameter(Mandatory)] [hashtable] $State, [Parameter(Mandatory)] [string] $Path)
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) { $null = New-Item -ItemType Directory -Path $directory -Force }
    ($State | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $Path -Encoding utf8
}

function Get-AddsPimStateValue {
    param($SavedState, [Parameter(Mandatory)] [string] $Name, $Default)
    if ($null -ne $SavedState -and $SavedState.PSObject.Properties.Name -contains $Name) { return $SavedState.$Name }
    return $Default
}

$saved = Get-AddsPimSavedState -Path $StatePath
if ($StartAtStep -gt 1 -and $null -eq $saved) {
    throw "No saved state found at $StatePath. Run without -StartAtStep first, or point -StatePath at an existing state file."
}

$state = @{}
if ($saved) { $saved.PSObject.Properties | ForEach-Object { $state[$_.Name] = $_.Value } }

if ($StartAtStep -eq 1) {
    Write-Host ''
    Write-Host 'Answer the following questions. Defaults in [brackets] come from a previous run, if any.' -ForegroundColor Cyan

    $state.DomainDnsName = Read-AddsPimValue -Prompt 'Domain DNS name (e.g. home.local)' -Default (Get-AddsPimStateValue $saved 'DomainDnsName')
    $state.ForestDnsName = Read-AddsPimValue -Prompt 'Forest DNS name' -Default (Get-AddsPimStateValue $saved 'ForestDnsName' $state.DomainDnsName)
    $state.DomainController = Read-AddsPimOptionalValue -Prompt 'Writable domain controller FQDN (blank = auto-discover after step 01)' -Default (Get-AddsPimStateValue $saved 'DomainController')

    $state.WebGmsaAccount = Read-AddsPimValue -Prompt 'Web gMSA account (DOMAIN\name$)' -Default (Get-AddsPimStateValue $saved 'WebGmsaAccount')
    $state.ApiGmsaAccount = Read-AddsPimValue -Prompt 'API gMSA account (DOMAIN\name$)' -Default (Get-AddsPimStateValue $saved 'ApiGmsaAccount')
    $state.WorkerGmsaAccount = Read-AddsPimValue -Prompt 'Worker gMSA account (DOMAIN\name$)' -Default (Get-AddsPimStateValue $saved 'WorkerGmsaAccount')

    $state.WebHostName = Read-AddsPimValue -Prompt 'Web DNS host name' -Default (Get-AddsPimStateValue $saved 'WebHostName')
    $state.ApiHostName = Read-AddsPimValue -Prompt 'API DNS host name' -Default (Get-AddsPimStateValue $saved 'ApiHostName')
    $state.WorkerHostName = Read-AddsPimValue -Prompt 'Worker DNS host name' -Default (Get-AddsPimStateValue $saved 'WorkerHostName')
    $state.WorkerBindAddress = Read-AddsPimValue -Prompt 'Worker bind IP address (non-loopback)' -Default (Get-AddsPimStateValue $saved 'WorkerBindAddress')
    $state.WorkerPort = [int] (Read-AddsPimValue -Prompt 'Worker port' -Default (Get-AddsPimStateValue $saved 'WorkerPort' '8990'))

    $state.SqlServerInstance = Read-AddsPimValue -Prompt 'SQL Server instance (e.g. server.domain.local or server\SQLEXPRESS)' -Default (Get-AddsPimStateValue $saved 'SqlServerInstance')
    $state.DatabaseName = Read-AddsPimValue -Prompt 'Database name' -Default (Get-AddsPimStateValue $saved 'DatabaseName' 'ADDS_PIM')

    $defaultScopeId = Get-AddsPimStateValue $saved 'DirectoryScopeId' ([guid]::NewGuid().ToString())
    $state.DirectoryScopeId = Read-AddsPimValue -Prompt 'DirectoryScope GUID (new environments: keep the generated default)' -Default $defaultScopeId
    $state.UsersGroupObjectGuid = Read-AddsPimValue -Prompt 'AD "PIM users" group objectGUID' -Default (Get-AddsPimStateValue $saved 'UsersGroupObjectGuid')
    $state.AdministratorsGroupObjectGuid = Read-AddsPimValue -Prompt 'AD "PIM administrators" group objectGUID' -Default (Get-AddsPimStateValue $saved 'AdministratorsGroupObjectGuid')

    $state.SigningKeyId = Read-AddsPimValue -Prompt 'Web signing KeyId label (e.g. web-<host>-01)' -Default (Get-AddsPimStateValue $saved 'SigningKeyId')

    $state.PublishRoot = Read-AddsPimValue -Prompt 'Web/API publish root' -Default (Get-AddsPimStateValue $saved 'PublishRoot' 'C:\ADDS-PIM\Publish')
    $state.WorkerPublishRoot = Read-AddsPimValue -Prompt 'Worker publish root' -Default (Get-AddsPimStateValue $saved 'WorkerPublishRoot' 'C:\ADDS-PIM\Worker')

    Save-AddsPimState -State $state -Path $StatePath
}
else {
    Write-Host "Resuming from step $StartAtStep using saved state at $StatePath." -ForegroundColor Cyan
}

$whatIf = $WhatIfPreference

if ($StartAtStep -le 1) {
    & (Join-Path $PSScriptRoot '01_HostPrerequisites.ps1') -WhatIf:$whatIf
    Save-AddsPimState -State $state -Path $StatePath
}

if ($StartAtStep -le 2) {
    # RSAT AD tools exist now (step 01), so resolve/validate the writable
    # domain controller here rather than at the upfront prompt. The result
    # is pinned into state and flows unchanged into appsettings and the
    # worker config (ADR-0006: one configured controller for the write,
    # the precondition lookup, and every verification read).
    $preferredController = if ([string]::IsNullOrWhiteSpace([string] $state.DomainController)) { $null } else { [string] $state.DomainController }
    $state.DomainController = Resolve-AddsPimWritableDomainController -DomainDnsName $state.DomainDnsName -Preferred $preferredController
    Write-Host "Writable domain controller: $($state.DomainController)" -ForegroundColor Cyan
    Save-AddsPimState -State $state -Path $StatePath

    $certs = & (Join-Path $PSScriptRoot '02_CertificatesAndIdentities.ps1') `
        -WebGmsaAccount $state.WebGmsaAccount -ApiGmsaAccount $state.ApiGmsaAccount -WorkerGmsaAccount $state.WorkerGmsaAccount `
        -WebHostName $state.WebHostName -ApiHostName $state.ApiHostName -WorkerHostName $state.WorkerHostName `
        -DomainController $state.DomainController -WhatIf:$whatIf
    if ($certs) {
        $state.WebTlsCertificateThumbprint = $certs.WebTlsCertificateThumbprint
        $state.ApiTlsCertificateThumbprint = $certs.ApiTlsCertificateThumbprint
        $state.WorkerTlsCertificateThumbprint = $certs.WorkerTlsCertificateThumbprint
        $state.ApiWorkerClientCertificateThumbprint = $certs.ApiWorkerClientCertificateThumbprint
        $state.WebSigningCertificateThumbprint = $certs.WebSigningCertificateThumbprint
        $state.TotpProtectionCertificateThumbprint = $certs.TotpProtectionCertificateThumbprint
    }
    Save-AddsPimState -State $state -Path $StatePath
}

if ($StartAtStep -le 3) {
    $publish = & (Join-Path $PSScriptRoot '03_PublishApplication.ps1') -PublishRoot $state.PublishRoot -WorkerPublishRoot $state.WorkerPublishRoot -WhatIf:$whatIf
    if ($publish) {
        $state.WebPublishPath = $publish.WebPublishPath
        $state.ApiPublishPath = $publish.ApiPublishPath
        $state.WorkerPublishPath = $publish.WorkerPublishPath
        $state.WorkerAssemblyPath = $publish.WorkerAssemblyPath
        $state.InfrastructureProjectPath = $publish.InfrastructureProjectPath
    }
    Save-AddsPimState -State $state -Path $StatePath
}

if ($StartAtStep -le 4) {
    $database = & (Join-Path $PSScriptRoot '04_InitializeDatabase.ps1') `
        -SqlServerInstance $state.SqlServerInstance -WorkerGmsaAccount $state.WorkerGmsaAccount -ApiGmsaAccount $state.ApiGmsaAccount `
        -DirectoryScopeId $state.DirectoryScopeId -DomainDnsName $state.DomainDnsName -ForestDnsName $state.ForestDnsName `
        -InfrastructureProjectPath $state.InfrastructureProjectPath -DatabaseName $state.DatabaseName -WhatIf:$whatIf
    if ($database) { $state.SqlConnectionString = $database.SqlConnectionString }
    Save-AddsPimState -State $state -Path $StatePath
}

if ($StartAtStep -le 5) {
    & (Join-Path $PSScriptRoot '05_RegisterWebSigningCertificate.ps1') `
        -SqlConnectionString $state.SqlConnectionString -Thumbprint $state.WebSigningCertificateThumbprint -KeyId $state.SigningKeyId -WhatIf:$whatIf
    Save-AddsPimState -State $state -Path $StatePath
}

if ($StartAtStep -le 6) {
    & (Join-Path $PSScriptRoot '06_WriteApplicationSettings.ps1') `
        -WebPublishPath $state.WebPublishPath -ApiPublishPath $state.ApiPublishPath `
        -WebHostName $state.WebHostName -ApiHostName $state.ApiHostName -WorkerHostName $state.WorkerHostName -WorkerPort $state.WorkerPort `
        -DirectoryScopeId $state.DirectoryScopeId -DomainDnsName $state.DomainDnsName -ForestDnsName $state.ForestDnsName -DomainController $state.DomainController `
        -UsersGroupObjectGuid $state.UsersGroupObjectGuid -AdministratorsGroupObjectGuid $state.AdministratorsGroupObjectGuid `
        -SigningCertificateThumbprint $state.WebSigningCertificateThumbprint -SigningKeyId $state.SigningKeyId `
        -SqlConnectionString $state.SqlConnectionString -ApiWorkerClientCertificateThumbprint $state.ApiWorkerClientCertificateThumbprint `
        -WorkerTlsCertificateThumbprint $state.WorkerTlsCertificateThumbprint -TotpProtectionCertificateThumbprint $state.TotpProtectionCertificateThumbprint -WhatIf:$whatIf
    Save-AddsPimState -State $state -Path $StatePath
}

if ($StartAtStep -le 7) {
    & (Join-Path $PSScriptRoot '07_ConfigureIis.ps1') `
        -WebPublishPath $state.WebPublishPath -ApiPublishPath $state.ApiPublishPath `
        -WebGmsaAccount $state.WebGmsaAccount -ApiGmsaAccount $state.ApiGmsaAccount `
        -WebTlsCertificateThumbprint $state.WebTlsCertificateThumbprint -ApiTlsCertificateThumbprint $state.ApiTlsCertificateThumbprint `
        -WebHostName $state.WebHostName -ApiHostName $state.ApiHostName -WhatIf:$whatIf
    Save-AddsPimState -State $state -Path $StatePath
}

if ($StartAtStep -le 8) {
    & (Join-Path $PSScriptRoot '08_ConfigureWorkerService.ps1') `
        -WorkerAssemblyPath $state.WorkerAssemblyPath -WorkerGmsaAccount $state.WorkerGmsaAccount `
        -BindAddress $state.WorkerBindAddress -Port $state.WorkerPort `
        -ServerCertificateThumbprint $state.WorkerTlsCertificateThumbprint -AllowedApiClientCertificateThumbprint @($state.ApiWorkerClientCertificateThumbprint) `
        -SqlConnectionString $state.SqlConnectionString -DomainController $state.DomainController -WhatIf:$whatIf
    Save-AddsPimState -State $state -Path $StatePath
}

if ($StartAtStep -le 9) {
    & (Join-Path $PSScriptRoot '09_Verify.ps1') -ApiHostName $state.ApiHostName -DirectoryScopeId $state.DirectoryScopeId
}

Write-Host ''
Write-Host "Install state saved to $StatePath (reuse with -StartAtStep to resume a failed run)." -ForegroundColor Cyan
